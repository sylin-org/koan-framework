using Microsoft.Extensions.Logging;
using Koan.AI.Contracts.Routing;

namespace Koan.AI;

internal sealed class DefaultAiRouter : IAiRouter
{
    private readonly IAiAdapterRegistry _registry;
    private readonly ILogger<DefaultAiRouter>? _logger;
    private int _rr;
    public DefaultAiRouter(IAiAdapterRegistry registry, ILogger<DefaultAiRouter>? logger = null)
    { _registry = registry; _logger = logger; }

    public async Task<Contracts.Models.AiChatResponse> PromptAsync(Contracts.Models.AiChatRequest request, CancellationToken ct = default)
    {
        var adapter = PickAdapter(request) ?? throw new InvalidOperationException("No AI providers available. Add an adapter (e.g., Ollama) or enable Dev auto-discovery.");
        var res = await adapter.ChatAsync(request, ct).ConfigureAwait(false);
        return res with { AdapterId = adapter.Id };
    }

    public async IAsyncEnumerable<Contracts.Models.AiChatChunk> StreamAsync(Contracts.Models.AiChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var adapter = PickAdapter(request) ?? throw new InvalidOperationException("No AI providers available. Add an adapter (e.g., Ollama) or enable Dev auto-discovery.");
        await foreach (var ch in adapter.StreamAsync(request, ct).ConfigureAwait(false))
            yield return ch with { AdapterId = adapter.Id };
    }

    public async Task<Contracts.Models.AiEmbeddingsResponse> EmbedAsync(Contracts.Models.AiEmbeddingsRequest request, CancellationToken ct = default)
    {
        var adapter = await PickAdapterForEmbeddingsAsync(request, ct);
        if (adapter is null)
            throw new InvalidOperationException("No AI providers available. Add an adapter (e.g., Ollama) or enable Dev auto-discovery.");

        var response = await adapter.EmbedAsync(request, ct).ConfigureAwait(false);

        return response;
    }

    private Contracts.Adapters.IAiAdapter? PickAdapter(Contracts.Models.AiChatRequest request)
    {
        // Policy: prefer explicit AdapterId; else round-robin among CanServe()
        var routeId = request.Route?.AdapterId;
        if (!string.IsNullOrEmpty(routeId))
        {
            var picked = _registry.Get(routeId!);
            _logger?.LogDebug("AI Router: Route requested adapter {AdapterId} -> {Picked}", routeId, picked?.Id ?? "<none>");
            return picked;
        }
        var list = _registry.All;
        if (list.Count == 0) return null;
        var start = Interlocked.Increment(ref _rr);
        for (var i = 0; i < list.Count; i++)
        {
            var idx = (start + i) % list.Count;
            var a = list[idx];
            if (a.CanServe(request)) return a;
        }
        // Fallback: any adapter
        var any = list[(start) % list.Count];
        _logger?.LogDebug("AI Router: Fallback picked adapter {AdapterId}", any.Id);
        return any;
    }

    private async Task<Contracts.Adapters.IAiAdapter?> PickAdapterForEmbeddingsAsync(Contracts.Models.AiEmbeddingsRequest request, CancellationToken ct)
    {
        var list = _registry.All;
        if (list.Count == 0) return null;

        // If no specific model requested, use round-robin
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            var rr = Interlocked.Increment(ref _rr);
            var adapter = list.Skip(rr % list.Count).FirstOrDefault();
            return adapter;
        }

        // Find adapter that supports the requested model
        foreach (var adapter in list)
        {
            try
            {
                var models = await adapter.ListModelsAsync(ct);
                var hasModel = models.Any(m =>
                    string.Equals(m.Name, request.Model, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.Name.Split(':')[0], request.Model, StringComparison.OrdinalIgnoreCase));

                if (hasModel)
                {
                    return adapter;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error checking models for adapter {AdapterId}: {Error}",
                    adapter.Id, ex.Message);
                continue;
            }
        }

        // Fallback: return first available adapter
        var fallbackAdapter = list.FirstOrDefault();
        _logger?.LogWarning("No adapter found for model '{Model}', using fallback {FallbackId}",
            request.Model, fallbackAdapter?.Id ?? "<null>");
        return fallbackAdapter;
    }
}