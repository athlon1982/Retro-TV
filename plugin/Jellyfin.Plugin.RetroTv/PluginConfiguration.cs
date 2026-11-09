using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.RetroTv;

/// <summary>
/// Retro TV plugin configuration, persisted by Jellyfin's plugin config
/// store. Defaults mirror config.example.json from the catalog builder, so
/// a plugin build and a Node builder build behave the same.
/// No API keys, passwords, or tokens live here (or anywhere in the plugin):
/// the Internet Archive and Open-Meteo calls need no credentials at all.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Where .strm/.nfo/poster files and catalog.json go. Empty = not configured yet (the task refuses to run until it is set). Leave blank if you only want Live TV — the channels do not need it.</summary>
    public string OutputFolder { get; set; } = string.Empty;

    /// <summary>Name written into catalog.json; the Jellyfin library keeps this name.</summary>
    public string LibraryName { get; set; } = "Retro TV";

    /// <summary>Internet Archive politeness: requests per second. Backs off on 429/503 regardless.</summary>
    public double RequestsPerSecond { get; set; } = 1;

    /// <summary>MP4 derivative preference: "full" (full MP4 first, 512kb fallback). Same scoring as the Node builder.</summary>
    public string Mp4Preference { get; set; } = "full";

    /// <summary>True includes the user-uploaded tier (rights unverified, clearly marked). False drops the whole tier; the pd tier is untouched.</summary>
    public bool IncludeUserUploadedTier { get; set; } = true;

    /// <summary>Folder definitions as JSON: [{ name, tier ("pd"|"user-uploaded"), query (IA Lucene), limit }]. Seeded from config.example.json.</summary>
    public string FoldersJson { get; set; } = DefaultFoldersJson;

    /// <summary>Curated pilot items as JSON: [{ folder, tier, identifier }].</summary>
    public string PilotJson { get; set; } = DefaultPilotJson;

    /// <summary>Identifiers to never (re-)add, as a JSON string array.</summary>
    public string PruneJson { get; set; } = "[]";

    /// <summary>Title/year overrides as JSON: { identifier: { title?, year? } }.</summary>
    public string OverridesJson { get; set; } = DefaultOverridesJson;

    /// <summary>Active guide theme id (see Guide/Themes). Default: woodgrain-78.</summary>
    public string ActiveGuideTheme { get; set; } = "woodgrain-78";

    /// <summary>Weather channel zip code. Blank until you enter your own and press Resolve zip.</summary>
    public string WeatherZipCode { get; set; } = string.Empty;

    /// <summary>Cached geocoded latitude for WeatherZipCode. Resolved once on save; null until the first geocode.</summary>
    public double? WeatherLatitude { get; set; }

    /// <summary>Cached geocoded longitude for WeatherZipCode. Resolved once on save; null until the first geocode.</summary>
    public double? WeatherLongitude { get; set; }

    /// <summary>Resolved place name for WeatherZipCode (shown on the config page so a wrong geocode is visible).</summary>
    public string WeatherPlaceName { get; set; } = string.Empty;

    /// <summary>Daily scheduled catalog build on/off.</summary>
    public bool AutoBuildEnabled { get; set; } = true;

    /// <summary>Live channels master switch. New installs get them on; an admin can still turn them off.</summary>
    public bool ChannelsEnabled { get; set; } = true;

    /// <summary>Old-school commercial breaks between programmes: at most once per hour per channel, 1 spot of 30-60 s, at programme boundaries. Default ON.</summary>
    public bool CommercialBreaksEnabled { get; set; } = true;

    /// <summary>ffmpeg binary path for the live channel streams. Empty = "ffmpeg" on the PATH. (Jellyfin's bundled ffmpeg).</summary>
    public string FfmpegPath { get; set; } = string.Empty;

    /// <summary>Font file for the weather channel slate (ffmpeg drawtext). Empty = auto-detect (DejaVu Sans on Linux, Arial on Windows).</summary>
    public string WeatherFontPath { get; set; } = string.Empty;

    internal const string DefaultFoldersJson = """
        [
          { "name": "Saturday Morning Cartoons", "tier": "pd", "query": "collection:animationandcartoons AND licenseurl:*publicdomain* AND mediatype:(movies)", "limit": 200 },
          { "name": "Classic TV", "tier": "pd", "query": "collection:classic_tv AND licenseurl:*publicdomain* AND mediatype:(movies)", "limit": 200 },
          { "name": "Drive-In Movies", "tier": "pd", "query": "collection:feature_films AND licenseurl:*publicdomain* AND mediatype:(movies)", "limit": 200 },
          { "name": "Prelinger Theater", "tier": "pd", "query": "collection:prelinger AND mediatype:(movies)", "limit": 200 },
          { "name": "Ad Break", "tier": "pd", "query": "collection:classic_tv_commercials AND licenseurl:*publicdomain* AND mediatype:(movies)", "limit": 100 },
          { "name": "Creature Features", "tier": "pd", "query": "collection:feature_films AND licenseurl:*publicdomain* AND (subject:(horror) OR subject:(science fiction)) AND mediatype:(movies)", "limit": 100 },
          { "name": "VHS Vault", "tier": "pd", "query": "collection:vhsvault AND licenseurl:*publicdomain* AND mediatype:(movies)", "limit": 100 },
          { "name": "Soaps", "tier": "user-uploaded", "query": "(title:(General Hospital) OR title:(Guiding Light) OR title:(Dark Shadows)) AND mediatype:(movies)", "limit": 100 },
          { "name": "Toonami", "tier": "user-uploaded", "query": "title:(Toonami) AND mediatype:(movies)", "limit": 100 },
          { "name": "Anime", "tier": "user-uploaded", "query": "collection:anime AND mediatype:(movies)", "limit": 100 }
        ]
        """;

    internal const string DefaultPilotJson = """
        [
          { "folder": "Saturday Morning Cartoons", "tier": "pd", "identifier": "all-new-popeye-and-friends" },
          { "folder": "Saturday Morning Cartoons", "tier": "pd", "identifier": "the_conceited_general" },
          { "folder": "Classic TV", "tier": "pd", "identifier": "The_Trouble_With_Father" },
          { "folder": "Classic TV", "tier": "pd", "identifier": "Yet_More_Lucy" },
          { "folder": "Drive-In Movies", "tier": "pd", "identifier": "TheManInGrey" },
          { "folder": "Drive-In Movies", "tier": "pd", "identifier": "UnknownWorld_201503" },
          { "folder": "Prelinger Theater", "tier": "pd", "identifier": "ToEnrichMank" },
          { "folder": "Prelinger Theater", "tier": "pd", "identifier": "TraderTh1956" },
          { "folder": "Ad Break", "tier": "pd", "identifier": "ShastaSodaCommercial-Frankenstein" },
          { "folder": "Soaps", "tier": "user-uploaded", "identifier": "general-hospital-3-11-1992-part-2-of-4" },
          { "folder": "Soaps", "tier": "user-uploaded", "identifier": "dark-shadows-disc-6" },
          { "folder": "Toonami", "tier": "user-uploaded", "identifier": "toonami-pre-flight-saturday-august-29th-2020" }
        ]
        """;

    internal const string DefaultOverridesJson = """
        {
          "The_Trouble_With_Father": { "title": "The Trouble with Father" },
          "Yet_More_Lucy": { "title": "What's My Line with Lucille Ball" },
          "UnknownWorld_201503": { "title": "Unknown World", "year": 1951 }
        }
        """;
}
