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
        var rr = Interlocked.Increment(ref _rr);
        var list = _registry.All;
        var adapter = list.Skip(rr % Math.Max(1, list.Count)).FirstOrDefault();
        if (adapter is null)
            throw new InvalidOperationException("No AI providers available. Add an adapter (e.g., Ollama) or enable Dev auto-discovery.");
        _logger?.LogDebug("AI Router: Embeddings via adapter {AdapterId} ({AdapterType}), model={Model}", adapter.Id, adapter.Type, request.Model ?? "<default>");
        return await adapter.EmbedAsync(request, ct).ConfigureAwait(false);
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
}