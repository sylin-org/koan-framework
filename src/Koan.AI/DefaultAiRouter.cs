using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;

namespace Koan.AI;

/// <summary>
/// ADR-0015 compliant AI router.
/// Implements priority-based source election and policy-driven member selection.
/// </summary>
internal sealed class DefaultAiRouter : IAiRouter
{
    private readonly IAiAdapterRegistry _adapterRegistry;
    private readonly IAiSourceRegistry _sourceRegistry;
    private readonly ILogger<DefaultAiRouter>? _logger;
    private readonly IOptionsMonitor<AiOptions> _options;

    public DefaultAiRouter(
        IAiAdapterRegistry adapterRegistry,
        IAiSourceRegistry sourceRegistry,
        IOptionsMonitor<AiOptions> options,
        ILogger<DefaultAiRouter>? logger = null)
    {
        _adapterRegistry = adapterRegistry;
        _sourceRegistry = sourceRegistry;
        _options = options;
        _logger = logger;
    }

    public async Task<AiChatResponse> PromptAsync(AiChatRequest request, CancellationToken ct = default)
    {
        var (source, member, adapter) = ResolveRouting(request.Route?.AdapterId, "Chat", request.Model);

        // Inject member URL into request for adapter
        request.InternalConnectionString = member.ConnectionString;

        // Get effective model (request model or member's capability model)
        var effectiveModel = request.Model;
        if (string.IsNullOrWhiteSpace(effectiveModel) && member.Capabilities?.TryGetValue("Chat", out var chatCap) == true)
        {
            effectiveModel = chatCap.Model;
        }

        try
        {
            var response = await adapter.ChatAsync(request, ct);

            _logger?.LogInformation(
                "AI route OK: {Adapter}/{Model} via {Source}:{Member}",
                adapter.Id, effectiveModel ?? "(default)", source.Name, member.Name);

            return response with { AdapterId = adapter.Id };
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                "AI route FAIL: {Adapter}/{Model} via {Source}:{Member} - {Error}",
                adapter.Id, effectiveModel ?? "(default)", source.Name, member.Name, ex.Message);
            throw;
        }
    }

    public async IAsyncEnumerable<AiChatChunk> StreamAsync(
        AiChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var (source, member, adapter) = ResolveRouting(request.Route?.AdapterId, "Chat", request.Model);

        request.InternalConnectionString = member.ConnectionString;

        // Get effective model (request model or member's capability model)
        var effectiveModel = request.Model;
        if (string.IsNullOrWhiteSpace(effectiveModel) && member.Capabilities?.TryGetValue("Chat", out var chatCap) == true)
        {
            effectiveModel = chatCap.Model;
        }

        var chunkCount = 0;
        Exception? streamError = null;

        await foreach (var chunk in adapter.StreamAsync(request, ct))
        {
            chunkCount++;
            yield return chunk with { AdapterId = adapter.Id };
        }

        if (streamError == null)
        {
            _logger?.LogInformation(
                "AI route OK: {Adapter}/{Model} via {Source}:{Member} (stream {Chunks} chunks)",
                adapter.Id, effectiveModel ?? "(default)", source.Name, member.Name, chunkCount);
        }
    }

    public async Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default)
    {
        var (source, member, adapter) = ResolveRouting(null, "Embedding", request.Model);

        request.InternalConnectionString = member.ConnectionString;

        // Get effective model (request model or member's capability model)
        var effectiveModel = request.Model;
        if (string.IsNullOrWhiteSpace(effectiveModel) && member.Capabilities?.TryGetValue("Embedding", out var embCap) == true)
        {
            effectiveModel = embCap.Model;
        }

        try
        {
            var response = await adapter.EmbedAsync(request, ct);

            _logger?.LogInformation(
                "AI route OK: {Adapter}/{Model} via {Source}:{Member} (embed {Count} inputs)",
                adapter.Id, effectiveModel ?? "(default)", source.Name, member.Name, request.Input?.Count ?? 0);

            return response;
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                "AI route FAIL: {Adapter}/{Model} via {Source}:{Member} - {Error}",
                adapter.Id, effectiveModel ?? "(default)", source.Name, member.Name, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Core routing resolution: source election → member selection → adapter lookup.
    /// ADR-0015: Simple, fail-fast semantics.
    /// </summary>
    private (AiSourceDefinition Source, AiMemberDefinition Member, IAiAdapter Adapter) ResolveRouting(
        string? sourceHint,
        string capabilityName,
        string? explicitModel)
    {
        // 1. Resolve source
        var source = ResolveSource(sourceHint, capabilityName, explicitModel);

        // 2. Select member (policy or pinned)
        var member = SelectMember(source, sourceHint);

        // 3. Get adapter
        var adapter = ResolveAdapter(source);

        return (source, member, adapter);
    }

    /// <summary>
    /// Resolve source: explicit hint, member reference, or priority-based election.
    /// </summary>
    private AiSourceDefinition ResolveSource(string? sourceHint, string capabilityName, string? explicitModel)
    {
        // If explicit model provided, skip source election (legacy behavior)
        if (!string.IsNullOrWhiteSpace(explicitModel))
        {
            // Try to find any source with this capability
            var anySources = _sourceRegistry.GetSourcesWithCapability(capabilityName);
            if (anySources.Count > 0)
                return anySources.First();

            // Fallback to any source
            var allSources = _sourceRegistry.GetAllSources();
            if (allSources.Count > 0)
                return allSources.First();

            throw new InvalidOperationException(
                $"No AI sources available. Add an adapter (e.g., Ollama) or enable auto-discovery.");
        }

        // Check for explicit source or member hint
        if (!string.IsNullOrWhiteSpace(sourceHint))
        {
            // Try direct source lookup
            var source = _sourceRegistry.GetSource(sourceHint);
            if (source != null)
                return source;

            // Check for member reference (contains ::)
            if (sourceHint.Contains("::"))
            {
                var sourceName = sourceHint.Split("::")[0];
                source = _sourceRegistry.GetSource(sourceName);
                if (source != null)
                    return source;

                // Fail fast: member reference but source not found
                throw new InvalidOperationException(
                    $"Source '{sourceName}' not found for member reference '{sourceHint}'. " +
                    $"Available sources: {string.Join(", ", _sourceRegistry.GetSourceNames())}");
            }

            // Fail fast: source hint provided but not found
            throw new InvalidOperationException(
                $"Source '{sourceHint}' not found. " +
                $"Available sources: {string.Join(", ", _sourceRegistry.GetSourceNames())}");
        }

        // No hint: elect by priority
        var candidates = _sourceRegistry.GetSourcesWithCapability(capabilityName);
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"No source found with capability '{capabilityName}'. " +
                "Configure a source or enable auto-discovery.");
        }

        return candidates.First(); // Already sorted by priority descending
    }

    /// <summary>
    /// Select member from source: pinned (if :: in hint) or policy-based.
    /// </summary>
    private AiMemberDefinition SelectMember(AiSourceDefinition source, string? sourceHint)
    {
        // Check for member pinning (sourceHint contains ::)
        if (!string.IsNullOrWhiteSpace(sourceHint) && sourceHint.Contains("::"))
        {
            var pinnedMember = source.Members.FirstOrDefault(m =>
                string.Equals(m.Name, sourceHint, StringComparison.OrdinalIgnoreCase));

            if (pinnedMember == null)
            {
                // Fail fast: member specified but not found
                throw new InvalidOperationException(
                    $"Member '{sourceHint}' not found in source '{source.Name}'. " +
                    $"Available members: {string.Join(", ", source.Members.Select(m => m.Name))}");
            }

            return pinnedMember;
        }

        // Policy-based member selection
        if (source.Members.Count == 0)
        {
            throw new InvalidOperationException(
                $"Source '{source.Name}' has no members. Check configuration or discovery.");
        }

        // Simple policy: use first healthy member (Fallback policy)
        // TODO: Implement RoundRobin, WeightedRoundRobin in Phase 4
        return source.Members
            .OrderBy(m => m.Order)
            .FirstOrDefault(m => m.HealthState != MemberHealthState.Unhealthy)
            ?? source.Members.OrderBy(m => m.Order).First();
    }

    /// <summary>
    /// Resolve adapter from source provider.
    /// </summary>
    private IAiAdapter ResolveAdapter(AiSourceDefinition source)
    {
        if (string.IsNullOrWhiteSpace(source.Provider))
        {
            throw new InvalidOperationException(
                $"Source '{source.Name}' has no provider configured");
        }

        var adapter = _adapterRegistry.Get(source.Provider);
        if (adapter == null)
        {
            throw new InvalidOperationException(
                $"No adapter found for provider '{source.Provider}'. " +
                $"Available adapters: {string.Join(", ", _adapterRegistry.All.Select(a => a.Id))}");
        }

        return adapter;
    }
}
