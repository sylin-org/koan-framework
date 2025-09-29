using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.AI.Contracts.Options;
using Koan.AI.Contracts.Routing;

namespace Koan.AI;

internal sealed class DefaultAiRouter : IAiRouter
{
    private readonly IAiAdapterRegistry _registry;
    private readonly ILogger<DefaultAiRouter>? _logger;
    private readonly IOptionsMonitor<AiOptions> _options;
    private readonly ConcurrentDictionary<int, long> _priorityCounters = new();
    private readonly ConcurrentDictionary<string, byte> _policyWarnings = new(StringComparer.OrdinalIgnoreCase);
    private int _rr;

    public DefaultAiRouter(
        IAiAdapterRegistry registry,
        IOptionsMonitor<AiOptions> options,
        ILogger<DefaultAiRouter>? logger = null)
    {
        _registry = registry;
        _options = options;
        _logger = logger;
    }

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
        var routeId = request.Route?.AdapterId;
        if (!string.IsNullOrWhiteSpace(routeId))
        {
            var picked = _registry.Get(routeId!);
            _logger?.LogDebug("AI Router: Route requested adapter {AdapterId} -> {Picked}", routeId, picked?.Id ?? "<none>");
            return picked;
        }

        var registrations = _registry.Registrations;
        if (registrations.Count == 0)
        {
            return null;
        }

        var policy = ResolvePolicy(request.Route?.Policy);
        Contracts.Adapters.IAiAdapter? candidate = policy switch
        {
            PolicyMode.RoundRobin => PickRoundRobin(registrations, adapter => adapter.CanServe(request)),
            PolicyMode.Priority => PickByPriority(registrations, adapter => adapter.CanServe(request), weighted: false),
            _ => PickByPriority(registrations, adapter => adapter.CanServe(request), weighted: true)
        };

        if (candidate is null && registrations.Count > 0)
        {
            candidate = registrations[0].Adapter;
            _logger?.LogDebug("AI Router: fallback to first adapter {AdapterId}", candidate.Id);
        }

        return candidate;
    }

    private async Task<Contracts.Adapters.IAiAdapter?> PickAdapterForEmbeddingsAsync(Contracts.Models.AiEmbeddingsRequest request, CancellationToken ct)
    {
        var registrations = _registry.Registrations;
        if (registrations.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            foreach (var registration in registrations.OrderByDescending(r => r.Priority))
            {
                try
                {
                    var models = await registration.Adapter.ListModelsAsync(ct).ConfigureAwait(false);
                    var hasModel = models.Any(m =>
                        string.Equals(m.Name, request.Model, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(m.Name.Split(':')[0], request.Model, StringComparison.OrdinalIgnoreCase));
                    if (hasModel)
                    {
                        return registration.Adapter;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error checking models for adapter {AdapterId}: {Error}", registration.Adapter.Id, ex.Message);
                }
            }

            var fallback = registrations.FirstOrDefault()?.Adapter;
            if (fallback is not null)
            {
                _logger?.LogWarning("No adapter found for model '{Model}', using fallback {FallbackId}", request.Model, fallback.Id);
            }
            return fallback;
        }

        var policy = ResolvePolicy(null);
        return policy switch
        {
            PolicyMode.RoundRobin => PickRoundRobin(registrations, predicate: null),
            PolicyMode.Priority => PickByPriority(registrations, predicate: null, weighted: false),
            _ => PickByPriority(registrations, predicate: null, weighted: true)
        };
    }

    private PolicyMode ResolvePolicy(string? overridePolicy)
    {
        var source = !string.IsNullOrWhiteSpace(overridePolicy)
            ? overridePolicy
            : _options.CurrentValue?.DefaultPolicy;

        if (string.IsNullOrWhiteSpace(source))
        {
            return PolicyMode.WeightedPriority;
        }

        var tokens = source.Split(new[] { '+', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hasWrw = false;
        var hasPriority = false;
        var hasRoundRobin = false;

        foreach (var token in tokens)
        {
            if (token.Equals("wrw", StringComparison.OrdinalIgnoreCase) || token.Equals("weighted-priority", StringComparison.OrdinalIgnoreCase))
            {
                hasWrw = true;
            }
            else if (token.Equals("priority", StringComparison.OrdinalIgnoreCase))
            {
                hasPriority = true;
            }
            else if (token.Equals("round-robin", StringComparison.OrdinalIgnoreCase) || token.Equals("rr", StringComparison.OrdinalIgnoreCase))
            {
                hasRoundRobin = true;
            }
            else if (token.Equals("health", StringComparison.OrdinalIgnoreCase) || token.Equals("least-pending", StringComparison.OrdinalIgnoreCase))
            {
                WarnPolicyToken(token);
            }
            else
            {
                WarnPolicyToken(token);
            }
        }

        if (hasRoundRobin && !hasWrw && !hasPriority)
        {
            return PolicyMode.RoundRobin;
        }

        if (hasWrw)
        {
            return PolicyMode.WeightedPriority;
        }

        if (hasPriority)
        {
            return PolicyMode.Priority;
        }

        return hasRoundRobin ? PolicyMode.RoundRobin : PolicyMode.WeightedPriority;
    }

    private void WarnPolicyToken(string token)
    {
        if (_policyWarnings.TryAdd(token, 0))
        {
            _logger?.LogInformation("AI Router: policy token '{Token}' not yet supported; ignoring.", token);
        }
    }

    private Contracts.Adapters.IAiAdapter? PickRoundRobin(IReadOnlyList<AiAdapterRegistration> registrations, Func<Contracts.Adapters.IAiAdapter, bool>? predicate)
    {
        if (registrations.Count == 0)
        {
            return null;
        }

        var start = (int)((uint)Interlocked.Increment(ref _rr));
        for (var i = 0; i < registrations.Count; i++)
        {
            var idx = (start + i) % registrations.Count;
            var candidate = registrations[idx].Adapter;
            if (predicate is null || predicate(candidate))
            {
                return candidate;
            }
        }

        return registrations[start % registrations.Count].Adapter;
    }

    private Contracts.Adapters.IAiAdapter? PickByPriority(
        IReadOnlyList<AiAdapterRegistration> registrations,
        Func<Contracts.Adapters.IAiAdapter, bool>? predicate,
        bool weighted)
    {
        if (registrations.Count == 0)
        {
            return null;
        }

        var grouped = registrations.GroupBy(r => r.Priority).OrderByDescending(g => g.Key);
        foreach (var group in grouped)
        {
            var list = weighted ? ExpandWeighted(group.ToList()) : group.ToList();
            if (list.Count == 0)
            {
                continue;
            }

            var start = GetNextForPriority(group.Key, list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                var idx = (start + i) % list.Count;
                var candidate = list[idx].Adapter;
                if (predicate is null || predicate(candidate))
                {
                    return candidate;
                }
            }
        }

        return registrations[0].Adapter;
    }

    private List<AiAdapterRegistration> ExpandWeighted(IReadOnlyList<AiAdapterRegistration> source)
    {
        var result = new List<AiAdapterRegistration>();
        foreach (var item in source)
        {
            var weight = Math.Max(1, item.Weight);
            for (var i = 0; i < weight; i++)
            {
                result.Add(item);
            }
        }

        return result;
    }

    private int GetNextForPriority(int priority, int count)
    {
        if (count <= 1)
        {
            return 0;
        }

        var next = _priorityCounters.AddOrUpdate(priority, 1, static (_, current) => current + 1);
        return (int)((next - 1) % count);
    }

    private enum PolicyMode
    {
        WeightedPriority,
        Priority,
        RoundRobin
    }
}