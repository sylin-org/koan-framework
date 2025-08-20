using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;

namespace Sora.Data.Weaviate;

internal static class WeaviateTelemetry
{
    public static readonly ActivitySource Activity = new("Sora.Data.Weaviate");
}

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
            var resp = await _http.GetAsync("/.well-known/ready", ct);
            if (resp.IsSuccessStatusCode)
            {
                // Optional version header
                resp.Headers.TryGetValues("weaviate-version", out var values);
                return (true, values?.FirstOrDefault());
            }
            return (false, null);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Weaviate ping failed");
            return (false, null);
        }
    }
}
