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
/// (pools.json); on first use the embedded full-lineup seed pools are
/// copied there (155 channels, 3,032 items in 0.3.0.0). A catalog build
/// refreshes the pools from catalog.json — additively, by identifier, so
/// the guide only ever grows: channels the catalog can't fill keep their
/// seed pools. Commercial breaks (sidecar v0.2.0 rules) interleave into
/// each channel's effective item list when the config switch is on, so
/// the guide and the streams can never disagree. Audiobooks is not
/// served: its items are MP3s and the plugin has no audio-only stream
/// path (sidecar-only channel).
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
    private IReadOnlyList<ScheduleEngine.BreakSpot> _breakPool = Array.Empty<ScheduleEngine.BreakSpot>();
    private IReadOnlyList<ScheduleEngine.BreakSpot> _breakPoolShort = Array.Empty<ScheduleEngine.BreakSpot>();

    public GuideService(IApplicationPaths applicationPaths, ILogger<GuideService> logger, WeatherChannel weather)
    {
        _logger = logger;
        _weather = weather;
        _poolsPath = Path.Combine(applicationPaths.ProgramDataPath, "RetroTv", "pools.json");
        _lineup = ChannelLineup.Load(logger);
        LoadBreakPool();
    }

    /// <summary>Commercial breaks on/off, from the plugin config page (default ON). Read per call so a config save takes effect without a restart.</summary>
    public bool CommercialBreaksEnabled => Plugin.Instance?.Configuration.CommercialBreaksEnabled ?? true;

    /// <summary>Break pool stats for the guide JSON / diagnostics.</summary>
    public (int Pool, int ShortPool, int Channels) BreakStats => (
        _breakPool.Count,
        _breakPoolShort.Count,
        GetActiveChannels().Count(c => ChannelGetsBreaks(c.Definition)));

    /// <summary>Whether a channel gets commercial breaks (excluded slugs + audio-only + weather never do).</summary>
    public static bool ChannelGetsBreaks(ChannelDefinition def)
    {
        if (def.Weather)
        {
            return false;
        }

        return !ScheduleEngine.CommercialBreaks.ExcludedSlugs.Contains(def.Slug);
    }

    /// <summary>The channel's effective item list: the original pool items, with one 30–60 s commercial-break spot interleaved at most once an hour when breaks are on and the channel qualifies. With breaks off (or an excluded channel, or an empty pool) the original list is returned unchanged.</summary>
    public IReadOnlyList<ScheduleEngine.GuideItem> GetEffectiveItems(GuideChannel channel)
    {
        if (channel.Definition.Weather || !CommercialBreaksEnabled || !ChannelGetsBreaks(channel.Definition))
        {
            return channel.Items;
        }

        if (_breakPool.Count == 0)
        {
            return channel.Items;
        }

        return ScheduleEngine.CommercialBreaks.BuildEffectiveItems(
            channel.Items, channel.Definition.Slug, _breakPoolShort, _breakPool);
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

        return ScheduleEngine.ScheduleFrom(GetEffectiveItems(channel), from, hours);
    }

    /// <summary>The programme playing on a channel right now (null when the channel has no items).</summary>
    public ScheduleEngine.ScheduledProgramme? GetNowPlaying(GuideChannel channel, DateTimeOffset at)
    {
        if (channel.Definition.Weather)
        {
            var progs = _weather.GetSchedule(at, 1);
            return progs.Count == 0 ? null : progs[0];
        }

        var items = GetEffectiveItems(channel);
        return items.Count == 0 ? null : ScheduleEngine.ProgramAt(items, at);
    }

    /// <summary>
    /// Merge the catalog the builder just wrote into the guide pools.
    /// Additive by identifier: catalog items that match a channel are
    /// added (or refresh the matching pool entry's metadata); nothing is
    /// ever removed, and channels the catalog can't fill keep their seed
    /// pools untouched. A channel's lineup Folder is the catalog folder
    /// it draws from — defs whose Folder is their own channel name never
    /// match a catalog folder, so they are refresh-frozen by design.
    /// Best-effort: a failure is logged and the old pools keep serving —
    /// it never fails the build.
    /// </summary>
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

            EnsureLoaded();
            // Base document = the pools file itself (seeded from the
            // embedded pools on first use by EnsureLoaded), so existing
            // pool items keep every field the catalog doesn't carry
            // (licenseurl, collection, ...). The merge only adds catalog
            // items or refreshes matching entries' metadata.
            PoolsDocument pools;
            if (File.Exists(_poolsPath))
            {
                pools = JsonSerializer.Deserialize<PoolsDocument>(File.ReadAllText(_poolsPath), JsonOptions)
                    ?? new PoolsDocument { Library = "Retro TV", GeneratedAt = DateTimeOffset.UtcNow.ToString("o") };
            }
            else
            {
                pools = new PoolsDocument { Library = "Retro TV", GeneratedAt = DateTimeOffset.UtcNow.ToString("o") };
            }

            pools.GeneratedAt = DateTimeOffset.UtcNow.ToString("o");

            var byName = pools.Channels.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
            var added = 0;
            var updated = 0;
            foreach (var def in _lineup)
            {
                if (!def.Enabled || def.Weather || string.IsNullOrEmpty(def.Folder))
                {
                    continue;
                }

                if (!byName.TryGetValue(def.Name, out var poolChannel))
                {
                    poolChannel = new PoolChannel
                    {
                        Id = def.ChannelId,
                        Slug = def.Slug,
                        Name = def.Name,
                        Tier = def.Tier,
                        Items = new List<PoolItem>()
                    };
                    pools.Channels.Add(poolChannel);
                    byName[def.Name] = poolChannel;
                }

                var matches = doc.Items
                    .Where(e => e.Tier == def.Tier && def.Matches(e.Folder, e.Title, e.Description, e.Year))
                    .ToList();
                if (matches.Count == 0)
                {
                    continue;
                }

                var byId = poolChannel.Items.ToDictionary(i => i.Identifier, i => i, StringComparer.Ordinal);
                foreach (var e in matches)
                {
                    if (byId.TryGetValue(e.Identifier, out var existing))
                    {
                        existing.Title = e.Title;
                        existing.Year = e.Year;
                        existing.RuntimeSeconds = e.RuntimeSeconds ?? ScheduleEngine.DefaultRuntimeSeconds;
                        existing.Mp4Url = e.Mp4Url;
                        existing.PosterUrl = e.PosterUrl;
                        existing.Description = e.Description;
                        updated++;
                    }
                    else
                    {
                        var item = new PoolItem
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
                        };
                        poolChannel.Items.Add(item);
                        byId[e.Identifier] = item;
                        added++;
                    }
                }

                if (def.OldestFirst)
                {
                    // Soaps play oldest episode first.
                    poolChannel.Items.Sort((a, b) =>
                    {
                        var ya = a.Year ?? int.MaxValue;
                        var yb = b.Year ?? int.MaxValue;
                        var byYear = ya.CompareTo(yb);
                        return byYear != 0 ? byYear : string.Compare(a.Title, b.Title, StringComparison.Ordinal);
                    });
                }
            }

            pools.Counts = new PoolsCounts
            {
                Channels = pools.Channels.Count,
                Items = pools.Channels.Sum(c => c.Items.Count)
            };
            Directory.CreateDirectory(Path.GetDirectoryName(_poolsPath)!);
            File.WriteAllText(_poolsPath, JsonSerializer.Serialize(pools, JsonOptions) + "\n");
            _logger.LogInformation(
                "[RetroTv] Guide pools merged from catalog: {Channels} channels, {Items} items ({Added} added, {Updated} updated) -> {Path}",
                pools.Channels.Count, pools.Counts.Items, added, updated, _poolsPath);

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

    /// <summary>XMLTV guide (96 h by default) for Jellyfin's XMLTV provider. Same schedule the guide page and the streams use, so they can never disagree. The 96 h window means a 48 h refresh cycle always leaves ~48 h of overlap — no gaps even if a refresh runs late.</summary>
    public string BuildXmltv(double hours = 96)
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
                if (p.Item.IsCommercialBreak)
                {
                    sb.Append("    <category>Commercial Break</category>\n");
                }
                else
                {
                    sb.Append("    <category>").Append(p.Item.Tier == "user-uploaded" ? "User-Uploaded Recording" : "Public Domain").Append("</category>\n");
                }

                sb.Append("  </programme>\n");
            }
        }

        sb.Append("</tv>\n");
        return sb.ToString();
    }

    /// <summary>Guide data as JSON — the same schedule the XMLTV feed carries, for the plugin's own guide page.</summary>
    public object BuildGuideJson(double hours = 96)
    {
        var now = DateTimeOffset.UtcNow;
        var breaks = BreakStats;
        return new
        {
            library = "Retro TV",
            generated_at = now.ToString("o"),
            hours,
            anchor = ScheduleEngine.Anchor.ToString("o"),
            attribution = "Program data from the Internet Archive. Weather data by Open-Meteo.com.",
            commercial_breaks = CommercialBreaksEnabled && _breakPool.Count > 0,
            commercial_break_pool = breaks.Pool,
            commercial_break_channels = breaks.Channels,
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
                    runtime_seconds = p.Item.EffectiveRuntimeSeconds,
                    commercial_break = p.Item.IsCommercialBreak
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

    /// <summary>Load the embedded breaks-pool.json (66 eligible vintage spots, 20–90 s; the 30–60 s subset is what breaks actually play). A missing or unreadable pool disables breaks (empty pool = original schedules).</summary>
    private void LoadBreakPool()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("breaks-pool.json", StringComparison.OrdinalIgnoreCase));
            if (resourceName is null)
            {
                _logger.LogWarning("[RetroTv] Embedded breaks-pool.json was not found; commercial breaks are disabled.");
                return;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            var doc = JsonSerializer.Deserialize<BreakPoolDocument>(reader.ReadToEnd(), JsonOptions);
            if (doc?.Spots is null)
            {
                _logger.LogWarning("[RetroTv] breaks-pool.json did not parse; commercial breaks are disabled.");
                return;
            }

            var eligible = doc.Spots
                .Where(s => !string.IsNullOrWhiteSpace(s.Mp4Url)
                    && s.RuntimeSeconds >= ScheduleEngine.CommercialBreaks.EligibleMinSeconds
                    && s.RuntimeSeconds <= ScheduleEngine.CommercialBreaks.EligibleMaxSeconds)
                .Select(s => new ScheduleEngine.BreakSpot
                {
                    Identifier = s.Identifier,
                    Title = s.Title,
                    Year = s.Year,
                    RuntimeSeconds = s.RuntimeSeconds,
                    Mp4Url = s.Mp4Url,
                    PosterUrl = s.PosterUrl
                })
                .ToList();
            _breakPool = eligible;
            _breakPoolShort = eligible
                .Where(s => s.RuntimeSeconds >= ScheduleEngine.CommercialBreaks.SpotMinSeconds
                    && s.RuntimeSeconds <= ScheduleEngine.CommercialBreaks.SpotMaxSeconds)
                .ToList();
            _logger.LogInformation(
                "[RetroTv] Break pool loaded: {Pool} eligible spots, {Short} in the 30–60 s break range.",
                _breakPool.Count, _breakPoolShort.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RetroTv] Break pool failed to load: {Message} — commercial breaks are disabled.", ex.Message);
            _breakPool = Array.Empty<ScheduleEngine.BreakSpot>();
            _breakPoolShort = Array.Empty<ScheduleEngine.BreakSpot>();
        }
    }

    // ---- breaks-pool.json document shape ----

    private class BreakPoolDocument
    {
        [JsonPropertyName("spots")]
        public List<BreakPoolSpot> Spots { get; set; } = new();
    }

    private class BreakPoolSpot
    {
        [JsonPropertyName("identifier")]
        public string Identifier { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("runtime_seconds")]
        public int RuntimeSeconds { get; set; }

        [JsonPropertyName("mp4_url")]
        public string Mp4Url { get; set; } = string.Empty;

        [JsonPropertyName("poster_url")]
        public string? PosterUrl { get; set; }
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
                // First use: seed from the embedded full-lineup pools (153 channels, 2,982 items in 0.3.0.0).
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
