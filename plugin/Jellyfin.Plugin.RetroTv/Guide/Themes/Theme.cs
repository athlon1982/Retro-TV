using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RetroTv.Guide.Themes;

/// <summary>
/// Guide theme model + loader. A theme is one JSON file: colors, fonts,
/// density, poster/show options, and a CRT overlay definition. The guide
/// page applies a theme purely by setting CSS custom properties — no
/// per-theme HTML. Built-in themes are embedded resources (they always
/// exist); user themes are optional JSON files in the plugin data dir's
/// Themes folder. An invalid user theme is ignored with a log line — a bad
/// theme file never breaks the guide; the default theme is the fallback.
/// </summary>
public class ThemeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<ThemeService> _logger;
    private readonly string _userThemesDir;
    private readonly Dictionary<string, ThemeDefinition> _themes = new(StringComparer.OrdinalIgnoreCase);

    public ThemeService(IApplicationPaths applicationPaths, ILogger<ThemeService> logger)
    {
        _logger = logger;
        _userThemesDir = Path.Combine(applicationPaths.ProgramDataPath, "RetroTv", "Themes");
        LoadThemes();
    }

    /// <summary>Default theme id. Used whenever the configured theme is missing or invalid.</summary>
    public const string DefaultThemeId = "woodgrain-78";

    public IReadOnlyList<ThemeDefinition> GetAllThemes()
    {
        lock (_themes)
        {
            return _themes.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        }
    }

    public ThemeDefinition? GetTheme(string id)
    {
        lock (_themes)
        {
            if (_themes.TryGetValue(id, out var theme))
            {
                return theme;
            }

            return null;
        }
    }

    private void LoadThemes()
    {
        // 1. Built-ins (embedded resources — always present).
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || !resourceName.Contains("Themes", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream);
                AddTheme(JsonSerializer.Deserialize<ThemeDefinition>(reader.ReadToEnd(), JsonOptions), source: "built-in " + resourceName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RetroTv] Built-in theme resource {Resource} failed to load: {Message}", resourceName, ex.Message);
            }
        }

        // 2. User themes (optional files in the plugin data dir).
        try
        {
            Directory.CreateDirectory(_userThemesDir);
            foreach (var file in Directory.GetFiles(_userThemesDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    AddTheme(JsonSerializer.Deserialize<ThemeDefinition>(json, JsonOptions), source: "user theme " + Path.GetFileName(file));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[RetroTv] User theme file {File} is invalid and was ignored: {Message}", file, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RetroTv] Could not read the user themes folder {Dir}: {Message}", _userThemesDir, ex.Message);
        }

        // 3. Guarantee: the default theme must exist, or the guide has nothing to fall back to.
        if (!_themes.ContainsKey(DefaultThemeId))
        {
            _logger.LogError("[RetroTv] Default guide theme '{Id}' is missing from the embedded resources; the guide will have no theme to fall back to.", DefaultThemeId);
        }
    }

    private void AddTheme(ThemeDefinition? theme, string source)
    {
        if (theme is null || string.IsNullOrWhiteSpace(theme.Id) || string.IsNullOrWhiteSpace(theme.Name) || theme.Colors is null)
        {
            _logger.LogWarning("[RetroTv] Theme from {Source} is missing id, name, or colors and was ignored.", source);
            return;
        }

        lock (_themes)
        {
            _themes[theme.Id] = theme;
        }
    }
}

/// <summary>One guide theme (see PLUGIN-DESIGN.md §5 for the full schema).</summary>
public class ThemeDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("colors")]
    public ThemeColors Colors { get; set; } = new();

    [JsonPropertyName("fonts")]
    public ThemeFonts Fonts { get; set; } = new();

    /// <summary>"compact" | "comfortable" | "roomy".</summary>
    [JsonPropertyName("density")]
    public string Density { get; set; } = "comfortable";

    /// <summary>Program blocks carry poster art.</summary>
    [JsonPropertyName("showPosters")]
    public bool ShowPosters { get; set; } = true;

    [JsonPropertyName("crtOverlay")]
    public CrtOverlay CrtOverlay { get; set; } = new();

    [JsonPropertyName("channelColumnWidth")]
    public int ChannelColumnWidth { get; set; } = 220;
}

public class ThemeColors
{
    [JsonPropertyName("background")]
    public string Background { get; set; } = "#000000";

    [JsonPropertyName("panel")]
    public string Panel { get; set; } = "#111111";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "#ffffff";

    [JsonPropertyName("mutedText")]
    public string MutedText { get; set; } = "#aaaaaa";

    [JsonPropertyName("accent")]
    public string Accent { get; set; } = "#e8a33d";

    [JsonPropertyName("header")]
    public string Header { get; set; } = "#000000";

    [JsonPropertyName("gridLine")]
    public string GridLine { get; set; } = "#333333";

    [JsonPropertyName("nowLine")]
    public string NowLine { get; set; } = "#ff5a3c";

    [JsonPropertyName("programBlock")]
    public string ProgramBlock { get; set; } = "#222222";

    [JsonPropertyName("programBlockHover")]
    public string ProgramBlockHover { get; set; } = "#333333";

    [JsonPropertyName("liveBadge")]
    public string LiveBadge { get; set; } = "#c0392b";
}

public class ThemeFonts
{
    [JsonPropertyName("display")]
    public string Display { get; set; } = "Georgia, 'Times New Roman', serif";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "Georgia, serif";

    [JsonPropertyName("mono")]
    public string Mono { get; set; } = "'Courier New', monospace";
}

public class CrtOverlay
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Scanline opacity 0-1.</summary>
    [JsonPropertyName("scanlines")]
    public double Scanlines { get; set; }

    /// <summary>Vignette opacity 0-1.</summary>
    [JsonPropertyName("vignette")]
    public double Vignette { get; set; }

    /// <summary>Tracking-noise opacity 0-1.</summary>
    [JsonPropertyName("trackingNoise")]
    public double TrackingNoise { get; set; }
}
