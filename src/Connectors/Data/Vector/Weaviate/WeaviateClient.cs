using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;

namespace Koan.Data.Vector.Connector.Weaviate;

internal sealed class WeaviateClient
{
    private readonly HttpClient _http;
    private readonly WeaviateOptions _options;
    private readonly ILogger<WeaviateClient>? _logger;

    public WeaviateClient(IHttpClientFactory httpFactory, IOptions<WeaviateOptions> options, ILogger<WeaviateClient>? logger)
    {
        _http = httpFactory.CreateClient("weaviate");
        _options = options.Value;
        _logger = logger;
        _http.BaseAddress = new Uri(_options.Endpoint);
        if (!string.IsNullOrEmpty(_options.ApiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.DefaultTimeoutSeconds));
    }

    public async Task<(bool Ok, string? Version)> PingAsync(CancellationToken ct)
    {
        try
        {
            using var _ = WeaviateTelemetry.Activity.StartActivity("weaviate.ping");
            var path = "/.well-known/ready";
            var url = new Uri(_http.BaseAddress!, path).AbsoluteUri;
            _logger?.LogDebug("Weaviate ping: GET {Url}", url);
            var resp = await _http.GetAsync(path, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var fallback = "/v1/.well-known/ready";
                var fallbackUrl = new Uri(_http.BaseAddress!, fallback).AbsoluteUri;
                _logger?.LogDebug("Weaviate ping: fallback GET {Url} (prior {Status})", fallbackUrl, (int)resp.StatusCode);
                resp = await _http.GetAsync(fallback, ct);
            }
            if (resp.IsSuccessStatusCode)
            {
                // Optional version header
                resp.Headers.TryGetValues("weaviate-version", out var values);
                _logger?.LogDebug("Weaviate ping: ready ({Status})", (int)resp.StatusCode);
                return (true, values?.FirstOrDefault());
            }
            _logger?.LogDebug("Weaviate ping: not ready ({Status})", (int)resp.StatusCode);
            return (false, null);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Weaviate ping failed");
            return (false, null);
        }
    }
}
