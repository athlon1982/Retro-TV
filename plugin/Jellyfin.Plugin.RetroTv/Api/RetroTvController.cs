using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RetroTv.Catalog;
using Jellyfin.Plugin.RetroTv.Guide;
using Jellyfin.Plugin.RetroTv.Weather;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RetroTv.Api;

/// <summary>
/// Retro TV admin API. All endpoints require elevation (admin only) — the
/// catalog build writes files to disk and the weather endpoints call out to
/// the internet, so neither is open to regular users. Every endpoint is
/// fast and non-blocking: long work runs on a background task behind the
/// CatalogBuildState single-flight guard.
///
/// NOTE: Jellyfin 12 does not publish a Jellyfin.Api NuGet package, so there
/// is no BaseJellyfinApiController to inherit. This is a plain ASP.NET Core
/// ControllerBase; Jellyfin discovers controllers from plugin assemblies
/// (application parts) at startup, and the RequiresElevation policy is
/// resolved by the server at runtime.
///
/// The public guide endpoints (guide page, M3U, XMLTV, themes, live
/// streams) live in Guide/GuideController.cs and LiveTv/LiveTvController.cs
/// — they are anonymous by design, because Jellyfin's own Live TV tuner
/// fetches them server-side with no user session.
/// </summary>
[ApiController]
[Route("RetroTv")]
[Authorize(Policy = "RequiresElevation")]
public class RetroTvController : ControllerBase
{
    private readonly ILogger<RetroTvController> _logger;
    private readonly RetroTvCatalogBuilder _builder;
    private readonly CatalogBuildState _state;
    private readonly GuideService _guide;
    private readonly OpenMeteoClient _weather;

    public RetroTvController(
        ILogger<RetroTvController> logger,
        RetroTvCatalogBuilder builder,
        CatalogBuildState state,
        GuideService guide,
        OpenMeteoClient weather)
    {
        _logger = logger;
        _builder = builder;
        _state = state;
        _guide = guide;
        _weather = weather;
    }

    /// <summary>Last catalog build status (or "never run"). Used by the config page.</summary>
    [HttpGet("Status")]
    public ActionResult<object> GetStatus()
    {
        var last = _state.LastBuild;
        return Ok(new
        {
            isRunning = _state.IsRunning,
            lastBuild = last is null
                ? null
                : new
                {
                    finishedAt = last.FinishedAt,
                    pilot = last.Pilot,
                    version = last.Version,
                    items = last.ItemCount,
                    skipped = last.SkippedCount,
                    durationSeconds = Math.Round(last.Duration.TotalSeconds, 1),
                    trigger = last.Trigger,
                    error = last.Error
                },
            outputFolder = Plugin.Instance?.Configuration.OutputFolder ?? string.Empty,
            libraryName = Plugin.Instance?.Configuration.LibraryName ?? "Retro TV"
        });
    }

    /// <summary>
    /// Start a catalog build now (pilot or full). Returns 202 when the build
    /// starts, 409 when one is already running, 400 when the plugin is not
    /// configured yet. The build runs on a background task; poll GetStatus.
    /// </summary>
    [HttpPost("Build")]
    public ActionResult<object> BuildNow([FromQuery] bool pilot = false)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return BadRequest(new { error = "Plugin instance is not loaded." });
        }

        var config = plugin.Configuration;
        if (string.IsNullOrWhiteSpace(config.OutputFolder))
        {
            return BadRequest(new { error = "OutputFolder is not set. Open the Retro TV plugin settings and choose the output folder first." });
        }

        if (!_state.TryBegin())
        {
            return Conflict(new { error = "A catalog build is already running." });
        }

        var startedAt = DateTimeOffset.UtcNow;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _builder.BuildAsync(config, pilot, progress: null, CancellationToken.None).ConfigureAwait(false);
                _state.End(new CatalogBuildState.LastBuildSummary
                {
                    FinishedAt = DateTimeOffset.UtcNow,
                    Pilot = pilot,
                    Version = result.Version,
                    ItemCount = result.ItemCount,
                    SkippedCount = result.SkippedCount,
                    Duration = DateTimeOffset.UtcNow - startedAt,
                    Trigger = "api"
                });

                // The guide grows with the catalog: refresh the channel pools
                // from the catalog just written (best-effort, never fails
                // the build — the old pools keep serving on failure).
                _guide.RefreshPoolsFromCatalog(Path.Combine(config.OutputFolder, "catalog.json"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[RetroTv] API-triggered catalog build failed: {Message}", ex.Message);
                _state.End(new CatalogBuildState.LastBuildSummary
                {
                    FinishedAt = DateTimeOffset.UtcNow,
                    Pilot = pilot,
                    Version = 0,
                    ItemCount = 0,
                    SkippedCount = 0,
                    Duration = DateTimeOffset.UtcNow - startedAt,
                    Trigger = "api",
                    Error = ex.Message
                });
            }
        });

        return Accepted(new { started = true, pilot });
    }

    /// <summary>Resolve a zip code to coordinates (Open-Meteo geocoding, Zippopotam.us fallback). Used by the config page when the zip changes; the resolved values are saved into config by the page.</summary>
    [HttpGet("Weather/Geocode")]
    public async Task<ActionResult<object>> GeocodeZip([FromQuery] string zip)
    {
        if (string.IsNullOrWhiteSpace(zip))
        {
            return BadRequest(new { error = "zip is required." });
        }

        var result = await _weather.GeocodeAsync(zip.Trim(), CancellationToken.None).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound(new { error = $"Could not resolve zip code '{zip}' to a place." });
        }

        return Ok(new
        {
            zip = zip.Trim(),
            latitude = result.Latitude,
            longitude = result.Longitude,
            placeName = result.PlaceName,
            source = result.Source
        });
    }

    /// <summary>Current + daily forecast for the configured (cached) coordinates. 503 when coordinates are not resolved yet.</summary>
    [HttpGet("Weather/Forecast")]
    public async Task<ActionResult<object>> GetForecast()
    {
        var config = Plugin.Instance?.Configuration;
        if (config?.WeatherLatitude is null || config.WeatherLongitude is null)
        {
            return StatusCode(503, new { error = "Weather coordinates are not resolved yet. Save the zip code on the config page first." });
        }

        var forecast = await _weather.GetForecastAsync(config.WeatherLatitude.Value, config.WeatherLongitude.Value, CancellationToken.None).ConfigureAwait(false);
        if (forecast is null)
        {
            return StatusCode(502, new { error = "Open-Meteo did not return a forecast. Try again in a few minutes." });
        }

        return Ok(new
        {
            placeName = config.WeatherPlaceName,
            zip = config.WeatherZipCode,
            fetchedAt = forecast.FetchedAt,
            current = forecast.Current,
            daily = forecast.Daily,
            attribution = "Weather data by Open-Meteo.com"
        });
    }
}
