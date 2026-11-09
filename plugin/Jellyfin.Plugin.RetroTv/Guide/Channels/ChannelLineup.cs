using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RetroTv.Guide.Channels;

/// <summary>
/// One channel in the Retro TV lineup. The lineup follows the BackWhen TV
/// shape Michael sent (2026-11-08): groups in order, one continuous channel
/// numbering starting at 4, kids first, single-show channels where the
/// catalog has the depth. Only channels with at least one pool item are
/// served (a defined channel with no items is inactive, never an empty
/// entry in the guide). Linear is PD-only in v0.2.0.0 — user-uploaded tier
/// channels are defined but disabled; they stay on-demand only.
/// </summary>
public class ChannelDefinition
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("group")]
    public string Group { get; set; } = string.Empty;

    /// <summary>Catalog folder this channel draws from. Empty for synthetic channels (weather).</summary>
    [JsonPropertyName("folder")]
    public string? Folder { get; set; }

    /// <summary>"pd" or "user-uploaded".</summary>
    [JsonPropertyName("tier")]
    public string Tier { get; set; } = "pd";

    /// <summary>Title must contain one of these (case-insensitive) — single-character channels.</summary>
    [JsonPropertyName("titleKeywords")]
    public List<string>? TitleKeywords { get; set; }

    /// <summary>Title or description must contain one of these (case-insensitive) — genre splits.</summary>
    [JsonPropertyName("genreKeywords")]
    public List<string>? GenreKeywords { get; set; }

    /// <summary>Item year must equal this — per-year VHS Vault channels.</summary>
    [JsonPropertyName("year")]
    public int? Year { get; set; }

    /// <summary>Sort items oldest first (soaps play oldest episode first). Dormant while the channel is disabled.</summary>
    [JsonPropertyName("oldestFirst")]
    public bool OldestFirst { get; set; }

    /// <summary>True for the weather channel (synthetic, no catalog folder).</summary>
    [JsonPropertyName("weather")]
    public bool Weather { get; set; }

    /// <summary>False = defined but not served (user-uploaded tier in v0.2.0.0).</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>Stable XMLTV/M3U channel id.</summary>
    public string ChannelId => "retrotv-" + Slug;

    /// <summary>Does this catalog item belong on this channel?</summary>
    public bool Matches(string folder, string title, string description, int? year)
    {
        if (!Enabled || Weather)
        {
            return false;
        }

        if (!string.Equals(folder, Folder, StringComparison.Ordinal))
        {
            return false;
        }

        if (TitleKeywords is { Count: > 0 }
            && !TitleKeywords.Any(k => title.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (GenreKeywords is { Count: > 0 })
        {
            var haystack = title + " " + description;
            if (!GenreKeywords.Any(k => haystack.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        if (Year.HasValue && year != Year.Value)
        {
            return false;
        }

        return true;
    }
}

/// <summary>Loads the embedded lineup.json. The lineup is data, not code — a future slice can let admins edit it from the config page.</summary>
public static class ChannelLineup
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>All channel definitions, in lineup (number) order.</summary>
    public static IReadOnlyList<ChannelDefinition> Load(ILogger? logger = null)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("lineup.json", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            logger?.LogError("[RetroTv] Embedded lineup.json was not found in the plugin assembly; the guide will have no channels.");
            return Array.Empty<ChannelDefinition>();
        }

        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            var defs = JsonSerializer.Deserialize<List<ChannelDefinition>>(reader.ReadToEnd(), JsonOptions) ?? new List<ChannelDefinition>();
            return defs.OrderBy(d => d.Number).ToList();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[RetroTv] Embedded lineup.json failed to parse: {Message}", ex.Message);
            return Array.Empty<ChannelDefinition>();
        }
    }
}
