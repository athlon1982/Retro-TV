using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RetroTv.Guide;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RetroTv.LiveTv;

/// <summary>
/// Live channel streams — one continuous MPEG-TS stream per channel, stitched
/// from the scheduled Internet Archive MP4s with ffmpeg (port of the Stage 2
/// sidecar's encoder supervisor). Anonymous: Jellyfin's Live TV tuner fetches
/// these server-side with no user session. Each request starts its own ffmpeg
/// batch (seeked to the programme in progress, up to 2 h of played time);
/// when the batch ends the client reconnects and the next batch starts at
/// the programme then in progress — the shared-encoder optimization (one
/// ffmpeg per channel, teed to all viewers) is a 0.4.0.0 item. The weather
/// channel is a rendered slate (ffmpeg drawtext over the active theme's
/// background) instead of stitched video.
/// </summary>
[ApiController]
[Route("RetroTv/Live")]
[AllowAnonymous]
public class LiveTvController : ControllerBase
{
    /// <summary>One batch covers up to 2 h of played time (same as the sidecar).</summary>
    public const int BatchSeconds = 7200;

    private static readonly string[] FontCandidates =
    {
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        @"C:\Windows\Fonts\arial.ttf",
        @"C:\Windows\Fonts\segoeui.ttf"
    };

    private readonly ILogger<LiveTvController> _logger;
    private readonly GuideService _guide;
    private readonly WeatherChannel _weather;
    private readonly string _workDir;
    private readonly object _nvencLock = new();
    private bool? _nvencAvailable;

    public LiveTvController(
        ILogger<LiveTvController> logger,
        GuideService guide,
        WeatherChannel weather,
        IApplicationPaths applicationPaths)
    {
        _logger = logger;
        _guide = guide;
        _weather = weather;
        _workDir = Path.Combine(applicationPaths.ProgramDataPath, "RetroTv", "work");
    }

    /// <summary>Stream one channel as MPEG-TS. Tune in mid-show: the batch starts at the programme in progress (seeked), like real TV.</summary>
    [HttpGet("{slug}")]
    public async Task Live(string slug)
    {
        if (!ChannelsEnabled())
        {
            Response.StatusCode = 503;
            await Response.WriteAsync("Retro TV live channels are disabled. Enable them on the Retro TV plugin settings page.").ConfigureAwait(false);
            return;
        }

        var channel = _guide.GetActiveChannels()
            .FirstOrDefault(c => string.Equals(c.Definition.Slug, slug, StringComparison.OrdinalIgnoreCase));
        if (channel is null)
        {
            Response.StatusCode = 404;
            await Response.WriteAsync("Unknown channel: " + slug).ConfigureAwait(false);
            return;
        }

        var ffmpeg = ResolveFfmpegPath();
        if (ffmpeg is null)
        {
            _logger.LogWarning("[RetroTv] No ffmpeg found (set FfmpegPath on the Retro TV plugin settings page, or put ffmpeg on the PATH); cannot stream channel {Slug}.", slug);
            Response.StatusCode = 503;
            await Response.WriteAsync("ffmpeg is not available on this server. Set FfmpegPath on the Retro TV plugin settings page.").ConfigureAwait(false);
            return;
        }

        if (channel.Definition.Weather)
        {
            await StreamWeatherAsync(channel, ffmpeg).ConfigureAwait(false);
            return;
        }

        await StreamChannelAsync(channel, ffmpeg).ConfigureAwait(false);
    }

    private async Task StreamChannelAsync(GuideService.GuideChannel channel, string ffmpeg)
    {
        var now = DateTimeOffset.UtcNow;
        var current = ScheduleEngine.ProgramAt(channel.Items, now);
        var items = new List<(ScheduleEngine.GuideItem Item, double OffsetSec)>();
        var totalPlayed = 0.0;
        var idx = current.Index;
        var t = current.Start;
        while (totalPlayed < BatchSeconds)
        {
            var item = channel.Items[idx % channel.Items.Count];
            var offsetSec = idx == current.Index ? current.Offset.TotalSeconds : 0;
            items.Add((item, offsetSec));
            totalPlayed += item.EffectiveRuntimeSeconds - offsetSec;
            t += TimeSpan.FromSeconds(item.EffectiveRuntimeSeconds);
            idx++;
        }

        // First item must carry an audio stream, or the concat demuxer's audio
        // map is empty for the whole batch (sidecar lesson). Probe it; if
        // silent, advance to the next programme that has audio (max 5 probes).
        var batchStart = items[0].Item;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var hasAudio = await ProbeAudioAsync(items[0].Item.Mp4Url, ffmpeg).ConfigureAwait(false);
            if (hasAudio is null)
            {
                _logger.LogInformation("[RetroTv] [{Slug}] audio probe inconclusive for {Id} — proceeding.", channel.Definition.Slug, items[0].Item.Identifier);
                break;
            }

            if (hasAudio.Value)
            {
                break;
            }

            _logger.LogInformation("[RetroTv] [{Slug}] first item {Id} has no audio — advancing batch start.", channel.Definition.Slug, items[0].Item.Identifier);
            items.RemoveAt(0);
            if (items.Count == 0)
            {
                var nextIdx = (current.Index + 1) % channel.Items.Count;
                items.Add((channel.Items[nextIdx], 0));
            }

            // Re-time from the new batch start and top the batch back up.
            totalPlayed = 0;
            foreach (var (item, _) in items)
            {
                totalPlayed += item.EffectiveRuntimeSeconds;
            }

            var tailIdx = (items.Count == 0 ? current.Index : channel.Items.IndexOf(items[^1].Item) + 1) % channel.Items.Count;
            while (totalPlayed < BatchSeconds)
            {
                var item = channel.Items[tailIdx % channel.Items.Count];
                items.Add((item, 0));
                totalPlayed += item.EffectiveRuntimeSeconds;
                tailIdx++;
            }

            if (attempt == 4)
            {
                _logger.LogWarning("[RetroTv] [{Slug}] audio probe gave up after 5 silent items — proceeding anyway.", channel.Definition.Slug);
            }
        }

        Directory.CreateDirectory(_workDir);
        var listFile = Path.Combine(_workDir, channel.Definition.Slug + "-batch-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ".txt");
        // The concat list. The seek into the programme in progress is done
        // with the concat script's `inpoint` on the first file — NOT with
        // -ss before -i: on a concat input -ss makes the demuxer read
        // linearly to the seek point at real-time speed (sidecar measured
        // 79.6 MB / 45 s for an 1105 s seek; inpoint seeks the child file
        // directly, first bytes in ~0.5 s). This plugin port originally
        // used -ss and produced zero bytes for the whole seek — fixed
        // 2026-11-08 during the 0.2.0.0 load test.
        var listLines = new List<string> { "ffconcat version 1.0" };
        for (var i = 0; i < items.Count; i++)
        {
            listLines.Add("file '" + (items[i].Item.Mp4Url ?? string.Empty).Replace("'", "'\\''", StringComparison.Ordinal) + "'");
            if (i == 0 && items[i].OffsetSec > 0)
            {
                listLines.Add("inpoint " + items[i].OffsetSec.ToString("F3", CultureInfo.InvariantCulture));
            }
        }

        await System.IO.File.WriteAllTextAsync(listFile, string.Join("\n", listLines) + "\n", HttpContext.RequestAborted).ConfigureAwait(false);

        // NVENC can still die at encoder init even after a passing probe
        // (driver hiccup, another process holding the encoder). If the
        // first attempt produced ZERO bytes, nothing reached the client
        // yet, so one retry with the software encoder is safe — the tuner
        // just sees the stream start a little later instead of a dead
        // channel.
        var useNvenc = UseNvenc(ffmpeg);
        var args = BuildFfmpegArgs(listFile, useNvenc);
        var (bytes, code) = await RunFfmpegToResponseAsync(ffmpeg, args, listFile, channel.Definition.Slug, "batch of " + items.Count + " programmes from " + batchStart.Identifier).ConfigureAwait(false);
        if (bytes == 0 && code != 0 && useNvenc)
        {
            _logger.LogWarning("[RetroTv] [{Slug}] NVENC encode failed at startup (exit {Code}); retrying this batch with libx264.", channel.Definition.Slug, code);
            args = BuildFfmpegArgs(listFile, nvenc: false);
            await RunFfmpegToResponseAsync(ffmpeg, args, listFile, channel.Definition.Slug, "batch of " + items.Count + " programmes from " + batchStart.Identifier + " (libx264 fallback)").ConfigureAwait(false);
        }
    }

    private async Task StreamWeatherAsync(GuideService.GuideChannel channel, string ffmpeg)
    {
        var font = ResolveFontPath();
        if (font is null)
        {
            _logger.LogWarning("[RetroTv] No font found for the weather slate (set WeatherFontPath, or install DejaVu Sans / Arial); the weather channel cannot stream.");
            Response.StatusCode = 503;
            await Response.WriteAsync("No font is available for the weather slate. Set WeatherFontPath on the Retro TV plugin settings page.").ConfigureAwait(false);
            return;
        }

        // The current weather segment and its remaining time (max one segment).
        await _weather.RefreshAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var programmes = _weather.GetSchedule(now, 1);
        if (programmes.Count == 0)
        {
            Response.StatusCode = 503;
            await Response.WriteAsync("Weather schedule is unavailable.").ConfigureAwait(false);
            return;
        }

        var current = programmes[0];
        var seconds = Math.Clamp((current.Stop - now).TotalSeconds, 30, WeatherChannel.SegmentSeconds);
        var lines = _weather.BuildSlateLines(current.Item.Identifier);
        var (bg, fg, accent) = ThemeColors();
        var drawtext = BuildDrawtextFilters(font, lines, fg, accent);
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "info",
            "-re",
            "-f", "lavfi", "-i", "color=c=" + bg + ":s=640x480:r=30",
            "-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000",
            "-vf", drawtext,
            "-t", ((int)Math.Ceiling(seconds)).ToString(CultureInfo.InvariantCulture),
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "23",
            "-c:a", "aac", "-b:a", "128k", "-ac", "2", "-ar", "48000",
            "-g", "60", "-keyint_min", "60", "-sc_threshold", "0",
            "-f", "mpegts", "pipe:1"
        };
        await RunFfmpegToResponseAsync(ffmpeg, args, null, channel.Definition.Slug, "weather slate: " + current.Item.Title + " (" + (int)seconds + "s)").ConfigureAwait(false);
    }

    /// <summary>Run ffmpeg, copying its MPEG-TS stdout to the response. Returns (bytes written, ffmpeg exit code). Bytes are counted with a manual copy loop so the caller can tell a dead-at-startup encode (0 bytes) from a stream that died mid-way.</summary>
    private async Task<(long BytesWritten, int ExitCode)> RunFfmpegToResponseAsync(string ffmpeg, List<string> args, string? listFile, string slug, string what)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        Process? proc = null;
        var stderr = new StringBuilder();
        long bytesWritten = 0;
        var exitCode = -1;
        try
        {
            proc = Process.Start(psi);
            if (proc is null)
            {
                Response.StatusCode = 500;
                await Response.WriteAsync("ffmpeg failed to start.").ConfigureAwait(false);
                return (0, -1);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var err = await proc.StandardError.ReadToEndAsync().ConfigureAwait(false);
                    stderr.Append(err);
                }
                catch
                {
                    // Best effort only.
                }
            });

            _logger.LogInformation("[RetroTv] [{Slug}] streaming {What}.", slug, what);
            Response.ContentType = "video/MP2T";
            Response.Headers.CacheControl = "no-cache";
            var token = HttpContext.RequestAborted;
            try
            {
                var buffer = new byte[65536];
                int read;
                while ((read = await proc.StandardOutput.BaseStream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    await Response.Body.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    bytesWritten += read;
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected — normal for a live channel.
            }

            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Already gone.
            }

            try
            {
                await proc.WaitForExitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Client disconnected — the process was killed above.
            }

            // ExitCode throws InvalidOperationException while the process is
            // still running (e.g. the kill above hasn't landed yet after a
            // client disconnect) — only read it once it has exited.
            exitCode = proc.HasExited ? proc.ExitCode : -1;
            if (exitCode != 0 && exitCode != -1)
            {
                var tail = stderr.Length > 2000 ? stderr.ToString()[^2000..] : stderr.ToString();
                _logger.LogWarning("[RetroTv] [{Slug}] ffmpeg exited {Code} while streaming {What}. stderr tail: {Tail}", slug, exitCode, what, tail);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[RetroTv] [{Slug}] stream failed for {What}: {Message}", slug, what, ex.Message);
            if (!Response.HasStarted)
            {
                Response.StatusCode = 500;
                await Response.WriteAsync("Stream failed: " + ex.Message).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                if (proc is not null && !proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Already gone.
            }

            proc?.Dispose();
            if (listFile is not null)
            {
                try
                {
                    System.IO.File.Delete(listFile);
                }
                catch
                {
                    // Best effort only.
                }
            }
        }

        return (bytesWritten, exitCode);
    }

    private List<string> BuildFfmpegArgs(string listFile, bool nvenc)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "info",
            // The concat demuxer only allows file/crypto/data by default — https
            // sources need an explicit protocol whitelist. httpproxy is included
            // because ffmpeg honours http_proxy/https_proxy env vars; harmless
            // where no proxy is set. (Sidecar lesson: -reconnect is rejected
            // with the concat demuxer — resilience is the batch restart, not
            // reconnect. And the seek lives in the list file as `inpoint`,
            // never as -ss before -i — see the list-file writer.)
            "-protocol_whitelist", "file,http,https,tcp,tls,crypto,data,httpproxy",
            "-re"
        };

        args.Add("-f");
        args.Add("concat");
        args.Add("-safe");
        args.Add("0");
        args.Add("-i");
        args.Add(listFile);
        args.Add("-map");
        args.Add("0:v:0");
        args.Add("-map");
        args.Add("0:a:0?");
        args.Add("-vf");
        args.Add("scale=640:480:force_original_aspect_ratio=decrease,pad=640:480:(ow-iw)/2:(oh-ih)/2,fps=30,format=yuv420p");
        if (nvenc)
        {
            args.Add("-c:v");
            args.Add("h264_nvenc");
            args.Add("-gpu");
            args.Add("0");
            args.Add("-preset");
            args.Add("p4");
            args.Add("-cq");
            args.Add("23");
        }
        else
        {
            args.Add("-c:v");
            args.Add("libx264");
            args.Add("-preset");
            args.Add("veryfast");
            args.Add("-crf");
            args.Add("23");
        }

        args.Add("-c:a");
        args.Add("aac");
        args.Add("-b:a");
        args.Add("128k");
        args.Add("-ac");
        args.Add("2");
        args.Add("-ar");
        args.Add("48000");
        args.Add("-g");
        args.Add("60");
        args.Add("-keyint_min");
        args.Add("60");
        args.Add("-sc_threshold");
        args.Add("0");
        args.Add("-f");
        args.Add("mpegts");
        args.Add("pipe:1");
        return args;
    }

    private static string BuildDrawtextFilters(string font, List<string> lines, string textColor, string accentColor)
    {
        // One drawtext filter per line. Text is escaped for the drawtext
        // parser (: % ' \ all need a backslash).
        var filters = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var color = i == 0 ? accentColor : textColor;
            var size = i == 0 ? 30 : 26;
            var y = 36 + i * 42;
            filters.Add("drawtext=fontfile='" + font.Replace("'", "'\\''", StringComparison.Ordinal) + "':text='" + EscapeDrawtext(lines[i]) + "':fontcolor=" + color + ":fontsize=" + size.ToString(CultureInfo.InvariantCulture) + ":x=32:y=" + y.ToString(CultureInfo.InvariantCulture));
        }

        return string.Join(",", filters);
    }

    private static string EscapeDrawtext(string s)
    {
        return s
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "'\\''", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal);
    }

    private (string Bg, string Fg, string Accent) ThemeColors()
    {
        // The active guide theme's colors, as 0xRRGGBB for ffmpeg lavfi.
        var theme = Plugin.Instance?.Configuration.ActiveGuideTheme ?? "woodgrain-78";
        return theme switch
        {
            "neon-nights" => ("0x0a0a1e", "0xe8f6ff", "0xff2fb3"),
            "vhs-blue-screen" => ("0x0000a8", "0xffffff", "0xffff00"),
            _ => ("0x2b1d12", "0xf3e6c8", "0xe8a33d")
        };
    }

    private bool UseNvenc(string ffmpeg)
    {
        lock (_nvencLock)
        {
            if (_nvencAvailable.HasValue)
            {
                return _nvencAvailable.Value;
            }
        }

        // A real test encode, not just "-encoders": ffmpeg lists h264_nvenc
        // whenever it was built with NVENC support, even on a machine with
        // no NVIDIA GPU at all (this VM proves it — the list check said
        // "available" and the real encode then died with exit 255). A
        // 1-frame null-source encode fails fast when there is no usable
        // NVENC device, so the result is trustworthy.
        var available = false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("lavfi");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add("nullsrc=s=64x64:d=0.1");
            psi.ArgumentList.Add("-c:v");
            psi.ArgumentList.Add("h264_nvenc");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("null");
            psi.ArgumentList.Add("-");
            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                var exited = proc.WaitForExit(15000);
                if (!exited && !proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
                else
                {
                    available = proc.ExitCode == 0;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RetroTv] NVENC probe failed; using the software encoder: {Message}", ex.Message);
        }

        lock (_nvencLock)
        {
            _nvencAvailable = available;
        }

        _logger.LogInformation("[RetroTv] Encoder: {Encoder} (NVENC {Available}).", available ? "h264_nvenc" : "libx264", available ? "available" : "not available");
        return available;
    }

    /// <summary>ffprobe: does this URL carry an audio stream? Null = inconclusive (probe failed / ffprobe missing) — proceed.</summary>
    private async Task<bool?> ProbeAudioAsync(string? url, string ffmpeg)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var ffprobe = ResolveFfprobePath(ffmpeg);
        if (ffprobe is null)
        {
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-select_streams");
            psi.ArgumentList.Add("a:0");
            psi.ArgumentList.Add("-show_entries");
            psi.ArgumentList.Add("stream=codec_type");
            psi.ArgumentList.Add("-of");
            psi.ArgumentList.Add("csv=p=0");
            psi.ArgumentList.Add(url);
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return null;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token).ConfigureAwait(false);
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return stdout.Contains("audio", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation("[RetroTv] Audio probe failed for {Url} (proceeding): {Message}", url, ex.Message);
            return null;
        }
    }

    private string? ResolveFfmpegPath()
    {
        var configured = Plugin.Instance?.Configuration.FfmpegPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        // "ffmpeg" on the PATH — the OS resolves it at Process.Start time.
        return "ffmpeg";
    }

    private static string? ResolveFfprobePath(string ffmpeg)
    {
        try
        {
            var dir = Path.GetDirectoryName(ffmpeg);
            if (!string.IsNullOrEmpty(dir))
            {
                var candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
                if (System.IO.File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            // Fall through to PATH.
        }

        return "ffprobe";
    }

    private string? ResolveFontPath()
    {
        var configured = Plugin.Instance?.Configuration.WeatherFontPath;
        if (!string.IsNullOrWhiteSpace(configured) && System.IO.File.Exists(configured.Trim()))
        {
            return configured.Trim();
        }

        foreach (var candidate in FontCandidates)
        {
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private bool ChannelsEnabled()
    {
        return Plugin.Instance?.Configuration.ChannelsEnabled ?? false;
    }
}
