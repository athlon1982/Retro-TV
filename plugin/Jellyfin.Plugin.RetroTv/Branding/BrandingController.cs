using System;
using System.IO;
using System.Reflection;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Branding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RetroTv.Branding;

/// <summary>
/// One-click theme apply for Jellyfin's own Live TV guide. The plugin's
/// themes style its own guide page (/RetroTv/Guide), but Jellyfin's
/// built-in guide only picks up styling from Dashboard &gt; General &gt;
/// Custom CSS (Branding) — and without the channel-name fix in that CSS
/// the built-in guide shows no channel names at all. This endpoint writes
/// the active theme's CSS into Branding for the admin, so a new user
/// never has to copy-paste CSS by hand. It preserves any CSS already in
/// Branding (only a previous Retro TV block is replaced), and it takes
/// effect on next page load with no restart. Admin only.
/// </summary>
[ApiController]
[Route("RetroTv/Branding")]
[Authorize(Policy = "RequiresElevation")]
public class BrandingController : ControllerBase
{
    /// <summary>Start of the block this plugin manages inside Branding CustomCss.</summary>
    private const string StartMarker = "/* Retro TV theme:";

    /// <summary>End of the block this plugin manages inside Branding CustomCss.</summary>
    private const string EndMarker = "/* end Retro TV theme */";

    private readonly ILogger<BrandingController> _logger;
    private readonly IConfigurationManager _configManager;

    public BrandingController(ILogger<BrandingController> logger, IConfigurationManager configManager)
    {
        _logger = logger;
        _configManager = configManager;
    }

    /// <summary>Apply a theme's branding CSS to Jellyfin's Custom CSS. ?theme=&lt;id&gt; picks the theme (default: the saved active theme).</summary>
    [HttpPost("ApplyTheme")]
    public ActionResult<object> ApplyTheme([FromQuery] string? theme = null)
    {
        var themeId = (theme ?? Plugin.Instance?.Configuration.ActiveGuideTheme ?? "woodgrain-78")
            .Trim().ToLowerInvariant();
        var css = LoadBrandingCss(themeId);
        if (css is null)
        {
            return NotFound(new { error = $"No branding CSS for theme '{themeId}'." });
        }

        var branding = (BrandingOptions)_configManager.GetConfiguration("branding");
        var before = branding.CustomCss ?? string.Empty;
        var stripped = StripRetroTvBlock(before);
        var replacedPrevious = stripped.Length != before.Length;
        var preservedExistingCss = stripped.Trim().Length > 0;

        var block = $"{StartMarker} {themeId} — applied by the Retro TV plugin. Delete this block to remove the theme. */\n{css}\n{EndMarker}";
        branding.CustomCss = (preservedExistingCss ? stripped.TrimEnd() + "\n\n" : string.Empty) + block + "\n";
        _configManager.SaveConfiguration("branding", branding);

        _logger.LogInformation("[RetroTv] Applied branding theme {Theme} (replaced previous: {Replaced}, preserved other CSS: {Preserved}).", themeId, replacedPrevious, preservedExistingCss);
        return Ok(new
        {
            applied = true,
            theme = themeId,
            customCssLength = branding.CustomCss.Length,
            replacedPrevious,
            preservedExistingCss
        });
    }

    /// <summary>Whether Branding currently carries a Retro TV theme block, and which theme it is.</summary>
    [HttpGet("Status")]
    public ActionResult<object> Status()
    {
        var branding = (BrandingOptions)_configManager.GetConfiguration("branding");
        var css = branding.CustomCss ?? string.Empty;
        var theme = ExtractThemeId(css);
        return Ok(new
        {
            hasRetroTvTheme = theme is not null,
            theme = theme ?? string.Empty,
            customCssLength = css.Length
        });
    }

    private static string? LoadBrandingCss(string themeId)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"Jellyfin.Plugin.RetroTv.Guide.Themes.Branding.retro-tv-livetv-{themeId}.css";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string StripRetroTvBlock(string css)
    {
        var start = css.IndexOf(StartMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return css;
        }

        var end = css.IndexOf(EndMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            return css;
        }

        return css.Remove(start, end + EndMarker.Length - start);
    }

    private static string? ExtractThemeId(string css)
    {
        var start = css.IndexOf(StartMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var after = css.Substring(start + StartMarker.Length).TrimStart();
        // Theme ids contain hyphens (woodgrain-78), so only whitespace ends the id.
        var end = after.IndexOfAny(new[] { ' ', '\n', '\r', '\t' });
        if (end <= 0)
        {
            return null;
        }

        return after.Substring(0, end).Trim();
    }
}
