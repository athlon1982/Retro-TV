using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RetroTv.Catalog;

/// <summary>
/// Internet Archive client — advancedsearch + metadata + file download.
/// Port of the IAClient in src/catalog_builder.mjs. Reads only, no key, no
/// account. Polite by construction: ~1 request/second (configurable),
/// exponential back-off on 429/503, descriptive User-Agent on every request.
/// All methods honor the caller's CancellationToken.
/// </summary>
public class InternetArchiveClient
{
    public const string BaseUrl = "https://archive.org";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ILogger<InternetArchiveClient> _logger;
    private readonly SemaphoreSlim _throttleLock = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public InternetArchiveClient(HttpClient http, ILogger<InternetArchiveClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Minimum gap between requests, from config (requests/second).</summary>
    public double RequestsPerSecond { get; set; } = 1;

    /// <summary>User-Agent sent on every request (set from config).</summary>
    public string UserAgent { get; set; } = "RetroTV/0.1 (personal Jellyfin project)";

    private async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        var minGap = TimeSpan.FromMilliseconds(Math.Ceiling(1000.0 / Math.Max(0.1, RequestsPerSecond)));
        await _throttleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = minGap - (DateTimeOffset.UtcNow - _lastRequestAt);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            _lastRequestAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _throttleLock.Release();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken cancellationToken, int tries = 5)
    {
        for (var attempt = 0; attempt < tries; attempt++)
        {
            await ThrottleAsync(cancellationToken).ConfigureAwait(false);
            HttpResponseMessage res;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < tries - 1 && ex is not OperationCanceledException)
            {
                _logger.LogWarning("[RetroTv] IA request failed ({Message}); retry {Attempt}/{Tries}", ex.Message, attempt + 1, tries);
                await Task.Delay(TimeSpan.FromSeconds(2 * Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (res.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                var retryAfter = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * Math.Pow(2, attempt));
                _logger.LogInformation("[RetroTv] IA rate-limited ({Status}); waiting {Seconds}s", (int)res.StatusCode, Math.Round(retryAfter.TotalSeconds));
                res.Dispose();
                await Task.Delay(retryAfter, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!res.IsSuccessStatusCode)
            {
                var body = string.Empty;
                try
                {
                    body = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Best effort only; the status is the error.
                }

                res.Dispose();
                throw new HttpRequestException($"IA HTTP {(int)res.StatusCode} for {url} {body}");
            }

            return res;
        }

        throw new HttpRequestException($"IA request failed after {tries} tries: {url}");
    }

    /// <summary>Advanced Search: returns (docs, numFound). Deep paging is capped at ~10k by IA — the builder uses the Scrape API for full bulk enumeration in a later slice.</summary>
    public async Task<(List<IaSearchDoc> Docs, int NumFound)> SearchAsync(string query, int rows, int page, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/advancedsearch.php?q={Uri.EscapeDataString(query)}&fl%5B%5D=identifier&fl%5B%5D=title&rows={rows}&page={page}&output=json";
        using var res = await SendAsync(url, cancellationToken).ConfigureAwait(false);
        var json = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var data = JsonSerializer.Deserialize<IaSearchResponse>(json, JsonOptions);
        return (data?.Response?.Docs ?? new List<IaSearchDoc>(), data?.Response?.NumFound ?? 0);
    }

    /// <summary>Full metadata record for one identifier (metadata + files list).</summary>
    public async Task<IaMetadataResponse> MetadataAsync(string identifier, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/metadata/{Uri.EscapeDataString(identifier)}";
        using var res = await SendAsync(url, cancellationToken).ConfigureAwait(false);
        var json = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var data = JsonSerializer.Deserialize<IaMetadataResponse>(json, JsonOptions)
                   ?? throw new InvalidOperationException($"No metadata for {identifier}");
        if (data.Metadata is null)
        {
            throw new InvalidOperationException($"No metadata for {identifier}");
        }

        return data;
    }

    /// <summary>Download one file's bytes (posters). Same throttling and back-off as everything else.</summary>
    public async Task<byte[]> DownloadBytesAsync(string url, CancellationToken cancellationToken)
    {
        using var res = await SendAsync(url, cancellationToken).ConfigureAwait(false);
        return await res.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---- Minimal IA response shapes (only the fields the builder uses) ----

    public class IaSearchResponse
    {
        [JsonPropertyName("response")]
        public IaSearchResponseBody? Response { get; set; }
    }

    public class IaSearchResponseBody
    {
        [JsonPropertyName("numFound")]
        public int NumFound { get; set; }

        [JsonPropertyName("docs")]
        public List<IaSearchDoc> Docs { get; set; } = new();
    }

    public class IaSearchDoc
    {
        [JsonPropertyName("identifier")]
        public string Identifier { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    public class IaMetadataResponse
    {
        [JsonPropertyName("metadata")]
        public IaMetadata? Metadata { get; set; }

        [JsonPropertyName("files")]
        public List<IaFile> Files { get; set; } = new();
    }

    public class IaMetadata
    {
        [JsonPropertyName("identifier")]
        public string? Identifier { get; set; }

        [JsonPropertyName("title")]
        public JsonElement Title { get; set; }

        [JsonPropertyName("year")]
        public JsonElement Year { get; set; }

        [JsonPropertyName("runtime")]
        public JsonElement Runtime { get; set; }

        [JsonPropertyName("description")]
        public JsonElement Description { get; set; }

        [JsonPropertyName("licenseurl")]
        public JsonElement LicenseUrl { get; set; }

        [JsonPropertyName("collection")]
        public JsonElement Collection { get; set; }
    }

    public class IaFile
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("format")]
        public string? Format { get; set; }

        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonPropertyName("length")]
        [JsonConverter(typeof(LenientNullableDoubleConverter))]
        public double? Length { get; set; }
    }

    /// <summary>
    /// Reads a nullable double from a JSON number OR a numeric string.
    /// The IA metadata API returns file "length" as a string for many items
    /// (e.g. "length": "1234.5"); a plain double? property throws on those
    /// and the whole item is skipped. This keeps the port as forgiving as
    /// the Node builder (JSON.parse is untyped). Non-numeric values read
    /// as null (unknown), never as an exception.
    /// </summary>
    public sealed class LenientNullableDoubleConverter : JsonConverter<double?>
    {
        public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetDouble(),
                JsonTokenType.String => double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null,
                JsonTokenType.Null => null,
                _ => null
            };
        }

        public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteNumberValue(value.Value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
