using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Koan.Core.AI;
using Koan.ZenGarden.Core;
using Microsoft.Extensions.Logging;

namespace Koan.ZenGarden.AI;

/// <summary>
/// Bridges Zen Garden orchestrator recommendations into Koan.AI's model resolution pipeline.
/// Fetches <c>/v1/recommendations</c> from the orchestrator proxy and caches rank-1 models
/// per capability. When registered, <c>Client.Chat()</c>, <c>Client.Embed()</c>, etc.
/// automatically use the best available model with zero application configuration.
/// </summary>
internal sealed class ZenGardenModelAdvisor : IAiModelAdvisor, IDisposable
{
    private readonly IZenGardenInitializationProvider? _initProvider;
    private readonly ZenGardenOptions _options;
    private readonly ILogger<ZenGardenModelAdvisor> _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    private volatile RecommendationSnapshot? _snapshot;
    private volatile Uri? _resolvedProxyUri;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // Orchestrator wire-format capability names (returned by /v1/recommendations)
    private const string WireChat = "chat";
    private const string WireEmbedding = "embedding";
    private const string WireOcr = "ocr";
    private const string WireVision = "vision";
    private const string WireQuick = "quick";
    private const string WireSynthesis = "synthesis";
    private const string WireThinking = "thinking";
    private const string WireTools = "tools";

    // Orchestrator capability → Koan AI category mapping
    private static readonly Dictionary<string, string> CapabilityToCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        [WireChat]      = AiCapability.Chat,
        [WireEmbedding] = AiCapability.Embed,
        [WireOcr]       = AiCapability.Ocr,
        [WireVision]    = AiCapability.Vision,
        [WireQuick]     = AiCapability.Quick,
        [WireSynthesis] = AiCapability.Synthesis,
        [WireThinking]  = AiCapability.Thinking,
        [WireTools]     = AiCapability.Tools,
    };

    // Reverse mapping for category → best capability to query
    private static readonly Dictionary<string, string> CategoryToCapability = new(StringComparer.OrdinalIgnoreCase)
    {
        [AiCapability.Chat]      = WireChat,
        [AiCapability.Embed]     = WireEmbedding,
        [AiCapability.Ocr]       = WireOcr,
        [AiCapability.Vision]    = WireVision,
        [AiCapability.Quick]     = WireQuick,
        [AiCapability.Synthesis] = WireSynthesis,
        [AiCapability.Thinking]  = WireThinking,
        [AiCapability.Tools]     = WireTools,
    };

    public ZenGardenModelAdvisor(
        IZenGardenInitializationProvider? initProvider,
        ZenGardenOptions options,
        ILogger<ZenGardenModelAdvisor> logger,
        HttpClient? httpClient = null)
    {
        _initProvider = initProvider;
        _options = options;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _ownsHttpClient = httpClient is null;
    }

    /// <inheritdoc />
    public string? GetRecommendedModel(string category)
    {
        var snapshot = _snapshot;

        // If cache expired or empty, trigger async refresh (non-blocking for callers)
        if (snapshot is null || snapshot.IsExpired(_options.RecommendationCacheTtlSeconds))
        {
            _ = RefreshInBackground();
        }

        // Return from cache if available (even if expired — stale is better than nothing)
        if (snapshot is not null && CategoryToCapability.TryGetValue(category, out var capability))
        {
            return snapshot.GetTopModel(capability);
        }

        return null;
    }

    private async Task RefreshInBackground()
    {
        if (!_refreshLock.Wait(0)) return; // Another refresh is already in progress

        try
        {
            var proxyUri = await ResolveProxyEndpoint();
            if (proxyUri is null)
            {
                _logger.LogDebug("Cannot resolve orchestrator proxy endpoint — skipping recommendation refresh");
                return;
            }

            var response = await _httpClient.GetAsync(
                new Uri(proxyUri, "/v1/recommendations"),
                HttpCompletionOption.ResponseContentRead);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Orchestrator proxy returned {StatusCode} for /v1/recommendations",
                    response.StatusCode);
                return;
            }

            var groups = await response.Content.ReadFromJsonAsync<List<RecommendationGroup>>(
                RecommendationSerializerOptions.Default);

            if (groups is null or { Count: 0 })
            {
                _logger.LogDebug("Orchestrator returned empty recommendations");
                return;
            }

            var modelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                if (string.IsNullOrEmpty(group.Capability) || group.Recommendations is not { Count: > 0 })
                    continue;

                // Rank 1 = best recommendation (pre-sorted by orchestrator)
                var top = group.Recommendations
                    .OrderBy(r => r.Rank)
                    .First();

                modelMap[group.Capability] = top.Model;
            }

            _snapshot = new RecommendationSnapshot(modelMap, DateTimeOffset.UtcNow);
            _logger.LogInformation(
                "Model recommendations refreshed: {Models}",
                string.Join(", ", modelMap.Select(kv => $"{kv.Key}={kv.Value}")));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not reach orchestrator proxy /v1/recommendations — will retry on next access");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error refreshing model recommendations");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<Uri?> ResolveProxyEndpoint()
    {
        // 1. Explicit endpoint from options (highest priority)
        if (!string.IsNullOrWhiteSpace(_options.OrchestratorProxyEndpoint))
        {
            if (Uri.TryCreate(_options.OrchestratorProxyEndpoint.TrimEnd('/'), UriKind.Absolute, out var explicit_))
                return explicit_;
        }

        // 2. Cached resolution from a previous successful discovery
        var cached = _resolvedProxyUri;
        if (cached is not null) return cached;

        // 3. Resolve via ZenGarden offering catalog (ollama::orchestrator)
        if (_initProvider is not null)
        {
            try
            {
                var intent = ZenGardenConnectionIntent.ForOffering("ollama", "orchestrator");
                var resolved = await _initProvider.Resolve(intent).ConfigureAwait(false);
                var endpoint = resolved?.GetUri("http", "https");

                if (!string.IsNullOrWhiteSpace(endpoint) &&
                    Uri.TryCreate(endpoint.TrimEnd('/'), UriKind.Absolute, out var uri))
                {
                    _resolvedProxyUri = uri;
                    _logger.LogDebug(
                        "Resolved orchestrator proxy via ZenGarden offering: {Endpoint}", uri);
                    return uri;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ZenGarden offering resolution for orchestrator failed");
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
        _refreshLock.Dispose();
    }

    // ── Cached snapshot ──────────────────────────────────────────────

    private sealed class RecommendationSnapshot
    {
        private readonly Dictionary<string, string> _topModels;
        public DateTimeOffset FetchedAt { get; }

        public RecommendationSnapshot(Dictionary<string, string> topModels, DateTimeOffset fetchedAt)
        {
            _topModels = topModels;
            FetchedAt = fetchedAt;
        }

        public bool IsExpired(int ttlSeconds) =>
            DateTimeOffset.UtcNow - FetchedAt > TimeSpan.FromSeconds(ttlSeconds);

        public string? GetTopModel(string capability) =>
            _topModels.TryGetValue(capability, out var model) ? model : null;
    }
}

// ── DTO shapes for /v1/recommendations ──────────────────────────────

internal sealed class RecommendationGroup
{
    [JsonPropertyName("capability")]
    public string Capability { get; set; } = "";

    [JsonPropertyName("recommendations")]
    public List<ModelRecommendation> Recommendations { get; set; } = [];
}

internal sealed class ModelRecommendation
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("parameter_size")]
    public string? ParameterSize { get; set; }

    [JsonPropertyName("quantization_level")]
    public string? QuantizationLevel { get; set; }

    [JsonPropertyName("context_length")]
    public long ContextLength { get; set; }

    [JsonPropertyName("verdict")]
    public string? Verdict { get; set; }

    [JsonPropertyName("reasoning")]
    public List<string> Reasoning { get; set; } = [];
}

internal static class RecommendationSerializerOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
