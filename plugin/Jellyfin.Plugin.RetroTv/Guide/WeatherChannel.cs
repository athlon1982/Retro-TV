using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.RetroTv.Weather;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RetroTv.Guide;

/// <summary>
/// The weather channel (BackWhen slot 125 WEATHER). Forecast comes from
/// Open-Meteo (free, keyless, CC BY 4.0 — every surface carries
/// "Weather data by Open-Meteo.com"). The forecast refreshes every 15
/// minutes; the channel keeps serving the last good forecast when the API
/// is down, and marks stale programs "forecast as of &lt;time&gt;" when the
/// data is older than 2 hours. The channel's guide entries are three
/// rotating 5-minute segments (Current Conditions / 7-Day Outlook /
/// Hourly Forecast), so the guide grid reads naturally.
/// </summary>
public class WeatherChannel
{
    /// <summary>One weather segment in the rotating loop.</summary>
    public const int SegmentSeconds = 300;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

    private readonly OpenMeteoClient _weather;
    private readonly ILogger<WeatherChannel> _logger;
    private readonly object _lock = new();
    private OpenMeteoClient.Forecast? _lastForecast;
    private DateTimeOffset _lastFetchAttempt = DateTimeOffset.MinValue;
    private bool _refreshRunning;

    public WeatherChannel(OpenMeteoClient weather, ILogger<WeatherChannel> logger)
    {
        _weather = weather;
        _logger = logger;
    }

    /// <summary>Current cached forecast (null before the first successful fetch).</summary>
    public OpenMeteoClient.Forecast? LastForecast
    {
        get
        {
            lock (_lock)
            {
                return _lastForecast;
            }
        }
    }

    /// <summary>True when the cached forecast is older than 2 hours (or missing).</summary>
    public bool IsStale
    {
        get
        {
            lock (_lock)
            {
                return _lastForecast is null || DateTimeOffset.UtcNow - _lastForecast.FetchedAt > StaleAfter;
            }
        }
    }

    /// <summary>Refresh the forecast if it is older than 15 minutes. Never throws; a failure keeps the last good forecast.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        OpenMeteoClient.Forecast? forecast = null;
        lock (_lock)
        {
            if (_refreshRunning)
            {
                return;
            }

            if (_lastForecast is not null && DateTimeOffset.UtcNow - _lastForecast.FetchedAt < RefreshInterval)
            {
                return;
            }

            if (DateTimeOffset.UtcNow - _lastFetchAttempt < TimeSpan.FromMinutes(1))
            {
                return;
            }

            _refreshRunning = true;
            _lastFetchAttempt = DateTimeOffset.UtcNow;
        }

        try
        {
            var config = Plugin.Instance?.Configuration;
            if (config?.WeatherLatitude is null || config.WeatherLongitude is null)
            {
                _logger.LogWarning("[RetroTv] Weather channel has no coordinates yet — save the zip code on the config page first.");
                return;
            }

            forecast = await _weather.GetForecastAsync(config.WeatherLatitude.Value, config.WeatherLongitude.Value, cancellationToken).ConfigureAwait(false);
            if (forecast is null)
            {
                _logger.LogWarning("[RetroTv] Open-Meteo forecast refresh failed; the weather channel keeps the last good forecast.");
                return;
            }

            lock (_lock)
            {
                _lastForecast = forecast;
            }

            _logger.LogInformation("[RetroTv] Weather forecast refreshed for {Place} ({Zip}).", config.WeatherPlaceName, config.WeatherZipCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[RetroTv] Weather forecast refresh failed; the weather channel keeps the last good forecast: {Message}", ex.Message);
        }
        finally
        {
            lock (_lock)
            {
                _refreshRunning = false;
            }
        }
    }

    /// <summary>Guide schedule for the weather channel: the three segments rotate on a 5-minute cycle, anchored like every other channel.</summary>
    public List<ScheduleEngine.ScheduledProgramme> GetSchedule(DateTimeOffset from, double hours)
    {
        var segments = GetSegments();
        return ScheduleEngine.ScheduleFrom(segments, from, hours);
    }

    /// <summary>The three rotating segments, with text from the latest forecast (or an unavailable slate).</summary>
    public List<ScheduleEngine.GuideItem> GetSegments()
    {
        var forecast = LastForecast;
        var place = Plugin.Instance?.Configuration.WeatherPlaceName ?? string.Empty;
        var staleNote = forecast is null
            ? "forecast unavailable"
            : IsStale
                ? "forecast as of " + forecast.FetchedAt.ToString("g", CultureInfo.InvariantCulture)
                : string.Empty;

        var current = forecast is null
            ? "Weather data is unavailable right now. Please check back shortly."
            : DescribeCurrent(forecast, place);
        var outlook = forecast is null
            ? "The 7-day outlook is unavailable right now."
            : DescribeOutlook(forecast);
        var hourly = forecast is null
            ? "The hourly forecast is unavailable right now."
            : DescribeHourly(forecast);

        if (!string.IsNullOrEmpty(staleNote))
        {
            current += " (" + staleNote + ")";
            outlook += " (" + staleNote + ")";
            hourly += " (" + staleNote + ")";
        }

        return new List<ScheduleEngine.GuideItem>
        {
            new() { Identifier = "weather-current", Title = "Current Conditions" + (string.IsNullOrEmpty(place) ? string.Empty : " — " + place), RuntimeSeconds = SegmentSeconds, Description = current, Tier = "pd" },
            new() { Identifier = "weather-outlook", Title = "7-Day Outlook" + (string.IsNullOrEmpty(place) ? string.Empty : " — " + place), RuntimeSeconds = SegmentSeconds, Description = outlook, Tier = "pd" },
            new() { Identifier = "weather-hourly", Title = "Hourly Forecast" + (string.IsNullOrEmpty(place) ? string.Empty : " — " + place), RuntimeSeconds = SegmentSeconds, Description = hourly, Tier = "pd" }
        };
    }

    /// <summary>Text lines for the stream slate renderer (ffmpeg drawtext). First line is the segment title; the rest is the forecast.</summary>
    public List<string> BuildSlateLines(string segmentIdentifier)
    {
        var forecast = LastForecast;
        var place = Plugin.Instance?.Configuration.WeatherPlaceName ?? "Wallingford, VT";
        var lines = new List<string> { "RETRO TV  •  WEATHER  •  " + place.ToUpperInvariant() };
        if (forecast is null)
        {
            lines.Add("Weather data is unavailable right now.");
            lines.Add("Please check back shortly.");
        }
        else
        {
            switch (segmentIdentifier)
            {
                case "weather-outlook":
                    lines.Add("7-DAY OUTLOOK");
                    lines.AddRange(OutlookLines(forecast));
                    break;
                case "weather-hourly":
                    lines.Add("HOURLY FORECAST");
                    lines.AddRange(HourlyLines(forecast));
                    break;
                default:
                    lines.Add("CURRENT CONDITIONS");
                    lines.AddRange(CurrentLines(forecast));
                    break;
            }

            if (IsStale)
            {
                lines.Add("Forecast as of " + forecast.FetchedAt.ToString("g", CultureInfo.InvariantCulture));
            }
        }

        lines.Add("Weather data by Open-Meteo.com");
        return lines;
    }

    private static List<string> CurrentLines(OpenMeteoClient.Forecast forecast)
    {
        var c = forecast.Current;
        var lines = new List<string>();
        lines.Add(DescribeCode(c.WeatherCode) + (c.TemperatureF.HasValue ? $"  •  {c.TemperatureF.Value:F0} F" : string.Empty));
        if (c.WindMph.HasValue)
        {
            lines.Add($"Wind {Compass(c.WindDirectionDeg)} {c.WindMph.Value:F0} mph");
        }

        if (c.HumidityPct.HasValue)
        {
            lines.Add($"Humidity {c.HumidityPct.Value:F0}%");
        }

        lines.AddRange(OutlookLines(forecast).Take(3));
        return lines;
    }

    private static List<string> OutlookLines(OpenMeteoClient.Forecast forecast)
    {
        var lines = new List<string>();
        var d = forecast.Daily;
        var days = Math.Min(7, d.Time.Length);
        for (var i = 0; i < days; i++)
        {
            var date = DateTime.TryParse(d.Time[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt.ToString("ddd MMM d", CultureInfo.InvariantCulture)
                : d.Time[i];
            var hi = i < d.TemperatureMaxF.Length && d.TemperatureMaxF[i].HasValue ? $"{d.TemperatureMaxF[i].GetValueOrDefault():F0}F" : "?";
            var lo = i < d.TemperatureMinF.Length && d.TemperatureMinF[i].HasValue ? $"{d.TemperatureMinF[i].GetValueOrDefault():F0}F" : "?";
            var code = i < d.WeatherCode.Length ? DescribeCode(d.WeatherCode[i]) : string.Empty;
            var precip = i < d.PrecipitationProbabilityMax.Length && d.PrecipitationProbabilityMax[i].HasValue ? $"  •  {d.PrecipitationProbabilityMax[i].GetValueOrDefault()}% precip" : string.Empty;
            lines.Add($"{date}: {code}  •  {hi} / {lo}{precip}");
        }

        return lines;
    }

    private static List<string> HourlyLines(OpenMeteoClient.Forecast forecast)
    {
        // The forecast response carries hourly arrays only when requested; this
        // client requests them, but the Forecast model keeps current + daily.
        // The hourly segment therefore shows the 7-day outlook until the
        // hourly arrays are surfaced in the model (a later slice).
        var lines = new List<string> { "Next 7 days:" };
        lines.AddRange(OutlookLines(forecast).Take(5));
        return lines;
    }

    private static string DescribeCurrent(OpenMeteoClient.Forecast forecast, string place)
    {
        var c = forecast.Current;
        var parts = new List<string>();
        if (c.TemperatureF.HasValue)
        {
            parts.Add($"{c.TemperatureF.Value:F0} F");
        }

        parts.Add(DescribeCode(c.WeatherCode));
        if (c.WindMph.HasValue)
        {
            parts.Add($"wind {Compass(c.WindDirectionDeg)} {c.WindMph.Value:F0} mph");
        }

        if (c.HumidityPct.HasValue)
        {
            parts.Add($"humidity {c.HumidityPct.Value:F0}%");
        }

        return (string.IsNullOrEmpty(place) ? "Current conditions: " : "Current conditions in " + place + ": ")
               + string.Join(", ", parts) + ".";
    }

    private static string DescribeOutlook(OpenMeteoClient.Forecast forecast)
    {
        var d = forecast.Daily;
        var parts = new List<string>();
        var days = Math.Min(3, d.Time.Length);
        for (var i = 0; i < days; i++)
        {
            var date = DateTime.TryParse(d.Time[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt.ToString("ddd", CultureInfo.InvariantCulture)
                : d.Time[i];
            var hi = i < d.TemperatureMaxF.Length && d.TemperatureMaxF[i].HasValue ? $"{d.TemperatureMaxF[i].GetValueOrDefault():F0}F" : "?";
            var lo = i < d.TemperatureMinF.Length && d.TemperatureMinF[i].HasValue ? $"{d.TemperatureMinF[i].GetValueOrDefault():F0}F" : "?";
            var code = i < d.WeatherCode.Length ? DescribeCode(d.WeatherCode[i]) : string.Empty;
            parts.Add($"{date} {code} {hi}/{lo}");
        }

        return "7-day outlook: " + string.Join("  •  ", parts) + ".";
    }

    private static string DescribeHourly(OpenMeteoClient.Forecast forecast)
    {
        return DescribeOutlook(forecast);
    }

    /// <summary>WMO weather code → plain words.</summary>
    public static string DescribeCode(int? code)
    {
        return code switch
        {
            0 => "Clear sky",
            1 => "Mainly clear",
            2 => "Partly cloudy",
            3 => "Overcast",
            45 or 48 => "Fog",
            51 => "Light drizzle",
            53 => "Drizzle",
            55 => "Dense drizzle",
            56 or 57 => "Freezing drizzle",
            61 => "Light rain",
            63 => "Rain",
            65 => "Heavy rain",
            66 or 67 => "Freezing rain",
            71 => "Light snow",
            73 => "Snow",
            75 => "Heavy snow",
            77 => "Snow grains",
            80 => "Light rain showers",
            81 => "Rain showers",
            82 => "Violent rain showers",
            85 or 86 => "Snow showers",
            95 => "Thunderstorm",
            96 or 99 => "Thunderstorm with hail",
            null => "Unknown conditions",
            _ => "Weather code " + code.Value.ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>Wind direction degrees → compass point.</summary>
    public static string Compass(double? degrees)
    {
        if (degrees is null)
        {
            return string.Empty;
        }

        var points = new[] { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
        var idx = (int)Math.Round(degrees.Value / 22.5) % 16;
        return points[idx];
    }
}
