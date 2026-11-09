using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RetroTv.Catalog;
using Jellyfin.Plugin.RetroTv.Guide.Channels;
using Jellyfin.Plugin.RetroTv.Weather;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RetroTv.Guide;

/// <summary>
/// The channel guide service — loads the lineup and the channel pools,
/// computes deterministic schedules, and builds the M3U + XMLTV + guide
/// JSON the plugin serves. Pools come from the plugin data dir
/// (pools.json); on first use the verified Stage 2 seed pools are copied
/// there. A catalog build refreshes the pools from catalog.json so the
/// guide grows with the catalog. Linear is PD-only in v0.2.0.0 — the
/// user-uploaded tier stays on-demand only.
/// </summary>
public class GuideService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly ILogger<GuideService> _logger;
    private readonly WeatherChannel _weather;
    private readonly string _poolsPath;
    private readonly object _lock = new();
    private IReadOnlyList<ChannelDefinition> _lineup = Array.Empty<ChannelDefinition>();
    private List<GuideChannel> _channels = new();
    private bool _loaded;

    public GuideService(IApplicationPaths applicationPaths, ILogger<GuideService> logger, WeatherChannel weather)
    {
        _logger = logger;
        _weather = weather;
        _poolsPath = Path.Combine(applicationPaths.ProgramDataPath, "RetroTv", "pools.json");
        _lineup = ChannelLineup.Load(logger);
    }

    /// <summary>One active channel: its lineup definition + its ordered pool items.</summary>
    public class GuideChannel
    {
        public required ChannelDefinition Definition { get; init; }
        public required List<ScheduleEngine.GuideItem> Items { get; init; }
    }

    /// <summary>Active channels (pool items &gt;= 1, or the weather channel), in lineup order.</summary>
    public IReadOnlyList<GuideChannel> GetActiveChannels()
    {
        EnsureLoaded();
        lock (_lock)
        {
            return _channels.ToList();
        }
    }

    /// <summary>All lineup definitions (active or not), in number order — for the config page / diagnostics.</summary>
    public IReadOnlyList<ChannelDefinition> GetLineup()
    {
        EnsureLoaded();
        return _lineup;
    }

    /// <summary>Schedule for one channel from <paramref name="from"/> for <paramref name="hours"/> hours.</summary>
    public List<ScheduleEngine.ScheduledProgramme> GetSchedule(GuideChannel channel, DateTimeOffset from, double hours)
    {
        if (channel.Definition.Weather)
        {
            return _weather.GetSchedule(from, hours);
        }

        return ScheduleEngine.ScheduleFrom(channel.Items, from, hours);
    }

    /// <summary>The programme playing on a channel right now (null when the channel has no items).</summary>
    public ScheduleEngine.ScheduledProgramme? GetNowPlaying(GuideChannel channel, DateTimeOffset at)
    {
        if (channel.Definition.Weather)
        {
            var progs = _weather.GetSchedule(at, 1);
            return progs.Count == 0 ? null : progs[0];
        }

        return channel.Items.Count == 0 ? null : ScheduleEngine.ProgramAt(channel.Items, at);
    }

    /// <summary>Rebuild the pools from the catalog the builder just wrote. Best-effort: a failure is logged and the old pools keep serving — it never fails the build.</summary>
    public bool RefreshPoolsFromCatalog(string catalogPath)
    {
        try
        {
            if (!File.Exists(catalogPath))
            {
                _logger.LogWarning("[RetroTv] Cannot refresh guide pools: catalog.json not found at {Path}", catalogPath);
                return false;
            }

            var text = File.ReadAllText(catalogPath);
            var doc = JsonSerializer.Deserialize<RetroTvCatalogBuilder.CatalogDocument>(text, JsonOptions);
            if (doc?.Items is null)
            {
                _logger.LogWarning("[RetroTv] Cannot refresh guide pools: catalog.json at {Path} did not parse as a catalog.", catalogPath);
                return false;
            }

            var pools = new PoolsDocument { Library = doc.Library, GeneratedAt = DateTimeOffset.UtcNow.ToString("o") };
            var total = 0;
            foreach (var def in _lineup)
            {
                if (!def.Enabled || def.Weather || def.Tier != "pd" || string.IsNullOrEmpty(def.Folder))
                {
                    continue;
                }

                var items = doc.Items
                    .Where(e => e.Tier == "pd" && def.Matches(e.Folder, e.Title, e.Description, e.Year))
                    .Select(e => new PoolItem
                    {
                        Identifier = e.Identifier,
                        Title = e.Title,
                        Year = e.Year,
                        RuntimeSeconds = e.RuntimeSeconds ?? ScheduleEngine.DefaultRuntimeSeconds,
                        LicenseUrl = e.LicenseUrl,
                        Tier = e.Tier,
                        Collection = e.Collection,
                        Mp4Url = e.Mp4Url,
                        PosterUrl = e.PosterUrl,
                        Description = e.Description
                    })
                    .ToList();
                if (def.OldestFirst)
                {
                    // Soaps play oldest episode first.
                    items.Sort((a, b) =>
                    {
                        var ya = a.Year ?? int.MaxValue;
                        var yb = b.Year ?? int.MaxValue;
                        var byYear = ya.CompareTo(yb);
                        return byYear != 0 ? byYear : string.Compare(a.Title, b.Title, StringComparison.Ordinal);
                    });
                }

                pools.Channels.Add(new PoolChannel
                {
                    Id = def.ChannelId,
                    Slug = def.Slug,
                    Name = def.Name,
                    Tier = def.Tier,
                    Items = items
                });
                total += items.Count;
            }

            pools.Counts = new PoolsCounts { Channels = pools.Channels.Count, Items = total };
            Directory.CreateDirectory(Path.GetDirectoryName(_poolsPath)!);
            File.WriteAllText(_poolsPath, JsonSerializer.Serialize(pools, JsonOptions) + "\n");
            _logger.LogInformation("[RetroTv] Guide pools refreshed from catalog: {Channels} channels, {Items} items -> {Path}", pools.Channels.Count, total, _poolsPath);

            lock (_lock)
            {
                _loaded = false;
            }

            EnsureLoaded();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RetroTv] Guide pool refresh from catalog failed (old pools keep serving): {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>M3U playlist for Jellyfin's Live TV tuner. Stream URLs are absolute (built from the request host) — Jellyfin fetches them server-side.</summary>
    public string BuildM3u(string baseUrl)
    {
        var lines = new List<string> { "#EXTM3U x-tvg-url=\"" + baseUrl + "/RetroTv/guide.xml\"" };
        foreach (var ch in GetActiveChannels())
        {
            var def = ch.Definition;
            lines.Add("#EXTINF:-1 tvg-id=\"" + def.ChannelId + "\" tvg-chno=\"" + def.Number.ToString(CultureInfo.InvariantCulture) + "\" tvg-name=\"" + XmlEscape(def.Name) + "\" tvg-logo=\"" + baseUrl + "/RetroTv/Guide/Icon\" group-title=\"Retro TV\"," + def.Name);
            lines.Add(baseUrl + "/RetroTv/Live/" + def.Slug);
        }

        return string.Join("\n", lines) + "\n";
    }

    /// <summary>XMLTV guide (48 h by default) for Jellyfin's XMLTV provider. Same schedule the guide page and the streams use, so they can never disagree.</summary>
    public string BuildXmltv(double hours = 48)
    {
        var now = DateTimeOffset.UtcNow;
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<tv generator-info-name=\"Retro TV\" generator-info-url=\"https://github.com/athlon1982/retro-tv\">\n");
        foreach (var ch in GetActiveChannels())
        {
            sb.Append("  <channel id=\"").Append(ch.Definition.ChannelId).Append("\">\n");
            sb.Append("    <display-name>").Append(XmlEscape(ch.Definition.Name)).Append("</display-name>\n");
            sb.Append("  </channel>\n");
        }

        foreach (var ch in GetActiveChannels())
        {
            foreach (var p in GetSchedule(ch, now, hours))
            {
                sb.Append("  <programme start=\"").Append(XmltvTime(p.Start)).Append("\" stop=\"").Append(XmltvTime(p.Stop)).Append("\" channel=\"").Append(ch.Definition.ChannelId).Append("\">\n");
                sb.Append("    <title>").Append(XmlEscape(p.Item.Title)).Append("</title>\n");
                if (!string.IsNullOrWhiteSpace(p.Item.Description))
                {
                    sb.Append("    <desc>").Append(XmlEscape(p.Item.Description)).Append("</desc>\n");
                }

                if (!string.IsNullOrWhiteSpace(p.Item.PosterUrl))
                {
                    sb.Append("    <icon src=\"").Append(XmlEscape(p.Item.PosterUrl)).Append("\"/>\n");
                }

                sb.Append("    <category>Retro TV</category>\n");
                sb.Append("    <category>").Append(p.Item.Tier == "user-uploaded" ? "User-Uploaded Recording" : "Public Domain").Append("</category>\n");
                sb.Append("  </programme>\n");
            }
        }

        sb.Append("</tv>\n");
        return sb.ToString();
    }

    /// <summary>Guide data as JSON — the same schedule the XMLTV feed carries, for the plugin's own guide page.</summary>
    public object BuildGuideJson(double hours = 48)
    {
        var now = DateTimeOffset.UtcNow;
        return new
        {
            library = "Retro TV",
            generated_at = now.ToString("o"),
            hours,
            anchor = ScheduleEngine.Anchor.ToString("o"),
            attribution = "Program data from the Internet Archive. Weather data by Open-Meteo.com.",
            channels = GetActiveChannels().Select(ch => new
            {
                id = ch.Definition.ChannelId,
                slug = ch.Definition.Slug,
                number = ch.Definition.Number,
                name = ch.Definition.Name,
                group = ch.Definition.Group,
                tier = ch.Definition.Tier,
                weather = ch.Definition.Weather,
                programs = GetSchedule(ch, now, hours).Select(p => new
                {
                    start = p.Start.ToString("o"),
                    stop = p.Stop.ToString("o"),
                    title = p.Item.Title,
                    year = p.Item.Year,
                    description = p.Item.Description,
                    identifier = p.Item.Identifier,
                    poster_url = p.Item.PosterUrl,
                    tier = p.Item.Tier,
                    runtime_seconds = p.Item.EffectiveRuntimeSeconds
                }).ToList()
            }).ToList()
        };
    }

    private void EnsureLoaded()
    {
        lock (_lock)
        {
            if (_loaded)
            {
                return;
            }

            _channels = LoadPools();
            _loaded = true;
        }
    }

    private List<GuideChannel> LoadPools()
    {
        var channels = new List<GuideChannel>();
        PoolsDocument? pools = null;

        try
        {
            if (File.Exists(_poolsPath))
            {
                pools = JsonSerializer.Deserialize<PoolsDocument>(File.ReadAllText(_poolsPath), JsonOptions);
            }
            else
            {
                // First use: seed from the verified Stage 2 pools (175 items, PD-only).
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("pools-seed.json", StringComparison.OrdinalIgnoreCase));
                if (resourceName is not null)
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName)!;
                    using var reader = new StreamReader(stream);
                    var seed = reader.ReadToEnd();
                    pools = JsonSerializer.Deserialize<PoolsDocument>(seed, JsonOptions);
                    if (pools is not null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(_poolsPath)!);
                        File.WriteAllText(_poolsPath, seed);
                        _logger.LogInformation("[RetroTv] Seeded guide pools from the built-in snapshot: {Channels} channels, {Items} items.", pools.Channels.Count, pools.Channels.Sum(c => c.Items.Count));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RetroTv] Guide pools failed to load ({Path}): {Message}", _poolsPath, ex.Message);
            pools = null;
        }

        if (pools is null)
        {
            return channels;
        }

        // Match seed pools to lineup definitions by NAME (the seed uses the
        // sidecar's slugs; the lineup owns the served slug/number).
        var defByName = _lineup
            .Where(d => d.Enabled && !d.Weather && !string.IsNullOrEmpty(d.Folder))
            .ToDictionary(d => d.Name, d => d, StringComparer.OrdinalIgnoreCase);
        foreach (var pool in pools.Channels)
        {
            if (!defByName.TryGetValue(pool.Name, out var def))
            {
                _logger.LogInformation("[RetroTv] Pool channel \"{Name}\" has no lineup definition and was ignored.", pool.Name);
                continue;
            }

            var items = pool.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Mp4Url) && i.RuntimeSeconds > 0)
                .Select(i => new ScheduleEngine.GuideItem
                {
                    Identifier = i.Identifier,
                    Title = i.Title,
                    Year = i.Year,
                    RuntimeSeconds = i.RuntimeSeconds,
                    Description = i.Description ?? string.Empty,
                    Mp4Url = i.Mp4Url,
                    PosterUrl = i.PosterUrl,
                    Tier = string.IsNullOrWhiteSpace(i.Tier) ? "pd" : i.Tier
                })
                .ToList();
            items = ScheduleEngine.OrderItems(items, def.OldestFirst);
            if (items.Count == 0)
            {
                continue;
            }

            channels.Add(new GuideChannel { Definition = def, Items = items });
        }

        // Weather channel is always active (its programs come from the forecast).
        var weatherDef = _lineup.FirstOrDefault(d => d.Weather && d.Enabled);
        if (weatherDef is not null)
        {
            channels.Add(new GuideChannel { Definition = weatherDef, Items = new List<ScheduleEngine.GuideItem>() });
        }

        channels.Sort((a, b) => a.Definition.Number.CompareTo(b.Definition.Number));
        _logger.LogInformation("[RetroTv] Guide loaded: {Count} active channels ({Names}).", channels.Count, string.Join(", ", channels.Select(c => c.Definition.Number + " " + c.Definition.Name)));
        return channels;
    }

    internal static string XmlEscape(string? s)
    {
        return (s ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }

    internal static string XmltvTime(DateTimeOffset t)
    {
        var u = t.UtcDateTime;
        return u.ToString("yyyyMMddHHmmss +0000", CultureInfo.InvariantCulture);
    }

    // ---- pools.json document shape (same as the Stage 2 sidecar) ----

    public class PoolsDocument
    {
        [JsonPropertyName("library")]
        public string Library { get; set; } = "Retro TV";

        [JsonPropertyName("generated_at")]
        public string GeneratedAt { get; set; } = string.Empty;

        [JsonPropertyName("channels")]
        public List<PoolChannel> Channels { get; set; } = new();

        [JsonPropertyName("counts")]
        public PoolsCounts? Counts { get; set; }
    }

    public class PoolsCounts
    {
        [JsonPropertyName("channels")]
        public int Channels { get; set; }

        [JsonPropertyName("items")]
        public int Items { get; set; }
    }

    public class PoolChannel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("tier")]
        public string Tier { get; set; } = "pd";

        [JsonPropertyName("items")]
        public List<PoolItem> Items { get; set; } = new();
    }

    public class PoolItem
    {
        [JsonPropertyName("identifier")]
        public string Identifier { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("runtime_seconds")]
        public int RuntimeSeconds { get; set; }

        [JsonPropertyName("licenseurl")]
        public string LicenseUrl { get; set; } = string.Empty;

        [JsonPropertyName("tier")]
        public string Tier { get; set; } = "pd";

        [JsonPropertyName("collection")]
        public List<string> Collection { get; set; } = new();

        [JsonPropertyName("mp4_url")]
        public string Mp4Url { get; set; } = string.Empty;

        [JsonPropertyName("poster_url")]
        public string? PosterUrl { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
    }
}
