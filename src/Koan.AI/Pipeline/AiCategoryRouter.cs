using System;
using System.Collections.Generic;
using System.Linq;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Categories;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Koan.AI.Context;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.AI.Pipeline;

/// <summary>
/// Category-aware routing engine. Resolves source, member, and adapter per AI category.
/// For task categories (e.g. Ocr) with a Via delegation, falls back to the protocol category
/// when no dedicated adapter is registered.
/// </summary>
internal sealed class AiCategoryRouter
{
    private readonly IAiAdapterRegistry _adapterRegistry;
    private readonly IAiSourceRegistry _sourceRegistry;
    private readonly AiOptions _options;
    private readonly ILogger<AiCategoryRouter>? _logger;

    private static readonly IReadOnlyDictionary<string, AiCategoryDefinition> Categories =
        new Dictionary<string, AiCategoryDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chat"] = new()
            {
                Name = "Chat",
                AdapterInterface = typeof(IChatAdapter),
            },
            ["Embed"] = new()
            {
                Name = "Embed",
                AdapterInterface = typeof(IEmbedAdapter),
            },
            ["Ocr"] = new()
            {
                Name = "Ocr",
                AdapterInterface = typeof(IOcrAdapter),
                Via = "Chat",
                DefaultModel = "glm-ocr",
            },
        };

    public AiCategoryRouter(
        IAiAdapterRegistry adapterRegistry,
        IAiSourceRegistry sourceRegistry,
        IOptions<AiOptions> options,
        ILogger<AiCategoryRouter>? logger = null)
    {
        _adapterRegistry = adapterRegistry;
        _sourceRegistry = sourceRegistry;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Resolve routing for a specific category. Merges scope overrides with config defaults.
    /// </summary>
    public AiRouteResolution Resolve(string category, string? sourceHint = null, string? modelHint = null)
    {
        if (!Categories.TryGetValue(category, out var definition))
            throw new InvalidOperationException($"Unknown AI category: '{category}'");

        // Merge scope context
        var (scopeSource, scopeModel) = AiCategoryScope.ResolveMerged(category, sourceHint, modelHint);

        // Category config defaults
        var categoryOptions = GetCategoryOptions(category);
        var effectiveSource = scopeSource ?? categoryOptions?.Source;
        var effectiveModel = scopeModel ?? categoryOptions?.Model ?? definition.DefaultModel;

        // Via delegation: if task category and no dedicated adapter, delegate to protocol category
        var via = categoryOptions?.Via ?? definition.Via;
        if (via is not null && !HasDedicatedAdapter(definition.AdapterInterface))
        {
            _logger?.LogDebug(
                "Category {Category} delegating via {Via} (no dedicated adapter)",
                category, via);
            return Resolve(via, effectiveSource, effectiveModel);
        }

        var source = ResolveSource(effectiveSource, MapCategoryToCapability(category), effectiveModel);
        var member = SelectMember(source, effectiveSource);
        var adapter = ResolveAdapter(source);
        var resolvedModel = DetermineEffectiveModel(source, member, MapCategoryToCapability(category), effectiveModel);

        return new AiRouteResolution(source, member, adapter, category, resolvedModel);
    }

    /// <summary>
    /// Resolve specifically for chat requests (convenience for pipeline use).
    /// </summary>
    public AiRouteResolution ResolveChat(AiChatRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return Resolve("Chat", request.Route?.AdapterId, request.Model);
    }

    /// <summary>
    /// Resolve specifically for embedding requests (convenience for pipeline use).
    /// </summary>
    public AiRouteResolution ResolveEmbeddings(AiEmbeddingsRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return Resolve("Embed", null, request.Model);
    }

    private bool HasDedicatedAdapter(Type adapterInterface)
    {
        return _adapterRegistry.All.Any(a => adapterInterface.IsInstanceOfType(a));
    }

    private AiCategoryOptions? GetCategoryOptions(string category) => category switch
    {
        "Chat" => _options.Chat,
        "Embed" => _options.Embed,
        "Ocr" => _options.Ocr,
        _ => null
    };

    private static string MapCategoryToCapability(string category) => category switch
    {
        "Chat" => "Chat",
        "Embed" => "Embedding",
        "Ocr" => "Ocr",
        _ => category
    };

    private AiSourceDefinition ResolveSource(string? sourceHint, string capabilityName, string? explicitModel)
    {
        if (!string.IsNullOrWhiteSpace(explicitModel))
        {
            var capabilitySources = _sourceRegistry.GetSourcesWithCapability(capabilityName);
            if (capabilitySources.Count > 0)
                return capabilitySources.First();

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
            if (source is not null) return source;

            if (sourceHint.Contains("::", StringComparison.Ordinal))
            {
                var sourceName = sourceHint.Split("::")[0];
                source = _sourceRegistry.GetSource(sourceName);
                if (source is not null) return source;

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

    private static AiMemberDefinition SelectMember(AiSourceDefinition source, string? sourceHint)
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
            return explicitModel;

        var capabilities = source.GetEffectiveCapabilities(member);
        if (capabilities.TryGetValue(capability, out var config))
            return config.Model;

        return null;
    }
}

internal readonly record struct AiRouteResolution(
    AiSourceDefinition Source,
    AiMemberDefinition Member,
    IAiAdapter Adapter,
    string Category,
    string? EffectiveModel);
