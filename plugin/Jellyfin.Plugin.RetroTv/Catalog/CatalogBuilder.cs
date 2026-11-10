using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RetroTv.Catalog;

/// <summary>
/// The catalog builder — a faithful port of src/catalog_builder.mjs into C#.
/// Queries the Internet Archive, enforces the license filter, picks an MP4
/// derivative, and writes .strm + .nfo + poster.jpg per item plus a versioned
/// catalog.json into the output folder. Jellyfin scans that folder as the
/// Retro TV library; video streams direct from the Archive at play time.
/// </summary>
public class RetroTvCatalogBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly InternetArchiveClient _ia;
    private readonly ILogger<RetroTvCatalogBuilder> _logger;

    public RetroTvCatalogBuilder(InternetArchiveClient ia, ILogger<RetroTvCatalogBuilder> logger)
    {
        _ia = ia;
        _logger = logger;
    }

    /// <summary>Result of one build run (also stored in CatalogBuildState).</summary>
    public class BuildResult
    {
        public required string OutputDir { get; init; }
        public required bool Pilot { get; init; }
        public required int Version { get; init; }
        public required int ItemCount { get; init; }
        public required int SkippedCount { get; init; }
        public required Dictionary<string, int> ByFolder { get; init; }
        public required Dictionary<string, int> ByTier { get; init; }
        public required TimeSpan Duration { get; init; }
        public string? Error { get; init; }
    }

    /// <summary>
    /// Runs a build. Pilot=true builds only the curated pilot list; otherwise
    /// every folder in config (skipping the user-uploaded tier when
    /// config.IncludeUserUploadedTier is false). Idempotent: re-running does
    /// not duplicate items; prune entries are never re-added.
    /// Throws only on configuration errors (bad JSON in config) or when the
    /// caller cancels. IA failures on individual items are recorded as
    /// skipped, never fatal to the run.
    /// </summary>
    public async Task<BuildResult> BuildAsync(
        PluginConfiguration config,
        bool pilot,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var outputDir = config.OutputFolder;
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            throw new InvalidOperationException("OutputFolder is not set. Open the Retro TV plugin settings and choose the output folder.");
        }

        _ia.RequestsPerSecond = config.RequestsPerSecond;
        var folders = ParseJson<List<CatalogFolder>>(config.FoldersJson, nameof(config.FoldersJson));
        var pilotItems = ParseJson<List<PilotItem>>(config.PilotJson, nameof(config.PilotJson));
        var pruneSet = new HashSet<string>(ParseJson<List<string>>(config.PruneJson, nameof(config.PruneJson)), StringComparer.Ordinal);
        var overrides = ParseJson<Dictionary<string, TitleOverride>>(config.OverridesJson, nameof(config.OverridesJson));

        Directory.CreateDirectory(outputDir);

        // The library icon: write the v5 Retro TV icon as folder.jpg at the
        // library root so Jellyfin shows it without the Node builder's help.
        WriteFolderJpg(outputDir);

        // Version the catalog: keep the previous run as catalog.prev.json.
        var catalogPath = Path.Combine(outputDir, "catalog.json");
        var prevVersion = 0;
        try
        {
            var prevText = await File.ReadAllTextAsync(catalogPath, cancellationToken).ConfigureAwait(false);
            var prev = JsonSerializer.Deserialize<CatalogDocument>(prevText, JsonOptions);
            prevVersion = prev?.Version ?? 0;
            File.Copy(catalogPath, Path.Combine(outputDir, "catalog.prev.json"), overwrite: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // First run (or a corrupt previous catalog): start at version 0.
        }

        var jobs = new List<(string Folder, string Tier, string Identifier, bool MultiFile)>();
        if (pilot)
        {
            jobs.AddRange(pilotItems.Select(p => (p.Folder, p.Tier, p.Identifier, false)));
            _logger.LogInformation("[RetroTv] Pilot mode: {Count} curated items", jobs.Count);
        }
        else
        {
            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (folder.Tier == "user-uploaded" && !config.IncludeUserUploadedTier)
                {
                    _logger.LogInformation("[RetroTv] Skipping folder \"{Folder}\" (user-uploaded tier disabled in config)", folder.Name);
                    continue;
                }

                _logger.LogInformation("[RetroTv] Searching \"{Folder}\" ({Tier}) — {Query} [limit {Limit}]", folder.Name, folder.Tier, folder.Query, folder.Limit);
                var (docs, numFound) = await _ia.SearchAsync(folder.Query, Math.Min(folder.Limit, 1000), 0, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("[RetroTv] Found {NumFound} total, taking {Count}", numFound, docs.Count);
                foreach (var doc in docs)
                {
                    jobs.Add((folder.Name, folder.Tier, doc.Identifier, folder.MultiFile));
                }
            }
        }

        var items = new List<CatalogEntry>();
        var skipped = new List<CatalogSkipped>();
        for (var i = 0; i < jobs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var job = jobs[i];
            try
            {
                var entries = await BuildItemAsync(_ia, outputDir, job.Folder, job.Tier, job.Identifier, job.MultiFile, pruneSet, overrides, cancellationToken).ConfigureAwait(false);
                if (entries.Count > 0)
                {
                    items.AddRange(entries);
                    _logger.LogInformation("[RetroTv] - {Identifier} [{Folder} / {Tier}] OK ({Count} entries)", job.Identifier, job.Folder, job.Tier, entries.Count);
                }
                else
                {
                    // BuildItemAsync logs the skip reason itself; record it generically here.
                    skipped.Add(new CatalogSkipped { Identifier = job.Identifier, Folder = job.Folder, Reason = "skipped (see log)" });
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                skipped.Add(new CatalogSkipped { Identifier = job.Identifier, Folder = job.Folder, Reason = "error: " + ex.Message });
                _logger.LogWarning(ex, "[RetroTv] - {Identifier} [{Folder} / {Tier}] ERROR ({Message})", job.Identifier, job.Folder, job.Tier, ex.Message);
            }

            progress?.Report(jobs.Count == 0 ? 1 : (double)(i + 1) / jobs.Count);
        }

        var byFolder = new Dictionary<string, int>(StringComparer.Ordinal);
        var byTier = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            byFolder[item.Folder] = byFolder.TryGetValue(item.Folder, out var f) ? f + 1 : 1;
            byTier[item.Tier] = byTier.TryGetValue(item.Tier, out var t) ? t + 1 : 1;
        }

        var document = new CatalogDocument
        {
            Library = config.LibraryName,
            Version = prevVersion + 1,
            GeneratedAt = DateTimeOffset.UtcNow.ToString("o"),
            Pilot = pilot,
            Counts = new CatalogCounts
            {
                Items = items.Count,
                ByFolder = byFolder,
                ByTier = byTier
            },
            Items = items,
            Skipped = skipped
        };
        await File.WriteAllTextAsync(catalogPath, JsonSerializer.Serialize(document, JsonOptions) + "\n", cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "[RetroTv] Done: {Items} items, {Skipped} skipped. Catalog v{Version} -> {Path}",
            items.Count, skipped.Count, document.Version, catalogPath);

        return new BuildResult
        {
            OutputDir = outputDir,
            Pilot = pilot,
            Version = document.Version,
            ItemCount = items.Count,
            SkippedCount = skipped.Count,
            ByFolder = byFolder,
            ByTier = byTier,
            Duration = DateTimeOffset.UtcNow - startedAt
        };
    }

    /// <summary>Builds one item: metadata -> license filter -> MP4 pick -> files. Returns null when the item is skipped (reason logged).</summary>
    /// <summary>
    /// Builds the catalog entries for one IA item. Single-file mode (the
    /// default) picks the best MP4 and returns one entry. Multi-file mode
    /// (folders with MultiFile=true — full-series collections) returns one
    /// entry per distinct episode file, each with a synthesized unique
    /// identifier ("{identifier}--{file-slug}") so the guide pool treats
    /// episodes as distinct items. Returns an empty list when the item is
    /// pruned, fails the license check, or has no usable MP4.
    /// </summary>
    private async Task<List<CatalogEntry>> BuildItemAsync(
        InternetArchiveClient client,
        string outDir,
        string folder,
        string tier,
        string identifier,
        bool multiFile,
        HashSet<string> pruneSet,
        Dictionary<string, TitleOverride> overrides,
        CancellationToken cancellationToken)
    {
        if (pruneSet.Contains(identifier))
        {
            _logger.LogInformation("[RetroTv] - {Identifier} SKIP (pruned)", identifier);
            return new List<CatalogEntry>();
        }

        var data = await client.MetadataAsync(identifier, cancellationToken).ConfigureAwait(false);
        var meta = data.Metadata!;
        var files = data.Files;

        var licenseUrl = FirstString(meta.LicenseUrl);
        if (tier == "pd" && !licenseUrl.Contains("publicdomain", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[RetroTv] - {Identifier} SKIP (pd tier requires publicdomain licenseurl, got: {License})", identifier, licenseUrl.Length == 0 ? "(none)" : licenseUrl);
            return new List<CatalogEntry>();
        }

        var mp4s = files
            .Select(f => (File: f, Score: ScoreMp4(f, identifier)))
            .Where(x => x.Score >= 0)
            .OrderByDescending(x => x.Score)
            .ToList();
        if (mp4s.Count == 0)
        {
            _logger.LogInformation("[RetroTv] - {Identifier} SKIP (no MP4 derivative)", identifier);
            return new List<CatalogEntry>();
        }

        var title = FirstString(meta.Title);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = identifier;
        }

        title = title.Trim();
        var year = ParseYear(meta.Year);
        var description = StripHtml(FirstString(meta.Description)).Trim();
        if (description.Length > 1000)
        {
            description = description.Substring(0, 1000);
        }

        overrides.TryGetValue(identifier, out var ov);
        var finalTitle = ov?.Title ?? title;
        var finalYear = ov?.Year ?? year;
        var collection = AsStringList(meta.Collection);
        var posterFile = files.Find(f => f.Name == "__ia_thumb.jpg");
        var posterUrl = posterFile is null
            ? null
            : $"{InternetArchiveClient.BaseUrl}/download/{Uri.EscapeDataString(identifier)}/__ia_thumb.jpg";
        var safeFolder = Sanitize(folder);

        if (!multiFile)
        {
            var chosen = mp4s[0].File;
            var small = files.Find(f => f.Name.EndsWith("_512kb.mp4", StringComparison.OrdinalIgnoreCase));
            var runtimeSeconds = ParseRuntimeSeconds(meta, chosen);
            var mp4Url = $"{InternetArchiveClient.BaseUrl}/download/{Uri.EscapeDataString(identifier)}/{EncodeUriComponentPath(chosen.Name)}";

            var baseName = Sanitize(finalYear.HasValue ? $"{finalTitle} ({finalYear.Value})" : finalTitle);
            var itemDir = Path.Combine(outDir, safeFolder, baseName);
            Directory.CreateDirectory(itemDir);

            var strmName = baseName + ".strm";
            var nfoName = baseName + ".nfo";
            await File.WriteAllTextAsync(Path.Combine(itemDir, strmName), mp4Url + "\n", cancellationToken).ConfigureAwait(false);

            var entry = new CatalogEntry
            {
                Identifier = identifier,
                Title = finalTitle,
                Year = finalYear,
                RuntimeSeconds = runtimeSeconds,
                LicenseUrl = licenseUrl,
                Tier = tier,
                Collection = collection,
                Folder = folder,
                Mp4Url = mp4Url,
                Mp4512kbUrl = small is null ? null : $"{InternetArchiveClient.BaseUrl}/download/{Uri.EscapeDataString(identifier)}/{EncodeUriComponentPath(small.Name)}",
                PosterUrl = posterUrl,
                Description = description,
                Files = new CatalogFiles
                {
                    Strm = $"{safeFolder}/{baseName}/{strmName}",
                    Nfo = $"{safeFolder}/{baseName}/{nfoName}",
                    Poster = posterFile is null ? null : $"{safeFolder}/{baseName}/poster.jpg"
                }
            };
            await File.WriteAllTextAsync(Path.Combine(itemDir, nfoName), NfoWriter.BuildNfo(entry), cancellationToken).ConfigureAwait(false);
            if (posterFile is not null && posterUrl is not null)
            {
                var bytes = await client.DownloadBytesAsync(posterUrl, cancellationToken).ConfigureAwait(false);
                await File.WriteAllBytesAsync(Path.Combine(itemDir, "poster.jpg"), bytes, cancellationToken).ConfigureAwait(false);
            }

            return new List<CatalogEntry> { entry };
        }

        // Multi-file mode: one entry per distinct episode. Group MP4s by
        // their base file name (stripping .ia / _512kb / _edit variants and
        // any directory prefix) and keep the best-scoring file per group,
        // so a file and its .ia.mp4 derivative don't become two entries.
        var byBase = new Dictionary<string, (InternetArchiveClient.IaFile File, int Score)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, score) in mp4s)
        {
            var leaf = (file.Name ?? string.Empty).Replace('\\', '/').Split('/').Last();
            var stem = Regex.Replace(leaf, "\\.mp4$", string.Empty, RegexOptions.IgnoreCase);
            stem = Regex.Replace(stem, "(\\.ia|_512kb|_edit)$", string.Empty, RegexOptions.IgnoreCase);
            if (stem.Length == 0)
            {
                continue;
            }
            if (!byBase.TryGetValue(stem, out var cur) || score > cur.Score)
            {
                byBase[stem] = (file, score);
            }
        }

        var entries = new List<CatalogEntry>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (stem, (file, _)) in byBase.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var epTitle = EpisodeTitleFromFileName(stem, finalTitle);
            var epId = identifier + "--" + Sanitize(stem).Replace(' ', '-').ToLowerInvariant();
            if (epId.Length > 120)
            {
                epId = epId.Substring(0, 120).TrimEnd('-');
            }
            if (!seenIds.Add(epId))
            {
                continue;
            }

            var epRuntime = ParseRuntimeSeconds(meta, file);
            var epMp4Url = $"{InternetArchiveClient.BaseUrl}/download/{Uri.EscapeDataString(identifier)}/{EncodeUriComponentPath(file.Name)}";
            var epBase = Sanitize(epTitle);
            var epDir = Path.Combine(outDir, safeFolder, epBase);
            Directory.CreateDirectory(epDir);
            var epStrm = epBase + ".strm";
            var epNfo = epBase + ".nfo";
            await File.WriteAllTextAsync(Path.Combine(epDir, epStrm), epMp4Url + "\n", cancellationToken).ConfigureAwait(false);

            var epEntry = new CatalogEntry
            {
                Identifier = epId,
                Title = epTitle,
                Year = finalYear,
                RuntimeSeconds = epRuntime,
                LicenseUrl = licenseUrl,
                Tier = tier,
                Collection = collection,
                Folder = folder,
                Mp4Url = epMp4Url,
                Mp4512kbUrl = null,
                PosterUrl = posterUrl,
                Description = description,
                Files = new CatalogFiles
                {
                    Strm = $"{safeFolder}/{epBase}/{epStrm}",
                    Nfo = $"{safeFolder}/{epBase}/{epNfo}",
                    Poster = posterFile is null ? null : $"{safeFolder}/{epBase}/poster.jpg"
                }
            };
            await File.WriteAllTextAsync(Path.Combine(epDir, epNfo), NfoWriter.BuildNfo(epEntry), cancellationToken).ConfigureAwait(false);
            entries.Add(epEntry);
        }

        return entries;
    }

    /// <summary>
    /// Derives a readable episode title from a series-collection file stem
    /// (e.g. "Mash S1E1 Pilot PAL" or "Animaniacs S01E01 De-Zanitized, The
    /// Monkey Song, Nighty-Night Toon"). Falls back to the series title plus
    /// the raw stem when the stem carries no episode name.
    /// </summary>
    private static string EpisodeTitleFromFileName(string stem, string seriesTitle)
    {
        var s = stem.Replace('_', ' ').Trim();
        s = Regex.Replace(s, "\\s+", " ");
        // Strip common trailing tags that aren't part of the episode name.
        s = Regex.Replace(s, "\\s+(PAL|NTSC|360p|480p|720p|1080p|UPSCALED|RE-DVDRIP|DVDRIP)$", string.Empty, RegexOptions.IgnoreCase).Trim();
        if (s.Length == 0)
        {
            return seriesTitle;
        }
        // If the stem already starts with the series name (or a close
        // variant), use it as-is; otherwise prefix the series title so the
        // guide shows which show the episode belongs to.
        var norm = Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", string.Empty);
        var seriesNorm = Regex.Replace(seriesTitle.ToLowerInvariant(), "[^a-z0-9]+", string.Empty);
        if (norm.StartsWith(seriesNorm, StringComparison.Ordinal) || seriesNorm.StartsWith(norm, StringComparison.Ordinal))
        {
            return s;
        }
        return seriesTitle + " — " + s;
    }

    // ---- Ports of the Node builder helpers (same rules, same outputs) ----

    /// <summary>Write the v5 Retro TV icon as folder.jpg at the library root (embedded JPEG asset). Idempotent — overwritten on every build.</summary>
    private void WriteFolderJpg(string outputDir)
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("folder.jpg", StringComparison.OrdinalIgnoreCase));
            if (resourceName is null)
            {
                _logger.LogWarning("[RetroTv] Embedded folder.jpg was not found in the plugin assembly; the library icon was not written.");
                return;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var file = File.Create(Path.Combine(outputDir, "folder.jpg"));
            stream.CopyTo(file);
            _logger.LogInformation("[RetroTv] Wrote folder.jpg (Retro TV icon) to the library root.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RetroTv] folder.jpg write failed (the catalog itself is unaffected): {Message}", ex.Message);
        }
    }


    private static T ParseJson<T>(string json, string fieldName)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                   ?? throw new InvalidOperationException($"{fieldName} deserialized to null");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON in config field {fieldName}: {ex.Message}", ex);
        }
    }

    /// <summary>Windows- and POSIX-safe file/folder name (same rules as the Node builder).</summary>
    internal static string Sanitize(string? name)
    {
        var s = Regex.Replace(name ?? string.Empty, "[<>:\"/\\\\|?*\\x00-\\x1f]", " ");
        s = Regex.Replace(s, "\\s+", " ").Trim().TrimEnd('.', ' ');
        if (s.Length > 100)
        {
            s = s.Substring(0, 100).Trim().TrimEnd('.', ' ');
        }

        return s.Length == 0 ? "untitled" : s;
    }

    /// <summary>Score an .mp4 file: exact &lt;id&gt;.mp4 wins, then plain derivatives, then .ia, then 512kb. -1 = not an MP4.</summary>
    internal static int ScoreMp4(InternetArchiveClient.IaFile file, string identifier)
    {
        var name = file.Name ?? string.Empty;
        if (!name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        var score = 0;
        if (name == identifier + ".mp4")
        {
            score += 3;
        }

        if (file.Source == "derivative")
        {
            score += 1;
        }

        if (name.EndsWith(".ia.mp4", StringComparison.OrdinalIgnoreCase))
        {
            score += 1;
        }
        else if (name.EndsWith("_512kb.mp4", StringComparison.OrdinalIgnoreCase))
        {
            score += 0;
        }
        else if (name.EndsWith("_edit.mp4", StringComparison.OrdinalIgnoreCase))
        {
            score += 1;
        }
        else
        {
            score += 3;
        }

        return score;
    }

    internal static int? ParseRuntimeSeconds(InternetArchiveClient.IaMetadata meta, InternetArchiveClient.IaFile? chosenFile)
    {
        if (chosenFile?.Length.HasValue == true && chosenFile.Length.Value > 0)
        {
            return (int)Math.Round(chosenFile.Length.Value);
        }

        var raw = meta.Runtime;
        if (raw.ValueKind == JsonValueKind.Number && raw.TryGetDouble(out var num))
        {
            return (int)Math.Round(num);
        }

        var s = FirstString(raw).Trim();
        if (s.Length == 0)
        {
            return null;
        }

        var m = Regex.Match(s, @"^(\d+):(\d{2}):(\d{2})$");
        if (m.Success)
        {
            return int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 3600
                   + int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) * 60
                   + int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        m = Regex.Match(s, @"^(\d+):(\d{2})$");
        if (m.Success)
        {
            return int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 60
                   + int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        m = Regex.Match(s, @"^(\d+)\s*min", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            return int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 60;
        }

        return null;
    }

    internal static int? ParseYear(JsonElement yearEl)
    {
        var m = Regex.Match(FirstString(yearEl), @"(\d{4})");
        return m.Success ? int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Strip HTML tags from Archive descriptions (they render literally in Jellyfin).</summary>
    internal static string StripHtml(string? s)
    {
        return Regex.Replace(Regex.Replace(s ?? string.Empty, "<[^>]+>", " "), "\\s+", " ").Trim();
    }

    /// <summary>First value of a metadata field that may be a string, a number, or an array.</summary>
    internal static string FirstString(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                return el.GetString() ?? string.Empty;
            case JsonValueKind.Number:
                return el.GetRawText();
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        return item.GetString() ?? string.Empty;
                    }

                    if (item.ValueKind == JsonValueKind.Number)
                    {
                        return item.GetRawText();
                    }
                }

                return string.Empty;
            default:
                return string.Empty;
        }
    }

    internal static List<string> AsStringList(JsonElement el)
    {
        var list = new List<string>();
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                list.Add(el.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        list.Add(item.GetString() ?? string.Empty);
                    }
                }

                break;
        }

        return list;
    }

    /// <summary>JavaScript encodeURIComponent per path segment (same escaping as the Node builder).</summary>
    internal static string EncodeUriComponentPath(string path)
    {
        var segments = path.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = EncodeUriComponent(segments[i]);
        }

        return string.Join("/", segments);
    }

    internal static string EncodeUriComponent(string s)
    {
        // JS encodeURIComponent leaves A-Z a-z 0-9 - _ . ! ~ * ' ( ) unescaped; everything else is %XX (UTF-8).
        var sb = new System.Text.StringBuilder();
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(s))
        {
            var c = (char)b;
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                || c == '-' || c == '_' || c == '.' || c == '!' || c == '~' || c == '*' || c == '\'' || c == '(' || c == ')')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    // ---- catalog.json document shape (matches the Node builder) ----

    public class CatalogDocument
    {
        [JsonPropertyName("library")]
        public string Library { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("generated_at")]
        public string GeneratedAt { get; set; } = string.Empty;

        [JsonPropertyName("pilot")]
        public bool Pilot { get; set; }

        [JsonPropertyName("counts")]
        public CatalogCounts Counts { get; set; } = new();

        [JsonPropertyName("items")]
        public List<CatalogEntry> Items { get; set; } = new();

        [JsonPropertyName("skipped")]
        public List<CatalogSkipped> Skipped { get; set; } = new();
    }

    public class CatalogCounts
    {
        [JsonPropertyName("items")]
        public int Items { get; set; }

        [JsonPropertyName("by_folder")]
        public Dictionary<string, int> ByFolder { get; set; } = new();

        [JsonPropertyName("by_tier")]
        public Dictionary<string, int> ByTier { get; set; } = new();
    }
}
