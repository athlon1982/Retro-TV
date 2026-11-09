using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RetroTv.Guide.Themes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RetroTv.Guide;

/// <summary>
/// Public guide endpoints — no login required. Jellyfin's own Live TV tuner
/// fetches the M3U + XMLTV server-side with no user session, and the guide
/// page is meant to be opened on any device with a browser, so these are
/// anonymous by design (same as the Stage 2 sidecar's endpoints). They
/// expose only public catalog data (Internet Archive URLs and schedule
/// times) — no config, no keys, no file writes. When the ChannelsEnabled
/// master switch is off, the data endpoints answer 503.
/// </summary>
[ApiController]
[Route("RetroTv")]
[AllowAnonymous]
public class GuideController : ControllerBase
{
    private static readonly JsonSerializerOptions CamelCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<GuideController> _logger;
    private readonly GuideService _guide;
    private readonly ThemeService _themes;
    private readonly WeatherChannel _weather;

    public GuideController(
        ILogger<GuideController> logger,
        GuideService guide,
        ThemeService themes,
        WeatherChannel weather)
    {
        _logger = logger;
        _guide = guide;
        _themes = themes;
        _weather = weather;
    }

    /// <summary>The themeable guide page (embedded resource). Jellyfin's built-in Live TV guide cannot be re-themed — this page is the themeable surface.</summary>
    [HttpGet("Guide")]
    public ActionResult GuidePage()
    {
        var html = ReadEmbeddedResource("guidePage.html");
        if (html is null)
        {
            return NotFound("Guide page is missing from the plugin assembly.");
        }

        return Content(html, "text/html");
    }

    /// <summary>Guide data as JSON (channels + programs + now-line), the same schedule the XMLTV feed carries. ?hours= (1-48, default 48). ?theme= is read by the page, not the API.</summary>
    [HttpGet("Guide/Data")]
    public ActionResult GuideData([FromQuery] double hours = 48)
    {
        if (!ChannelsEnabled())
        {
            return StatusCode(503, "Retro TV live channels are disabled. Enable them on the Retro TV plugin settings page.");
        }

        hours = Math.Clamp(hours, 1, 48);
        _ = _weather.RefreshAsync(CancellationToken.None).ContinueWith(
            t => _logger.LogWarning(t.Exception, "[RetroTv] Background weather refresh faulted."),
            TaskContinuationOptions.OnlyOnFaulted);
        var json = JsonSerializer.Serialize(_guide.BuildGuideJson(hours), CamelCaseJson);
        return Content(json, "application/json");
    }

    /// <summary>M3U playlist for Jellyfin's Live TV tuner (Dashboard → Live TV → Tuner → M3U). Stream URLs are absolute, built from this request's host.</summary>
    [HttpGet("channels.m3u")]
    public ActionResult ChannelsM3u()
    {
        if (!ChannelsEnabled())
        {
            return StatusCode(503, "Retro TV live channels are disabled. Enable them on the Retro TV plugin settings page.");
        }

        return Content(_guide.BuildM3u(BaseUrl()), "audio/x-mpegurl");
    }

    /// <summary>XMLTV guide (48 h) for Jellyfin's XMLTV provider. Add the M3U tuner first, then this guide — refresh Channels first, then Refresh Guide, or the guide comes up blank.</summary>
    [HttpGet("guide.xml")]
    public ActionResult GuideXml()
    {
        if (!ChannelsEnabled())
        {
            return StatusCode(503, "Retro TV live channels are disabled. Enable them on the Retro TV plugin settings page.");
        }

        return Content(_guide.BuildXmltv(48), "application/xml");
    }

    /// <summary>All available guide themes (built-ins + valid user themes) for the config page dropdown and the guide page switcher.</summary>
    [HttpGet("Guide/Themes")]
    public ActionResult<object> GetGuideThemes()
    {
        return Ok(_themes.GetAllThemes());
    }

    /// <summary>One guide theme definition. ?theme=&lt;id&gt; previews a theme without changing the saved one (the guide page uses this).</summary>
    [HttpGet("Guide/Theme")]
    public ActionResult<ThemeDefinition> GetGuideTheme([FromQuery] string? theme = null)
    {
        var activeId = Plugin.Instance?.Configuration.ActiveGuideTheme ?? "woodgrain-78";
        var definition = _themes.GetTheme(theme ?? activeId) ?? _themes.GetTheme("woodgrain-78");
        if (definition is null)
        {
            return NotFound(new { error = "No guide themes are available." });
        }

        return Ok(definition);
    }

    /// <summary>The v5 Retro TV icon (TV on a dual-deck VCR with VHS + BETA tapes) — used as the channel logo in the M3U and the guide page header.</summary>
    [HttpGet("Guide/Icon")]
    public ActionResult GuideIcon()
    {
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var iconPath = assemblyDir is null ? null : Path.Combine(assemblyDir, "icon.webp");
        if (iconPath is null || !System.IO.File.Exists(iconPath))
        {
            return NotFound("Icon is missing from the plugin folder.");
        }

        return PhysicalFile(iconPath, "image/webp");
    }

    /// <summary>Current weather summary for the configured zip (place, temperature, condition) — the guide page header widget. Anonymous; reads only the cached forecast.</summary>
    [HttpGet("Guide/Weather")]
    public ActionResult<object> GuideWeather()
    {
        var config = Plugin.Instance?.Configuration;
        var forecast = _weather.LastForecast;
        _ = _weather.RefreshAsync(CancellationToken.None).ContinueWith(
            t => _logger.LogWarning(t.Exception, "[RetroTv] Background weather refresh faulted."),
            TaskContinuationOptions.OnlyOnFaulted);

        if (forecast is null)
        {
            return Ok(new
            {
                placeName = config?.WeatherPlaceName ?? string.Empty,
                zip = config?.WeatherZipCode ?? string.Empty,
                temperatureF = (double?)null,
                condition = "unavailable",
                attribution = "Weather data by Open-Meteo.com"
            });
        }

        return Ok(new
        {
            placeName = config?.WeatherPlaceName ?? string.Empty,
            zip = config?.WeatherZipCode ?? string.Empty,
            temperatureF = forecast.Current.TemperatureF,
            condition = WeatherChannel.DescribeCode(forecast.Current.WeatherCode),
            fetchedAt = forecast.FetchedAt,
            stale = _weather.IsStale,
            attribution = "Weather data by Open-Meteo.com"
        });
    }

    private bool ChannelsEnabled()
    {
        return Plugin.Instance?.Configuration.ChannelsEnabled ?? false;
    }

    private string BaseUrl()
    {
        return Request.Scheme + "://" + Request.Host.Value;
    }

    private static string? ReadEmbeddedResource(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
