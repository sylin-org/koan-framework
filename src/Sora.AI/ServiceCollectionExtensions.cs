using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.AI.Contracts;
using Sora.AI.Contracts.Options;
using Sora.AI.Contracts.Routing;

namespace Sora.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration? config = null)
    {
        services.AddOptions<AiOptions>();
        if (config is not null)
            services.Configure<AiOptions>(config.GetSection("Sora:Ai"));

        services.TryAddSingleton<IAiAdapterRegistry, InMemoryAdapterRegistry>();
        services.TryAddSingleton<IAiRouter, DefaultAiRouter>();
        services.TryAddSingleton<IAi, RouterAi>();
        return services;
    }
}

internal sealed class RouterAi : IAi
{
    private readonly IAiRouter _router;
    public RouterAi(IAiRouter router) => _router = router;
    public Task<Sora.AI.Contracts.Models.AiChatResponse> PromptAsync(Sora.AI.Contracts.Models.AiChatRequest request, CancellationToken ct = default)
        => _router.PromptAsync(request, ct);

    public IAsyncEnumerable<Sora.AI.Contracts.Models.AiChatChunk> StreamAsync(Sora.AI.Contracts.Models.AiChatRequest request, CancellationToken ct = default)
        => _router.StreamAsync(request, ct);

    public Task<Sora.AI.Contracts.Models.AiEmbeddingsResponse> EmbedAsync(Sora.AI.Contracts.Models.AiEmbeddingsRequest request, CancellationToken ct = default)
        => _router.EmbedAsync(request, ct);

    public Task<string> PromptAsync(string message, string? model = null, Sora.AI.Contracts.Models.AiPromptOptions? opts = null, CancellationToken ct = default)
        => _router.PromptAsync(new Sora.AI.Contracts.Models.AiChatRequest { Messages = new() { new Sora.AI.Contracts.Models.AiMessage("user", message) }, Model = model, Options = opts }, ct)
            .ContinueWith(t => t.Result.Text, ct);

    public IAsyncEnumerable<Sora.AI.Contracts.Models.AiChatChunk> StreamAsync(string message, string? model = null, Sora.AI.Contracts.Models.AiPromptOptions? opts = null, CancellationToken ct = default)
        => _router.StreamAsync(new Sora.AI.Contracts.Models.AiChatRequest { Messages = new() { new Sora.AI.Contracts.Models.AiMessage("user", message) }, Model = model, Options = opts }, ct);
}

internal sealed class InMemoryAdapterRegistry : IAiAdapterRegistry
{
    private readonly object _gate = new();
    private readonly List<Sora.AI.Contracts.Adapters.IAiAdapter> _adapters = new();
    public IReadOnlyList<Sora.AI.Contracts.Adapters.IAiAdapter> All
    { get { lock (_gate) return _adapters.ToArray(); } }
    public void Add(Sora.AI.Contracts.Adapters.IAiAdapter adapter)
    { lock (_gate) { if (!_adapters.Any(a => a.Id == adapter.Id)) _adapters.Add(adapter); } }
    public bool Remove(string id)
    { lock (_gate) { return _adapters.RemoveAll(a => a.Id == id) > 0; } }
    public Sora.AI.Contracts.Adapters.IAiAdapter? Get(string id)
    { lock (_gate) { return _adapters.FirstOrDefault(a => a.Id == id); } }
}

internal sealed class DefaultAiRouter : IAiRouter
{
    private readonly IAiAdapterRegistry _registry;
    private int _rr;
    public DefaultAiRouter(IAiAdapterRegistry registry) => _registry = registry;

    public async Task<Sora.AI.Contracts.Models.AiChatResponse> PromptAsync(Sora.AI.Contracts.Models.AiChatRequest request, CancellationToken ct = default)
    {
        var adapter = PickAdapter(request) ?? throw new InvalidOperationException("No AI providers available. Add an adapter (e.g., Ollama) or enable Dev auto-discovery.");
        var res = await adapter.ChatAsync(request, ct).ConfigureAwait(false);
        return res with { AdapterId = adapter.Id };
    }

    public async IAsyncEnumerable<Sora.AI.Contracts.Models.AiChatChunk> StreamAsync(Sora.AI.Contracts.Models.AiChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var adapter = PickAdapter(request) ?? throw new InvalidOperationException("No AI providers available. Add an adapter (e.g., Ollama) or enable Dev auto-discovery.");
        await foreach (var ch in adapter.StreamAsync(request, ct).ConfigureAwait(false))
            yield return ch with { AdapterId = adapter.Id };
    }

    public async Task<Sora.AI.Contracts.Models.AiEmbeddingsResponse> EmbedAsync(Sora.AI.Contracts.Models.AiEmbeddingsRequest request, CancellationToken ct = default)
    {
        var rr = Interlocked.Increment(ref _rr);
        var adapter = _registry.All.Skip(rr % Math.Max(1, _registry.All.Count)).FirstOrDefault();
        if (adapter is null)
            throw new InvalidOperationException("No AI providers available. Add an adapter (e.g., Ollama) or enable Dev auto-discovery.");
        return await adapter.EmbedAsync(request, ct).ConfigureAwait(false);
    }

    private Sora.AI.Contracts.Adapters.IAiAdapter? PickAdapter(Sora.AI.Contracts.Models.AiChatRequest request)
    {
        // Policy: prefer explicit AdapterId; else round-robin among CanServe()
        var routeId = request.Route?.AdapterId;
        if (!string.IsNullOrEmpty(routeId))
            return _registry.Get(routeId!);
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
        return list[(start) % list.Count];
    }
}
