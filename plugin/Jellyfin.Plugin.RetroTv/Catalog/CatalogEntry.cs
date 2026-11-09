using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.RetroTv.Catalog;

/// <summary>
/// One catalog item. Serialized property names match catalog.json from the
/// Node builder exactly (snake_case), so a plugin build and a Node build
/// produce the same catalog shape and either can read the other's file.
/// </summary>
public class CatalogEntry
{
    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("runtime_seconds")]
    public int? RuntimeSeconds { get; set; }

    [JsonPropertyName("licenseurl")]
    public string LicenseUrl { get; set; } = string.Empty;

    /// <summary>"pd" (public domain / CC) or "user-uploaded" (rights unverified, clearly marked).</summary>
    [JsonPropertyName("tier")]
    public string Tier { get; set; } = string.Empty;

    [JsonPropertyName("collection")]
    public List<string> Collection { get; set; } = new();

    [JsonPropertyName("folder")]
    public string Folder { get; set; } = string.Empty;

    [JsonPropertyName("mp4_url")]
    public string Mp4Url { get; set; } = string.Empty;

    [JsonPropertyName("mp4_512kb_url")]
    public string? Mp4512kbUrl { get; set; }

    [JsonPropertyName("poster_url")]
    public string? PosterUrl { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("files")]
    public CatalogFiles Files { get; set; } = new();
}

/// <summary>Relative paths of the files written for one catalog item.</summary>
public class CatalogFiles
{
    [JsonPropertyName("strm")]
    public string Strm { get; set; } = string.Empty;

    [JsonPropertyName("nfo")]
    public string Nfo { get; set; } = string.Empty;

    [JsonPropertyName("poster")]
    public string? Poster { get; set; }
}

/// <summary>One skipped item, with the reason it was left out.</summary>
public class CatalogSkipped
{
    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = string.Empty;

    [JsonPropertyName("folder")]
    public string Folder { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// A folder definition from config (name, tier, IA Lucene query, limit).
/// Deserialized from PluginConfiguration.FoldersJson.
/// </summary>
public class CatalogFolder
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("tier")]
    public string Tier { get; set; } = string.Empty;

    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 100;
}

/// <summary>A curated pilot item from config: folder, tier, identifier.</summary>
public class PilotItem
{
    [JsonPropertyName("folder")]
    public string Folder { get; set; } = string.Empty;

    [JsonPropertyName("tier")]
    public string Tier { get; set; } = string.Empty;

    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = string.Empty;
}

/// <summary>Title/year override for one identifier, from config.</summary>
public class TitleOverride
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }
}
