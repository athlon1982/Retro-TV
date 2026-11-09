using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RetroTv.Weather;

/// <summary>
/// Open-Meteo client — geocoding + forecast. Free for non-commercial use,
/// NO API key. Chosen over the US National Weather Service because one
/// provider covers geocoding and forecast, with no mandatory headers and
/// trivial JSON (see PLUGIN-DESIGN.md §6). Data is CC BY 4.0: every surface
/// that shows weather must carry "Weather data by Open-Meteo.com".
/// Free tier: 10,000 calls/day. The weather channel needs ~100/day.
/// </summary>
public class OpenMeteoClient
{
    private const string GeocodingBase = "https://geocoding-api.open-meteo.com/v1/search";
    private const string ForecastBase = "https://api.open-meteo.com/v1/forecast";
    private const string ZippopotamBase = "https://api.zippopotam.us/us";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ILogger<OpenMeteoClient> _logger;

    public OpenMeteoClient(HttpClient http, ILogger<OpenMeteoClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Geocoded place for a zip code.</summary>
    public class GeocodedPlace
    {
        public required double Latitude { get; init; }
        public required double Longitude { get; init; }
        public required string PlaceName { get; init; }
        public required string Source { get; init; }
    }

    /// <summary>
    /// Resolve a US zip code to coordinates. Tries Open-Meteo geocoding
    /// first, then Zippopotam.us (the Open-Meteo geocoder is place-name
    /// oriented and misses some postcodes). Returns null when neither can
    /// resolve the zip. The caller caches the result in plugin config — the
    /// geocoder is never called per forecast refresh.
    /// </summary>
    public async Task<GeocodedPlace?> GeocodeAsync(string zip, CancellationToken cancellationToken)
    {
        // Open-Meteo geocoding.
        try
        {
            var url = $"{GeocodingBase}?name={Uri.EscapeDataString(zip)}&count=1&language=en&countryCode=US&format=json";
            using var res = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var data = JsonSerializer.Deserialize<GeocodingResponse>(json, JsonOptions);
                var first = data?.Results is { Count: > 0 } ? data.Results[0] : null;
                if (first is not null)
                {
                    return new GeocodedPlace
                    {
                        Latitude = first.Latitude,
                        Longitude = first.Longitude,
                        PlaceName = first.Name ?? zip,
                        Source = "Open-Meteo geocoding"
                    };
                }
            }
            else
            {
                _logger.LogWarning("[RetroTv] Open-Meteo geocoding for zip {Zip} returned HTTP {Status}", zip, (int)res.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[RetroTv] Open-Meteo geocoding for zip {Zip} failed: {Message}", zip, ex.Message);
        }

        // Fallback: Zippopotam.us (free, no key).
        try
        {
            var url = $"{ZippopotamBase}/{Uri.EscapeDataString(zip)}";
            using var res = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var data = JsonSerializer.Deserialize<ZippopotamResponse>(json, JsonOptions);
                var place = data?.Places is { Count: > 0 } ? data.Places[0] : null;
                if (place is not null
                    && double.TryParse(place.Latitude, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat)
                    && double.TryParse(place.Longitude, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon))
                {
                    return new GeocodedPlace
                    {
                        Latitude = lat,
                        Longitude = lon,
                        PlaceName = $"{place.PlaceName}, {place.StateAbbreviation} {data!.PostCode}",
                        Source = "Zippopotam.us"
                    };
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[RetroTv] Zippopotam.us fallback for zip {Zip} failed: {Message}", zip, ex.Message);
        }

        return null;
    }

    /// <summary>Forecast for cached coordinates: current + 7-day daily. Imperial units, America/New_York timezone.</summary>
    public async Task<Forecast?> GetForecastAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        var url = $"{ForecastBase}?latitude={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            + $"&longitude={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            + "&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m,wind_direction_10m"
            + "&hourly=temperature_2m,precipitation_probability,weather_code"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max"
            + "&temperature_unit=fahrenheit&wind_speed_unit=mph&precipitation_unit=inch"
            + "&timezone=America%2FNew_York&forecast_days=7";
        try
        {
            using var res = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("[RetroTv] Open-Meteo forecast returned HTTP {Status}", (int)res.StatusCode);
                return null;
            }

            var json = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize<ForecastResponse>(json, JsonOptions);
            if (data?.Current is null || data.Daily is null)
            {
                return null;
            }

            return new Forecast
            {
                FetchedAt = DateTimeOffset.UtcNow,
                Latitude = latitude,
                Longitude = longitude,
                Timezone = data.Timezone ?? "America/New_York",
                Current = data.Current,
                Daily = data.Daily
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[RetroTv] Open-Meteo forecast failed: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>One fetched forecast (cached by the caller; the channel serves the last good one on API failure).</summary>
    public class Forecast
    {
        public required DateTimeOffset FetchedAt { get; init; }
        public required double Latitude { get; init; }
        public required double Longitude { get; init; }
        public required string Timezone { get; init; }
        public required CurrentConditions Current { get; init; }
        public required DailyForecast Daily { get; init; }
    }

    public class CurrentConditions
    {
        [JsonPropertyName("temperature_2m")]
        public double? TemperatureF { get; set; }

        [JsonPropertyName("relative_humidity_2m")]
        public double? HumidityPct { get; set; }

        [JsonPropertyName("weather_code")]
        public int? WeatherCode { get; set; }

        [JsonPropertyName("wind_speed_10m")]
        public double? WindMph { get; set; }

        [JsonPropertyName("wind_direction_10m")]
        public double? WindDirectionDeg { get; set; }

        [JsonPropertyName("time")]
        public string? Time { get; set; }
    }

    public class DailyForecast
    {
        [JsonPropertyName("time")]
        public string[] Time { get; set; } = Array.Empty<string>();

        [JsonPropertyName("weather_code")]
        public int?[] WeatherCode { get; set; } = Array.Empty<int?>();

        [JsonPropertyName("temperature_2m_max")]
        public double?[] TemperatureMaxF { get; set; } = Array.Empty<double?>();

        [JsonPropertyName("temperature_2m_min")]
        public double?[] TemperatureMinF { get; set; } = Array.Empty<double?>();

        [JsonPropertyName("precipitation_probability_max")]
        public int?[] PrecipitationProbabilityMax { get; set; } = Array.Empty<int?>();
    }

    private class GeocodingResponse
    {
        [JsonPropertyName("results")]
        public System.Collections.Generic.List<GeocodingResult>? Results { get; set; }
    }

    private class GeocodingResult
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }
    }

    private class ZippopotamResponse
    {
        [JsonPropertyName("post code")]
        public string? PostCode { get; set; }

        [JsonPropertyName("places")]
        public System.Collections.Generic.List<ZippopotamPlace>? Places { get; set; }
    }

    private class ZippopotamPlace
    {
        [JsonPropertyName("place name")]
        public string? PlaceName { get; set; }

        [JsonPropertyName("state abbreviation")]
        public string? StateAbbreviation { get; set; }

        [JsonPropertyName("latitude")]
        public string? Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public string? Longitude { get; set; }
    }

    private class ForecastResponse
    {
        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }

        [JsonPropertyName("current")]
        public CurrentConditions? Current { get; set; }

        [JsonPropertyName("daily")]
        public DailyForecast? Daily { get; set; }
    }
}
