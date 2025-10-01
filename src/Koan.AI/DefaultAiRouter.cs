using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Options;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;

namespace Koan.AI;

internal sealed class DefaultAiRouter : IAiRouter
{
    private readonly IAiAdapterRegistry _registry;
    private readonly ILogger<DefaultAiRouter>? _logger;
    private readonly IOptionsMonitor<AiOptions> _options;
    private readonly IAiSourceRegistry? _sourceRegistry;
    private readonly IAiGroupRegistry? _groupRegistry;
    private readonly ISourceHealthRegistry? _healthRegistry;
    private readonly ConcurrentDictionary<int, long> _priorityCounters = new();
    private readonly ConcurrentDictionary<string, byte> _policyWarnings = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IAiAdapter> _groupAdapters = new(StringComparer.OrdinalIgnoreCase);
    private int _rr;

    public DefaultAiRouter(
        IAiAdapterRegistry registry,
        IOptionsMonitor<AiOptions> options,
        ILogger<DefaultAiRouter>? logger = null,
        IAiSourceRegistry? sourceRegistry = null,
        IAiGroupRegistry? groupRegistry = null,
        ISourceHealthRegistry? healthRegistry = null)
    {
        _registry = registry;
        _options = options;
        _logger = logger;
        _sourceRegistry = sourceRegistry;
        _groupRegistry = groupRegistry;
        _healthRegistry = healthRegistry;
    }

    public async Task<Contracts.Models.AiChatResponse> PromptAsync(Contracts.Models.AiChatRequest request, CancellationToken ct = default)
    {
        // ADR-0014: Capability-aware routing
        var (adapter, resolvedModel) = ResolveAdapterForCapability("Chat", request.Model, request.Route?.AdapterId);

        if (adapter == null)
        {
            // Fallback to legacy routing
            adapter = PickAdapter(request) ?? throw new InvalidOperationException("No AI providers available. Add an adapter (e.g., Ollama) or enable Dev auto-discovery.");
        }

        // Override model if capability mapping resolved a specific one
        var finalRequest = resolvedModel != null && string.IsNullOrWhiteSpace(request.Model)
            ? request with { Model = resolvedModel }
            : request;

        var res = await adapter.ChatAsync(finalRequest, ct).ConfigureAwait(false);
        return res with { AdapterId = adapter.Id };
    }

    public async IAsyncEnumerable<Contracts.Models.AiChatChunk> StreamAsync(Contracts.Models.AiChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // ADR-0014: Capability-aware routing
        var (adapter, resolvedModel) = ResolveAdapterForCapability("Chat", request.Model, request.Route?.AdapterId);

        if (adapter == null)
        {
            // Fallback to legacy routing
            adapter = PickAdapter(request) ?? throw new InvalidOperationException("No AI providers available. Add an adapter (e.g., Ollama) or enable Dev auto-discovery.");
        }

        // Override model if capability mapping resolved a specific one
        var finalRequest = resolvedModel != null && string.IsNullOrWhiteSpace(request.Model)
            ? request with { Model = resolvedModel }
            : request;

        await foreach (var ch in adapter.StreamAsync(finalRequest, ct).ConfigureAwait(false))
            yield return ch with { AdapterId = adapter.Id };
    }

    public async Task<Contracts.Models.AiEmbeddingsResponse> EmbedAsync(Contracts.Models.AiEmbeddingsRequest request, CancellationToken ct = default)
    {
        // ADR-0014: Capability-aware routing for embeddings
        var (adapter, resolvedModel) = ResolveAdapterForCapability("Embedding", request.Model, null);

        if (adapter == null)
        {
            // Fallback to legacy routing
            adapter = await PickAdapterForEmbeddingsAsync(request, ct);
            if (adapter is null)
                throw new InvalidOperationException("No AI providers available. Add an adapter (e.g., Ollama) or enable Dev auto-discovery.");
        }

        // Override model if capability mapping resolved a specific one
        var finalRequest = resolvedModel != null && string.IsNullOrWhiteSpace(request.Model)
            ? request with { Model = resolvedModel }
            : request;

        var response = await adapter.EmbedAsync(finalRequest, ct).ConfigureAwait(false);

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

    /// <summary>
    /// ADR-0014: Resolve adapter and model for a capability using source registry.
    /// Priority: sourceNameHint (can be source or group) → Default source → null (fallback to legacy)
    /// </summary>
    private (Contracts.Adapters.IAiAdapter? Adapter, string? Model) ResolveAdapterForCapability(
        string capabilityName,
        string? explicitModel,
        string? sourceNameHint)
    {
        // If no source registry available, return null to trigger legacy routing
        if (_sourceRegistry == null)
        {
            return (null, null);
        }

        // If explicit model provided, don't override
        if (!string.IsNullOrWhiteSpace(explicitModel))
        {
            return (null, null);
        }

        // 1. Check if sourceNameHint is a group - if so, use ResilientAiAdapter
        if (!string.IsNullOrWhiteSpace(sourceNameHint) && _groupRegistry != null && _healthRegistry != null)
        {
            if (_groupRegistry.HasGroup(sourceNameHint))
            {
                _logger?.LogDebug("AI Router: Source hint '{SourceHint}' is a group - using resilient adapter",
                    sourceNameHint);

                var groupAdapter = GetOrCreateGroupAdapter(sourceNameHint);
                if (groupAdapter != null)
                {
                    // Get model from first source in group with this capability
                    var groupSources = _sourceRegistry.GetSourcesInGroup(sourceNameHint);
                    var firstSourceWithCapability = groupSources
                        .FirstOrDefault(s => s.Capabilities.ContainsKey(capabilityName));

                    var model = firstSourceWithCapability?.Capabilities.TryGetValue(capabilityName, out var cap) == true
                        ? cap.Model
                        : null;

                    return (groupAdapter, model);
                }
            }
        }

        // 2. Try to resolve as single source
        AiSourceDefinition? source = null;

        // Check explicit source hint from route
        if (!string.IsNullOrWhiteSpace(sourceNameHint))
        {
            source = _sourceRegistry.GetSource(sourceNameHint);
            if (source == null)
            {
                _logger?.LogWarning("AI Router: Requested source '{SourceName}' not found, falling back", sourceNameHint);
            }
        }

        // 3. Fallback to "Default" source
        if (source == null)
        {
            source = _sourceRegistry.GetSource("Default");
        }

        // 4. If still no source, check if there's a group hint and use first source in group
        if (source == null && !string.IsNullOrWhiteSpace(sourceNameHint))
        {
            var groupSources = _sourceRegistry.GetSourcesInGroup(sourceNameHint);
            source = groupSources.FirstOrDefault();
            if (source != null)
            {
                _logger?.LogDebug("AI Router: Using first source '{SourceName}' from group '{GroupName}'",
                    source.Name, sourceNameHint);
            }
        }

        if (source == null)
        {
            // No source configuration available
            return (null, null);
        }

        // Get capability configuration from source
        if (!source.Capabilities.TryGetValue(capabilityName, out var capabilityConfig))
        {
            _logger?.LogDebug("AI Router: Source '{SourceName}' has no configuration for capability '{Capability}'",
                source.Name, capabilityName);
            return (null, null);
        }

        // Find adapter matching this source's connection string
        var adapter = FindAdapterForSource(source);
        if (adapter == null)
        {
            _logger?.LogWarning("AI Router: No adapter found for source '{SourceName}' ({ConnectionString})",
                source.Name, source.ConnectionString);
            return (null, null);
        }

        _logger?.LogDebug("AI Router: Resolved capability '{Capability}' → source '{SourceName}' → model '{Model}' → adapter '{AdapterId}'",
            capabilityName, source.Name, capabilityConfig.Model, adapter.Id);

        return (adapter, capabilityConfig.Model);
    }

    /// <summary>
    /// Get or create a resilient adapter for a group with fallback/circuit breaker behavior
    /// </summary>
    private IAiAdapter? GetOrCreateGroupAdapter(string groupName)
    {
        if (_groupAdapters.TryGetValue(groupName, out var existing))
        {
            return existing;
        }

        if (_sourceRegistry == null || _groupRegistry == null || _healthRegistry == null)
        {
            return null;
        }

        var group = _groupRegistry.GetGroup(groupName);
        if (group == null)
        {
            return null;
        }

        var policy = Sources.Policies.GroupPolicyFactory.CreatePolicy(group.Policy, _registry);
        var resilientAdapter = new Sources.ResilientAiAdapter(
            groupName,
            _sourceRegistry,
            _healthRegistry,
            policy,
            _logger as ILogger<Sources.ResilientAiAdapter>);

        _groupAdapters[groupName] = resilientAdapter;

        _logger?.LogInformation(
            "Created resilient adapter for group '{GroupName}' with policy '{Policy}'",
            groupName,
            group.Policy);

        return resilientAdapter;
    }

    /// <summary>
    /// Find adapter instance that matches a source's connection string.
    /// Matches by URL pattern in adapter ID (e.g., "ollama@host.docker.internal:11434")
    /// </summary>
    private Contracts.Adapters.IAiAdapter? FindAdapterForSource(AiSourceDefinition source)
    {
        if (string.IsNullOrWhiteSpace(source.ConnectionString))
        {
            // No connection string - use first adapter as fallback
            return _registry.All.FirstOrDefault();
        }

        // Parse source URL to extract host:port
        var sourceUri = TryParseUri(source.ConnectionString);
        if (sourceUri == null)
        {
            return null;
        }

        var sourceHost = sourceUri.Host;
        var sourcePort = sourceUri.Port;

        // Find adapter whose ID contains matching host:port
        foreach (var adapter in _registry.All)
        {
            // Adapter IDs are like "ollama@host.docker.internal:11434"
            if (adapter.Id.Contains($"{sourceHost}:{sourcePort}", StringComparison.OrdinalIgnoreCase))
            {
                return adapter;
            }

            // Also try matching just the host for standard ports
            if (adapter.Id.Contains(sourceHost, StringComparison.OrdinalIgnoreCase))
            {
                return adapter;
            }
        }

        return null;
    }

    private static Uri? TryParseUri(string? uriString)
    {
        if (string.IsNullOrWhiteSpace(uriString))
        {
            return null;
        }

        try
        {
            return new Uri(uriString);
        }
        catch
        {
            return null;
        }
    }

    private enum PolicyMode
    {
        WeightedPriority,
        Priority,
        RoundRobin
    }
}