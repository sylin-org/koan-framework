using System;
using System.Collections.Generic;
using System.Linq;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Microsoft.Extensions.Logging;

namespace Koan.AI.Pipeline;

/// <summary>
/// Central routing engine that elects sources, members, and adapters for AI requests.
/// </summary>
internal sealed class AiRoutingEngine
{
    private readonly IAiAdapterRegistry _adapterRegistry;
    private readonly IAiSourceRegistry _sourceRegistry;
    private readonly ILogger<AiRoutingEngine>? _logger;

    public AiRoutingEngine(
        IAiAdapterRegistry adapterRegistry,
        IAiSourceRegistry sourceRegistry,
        ILogger<AiRoutingEngine>? logger = null)
    {
        _adapterRegistry = adapterRegistry;
        _sourceRegistry = sourceRegistry;
        _logger = logger;
    }

    public AiRouteResolution ResolveChat(AiChatRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var (source, member, adapter) = ResolveRouting(request.Route?.AdapterId, "Chat", request.Model);
        var effectiveModel = DetermineEffectiveModel(source, member, "Chat", request.Model);

        return new AiRouteResolution(source, member, adapter, "Chat", effectiveModel);
    }

    public AiRouteResolution ResolveEmbeddings(AiEmbeddingsRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var (source, member, adapter) = ResolveRouting(null, "Embedding", request.Model);
        var effectiveModel = DetermineEffectiveModel(source, member, "Embedding", request.Model);

        return new AiRouteResolution(source, member, adapter, "Embedding", effectiveModel);
    }

    private (AiSourceDefinition Source, AiMemberDefinition Member, IAiAdapter Adapter) ResolveRouting(
        string? sourceHint,
        string capabilityName,
        string? explicitModel)
    {
        var source = ResolveSource(sourceHint, capabilityName, explicitModel);
        var member = SelectMember(source, sourceHint);
        var adapter = ResolveAdapter(source);
        return (source, member, adapter);
    }

    private AiSourceDefinition ResolveSource(string? sourceHint, string capabilityName, string? explicitModel)
    {
        if (!string.IsNullOrWhiteSpace(explicitModel))
        {
            var capabilitySources = _sourceRegistry.GetSourcesWithCapability(capabilityName);
            if (capabilitySources.Count > 0)
            {
                return capabilitySources.First();
            }

            var allSources = _sourceRegistry.GetAllSources();
            if (allSources.Count > 0)
            {
                _logger?.LogWarning(
                    "No source advertises capability {Capability}; falling back to first configured source",
                    capabilityName);
                return allSources.First();
            }

            throw new InvalidOperationException(
                "No AI sources available. Configure an adapter (e.g. Ollama) or enable discovery.");
        }

        if (!string.IsNullOrWhiteSpace(sourceHint))
        {
            var source = _sourceRegistry.GetSource(sourceHint);
            if (source is not null)
            {
                return source;
            }

            if (sourceHint.Contains("::", StringComparison.Ordinal))
            {
                var sourceName = sourceHint.Split("::")[0];
                source = _sourceRegistry.GetSource(sourceName);
                if (source is not null)
                {
                    return source;
                }

                throw new InvalidOperationException(
                    $"Source '{sourceName}' not found for member reference '{sourceHint}'. " +
                    $"Available sources: {string.Join(", ", _sourceRegistry.GetSourceNames())}");
            }

            throw new InvalidOperationException(
                $"Source '{sourceHint}' not found. " +
                $"Available sources: {string.Join(", ", _sourceRegistry.GetSourceNames())}");
        }

        var candidates = _sourceRegistry.GetSourcesWithCapability(capabilityName);
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"No source found with capability '{capabilityName}'. Configure a source or enable discovery.");
        }

        return candidates.First();
    }

    private AiMemberDefinition SelectMember(AiSourceDefinition source, string? sourceHint)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        if (!string.IsNullOrWhiteSpace(sourceHint) && sourceHint.Contains("::", StringComparison.Ordinal))
        {
            var pinned = source.Members.FirstOrDefault(m =>
                string.Equals(m.Name, sourceHint, StringComparison.OrdinalIgnoreCase));

            if (pinned is null)
            {
                throw new InvalidOperationException(
                    $"Member '{sourceHint}' not found in source '{source.Name}'. " +
                    $"Available members: {string.Join(", ", source.Members.Select(m => m.Name))}");
            }

            return pinned;
        }

        if (source.Members.Count == 0)
        {
            throw new InvalidOperationException(
                $"Source '{source.Name}' has no members. Check configuration or discovery results.");
        }

        return source.Members
            .OrderBy(m => m.Order)
            .FirstOrDefault(m => m.HealthState != MemberHealthState.Unhealthy)
            ?? source.Members.OrderBy(m => m.Order).First();
    }

    private IAiAdapter ResolveAdapter(AiSourceDefinition source)
    {
        if (string.IsNullOrWhiteSpace(source.Provider))
        {
            throw new InvalidOperationException(
                $"Source '{source.Name}' has no provider configured");
        }

        var adapter = _adapterRegistry.Get(source.Provider);
        if (adapter is null)
        {
            throw new InvalidOperationException(
                $"No adapter found for provider '{source.Provider}'. " +
                $"Available adapters: {string.Join(", ", _adapterRegistry.All.Select(a => a.Id))}");
        }

        return adapter;
    }

    private static string? DetermineEffectiveModel(
        AiSourceDefinition source,
        AiMemberDefinition member,
        string capability,
        string? explicitModel)
    {
        if (!string.IsNullOrWhiteSpace(explicitModel))
        {
            return explicitModel;
        }

        var capabilities = source.GetEffectiveCapabilities(member);
        if (capabilities.TryGetValue(capability, out var config))
        {
            return config.Model;
        }

        return null;
    }
}

internal readonly record struct AiRouteResolution(
    AiSourceDefinition Source,
    AiMemberDefinition Member,
    IAiAdapter Adapter,
    string Capability,
    string? EffectiveModel);
