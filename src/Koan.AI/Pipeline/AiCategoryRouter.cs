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
using Koan.AI.Contracts;
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
    private readonly IAiRecipeProvider? _recipe;
    private readonly IAiModelAdvisor? _advisor;
    private readonly ILogger<AiCategoryRouter>? _logger;

    private static readonly IReadOnlyDictionary<string, AiCategoryDefinition> Categories =
        new Dictionary<string, AiCategoryDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [AiCapability.Chat] = new()
            {
                Name = AiCapability.Chat,
                AdapterInterface = typeof(IChatAdapter),
            },
            [AiCapability.Embed] = new()
            {
                Name = AiCapability.Embed,
                AdapterInterface = typeof(IEmbedAdapter),
            },
            [AiCapability.Ocr] = new()
            {
                Name = AiCapability.Ocr,
                AdapterInterface = typeof(IOcrAdapter),
                Via = AiCapability.Chat,
                DefaultModel = "glm-ocr",
            },
        };

    public AiCategoryRouter(
        IAiAdapterRegistry adapterRegistry,
        IAiSourceRegistry sourceRegistry,
        IOptions<AiOptions> options,
        IAiRecipeProvider? recipe = null,
        IAiModelAdvisor? advisor = null,
        ILogger<AiCategoryRouter>? logger = null)
    {
        _adapterRegistry = adapterRegistry;
        _sourceRegistry = sourceRegistry;
        _options = options.Value;
        _recipe = recipe;
        _advisor = advisor;
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

        // Recipe layer: human-curated bindings sit between scope and advisor (AI-0032)
        var recipeModel = scopeModel is null ? _recipe?.GetModel(category) : null;
        var advisorModel = scopeModel is null && recipeModel is null
            ? _advisor?.GetRecommendedModel(category) : null;
        var effectiveModel = scopeModel ?? recipeModel ?? advisorModel
            ?? categoryOptions?.Model ?? definition.DefaultModel;

        if (recipeModel is not null)
            _logger?.LogDebug("Category {Category} using recipe-bound model: {Model}", category, recipeModel);
        else if (advisorModel is not null)
            _logger?.LogDebug("Category {Category} using advisor-recommended model: {Model}", category, advisorModel);

        // Via delegation: if task category and no dedicated adapter, delegate to protocol category
        var via = categoryOptions?.Via ?? definition.Via;
        if (via is not null && !HasDedicatedAdapter(definition.AdapterInterface))
        {
            _logger?.LogDebug(
                "Category {Category} delegating via {Via} (no dedicated adapter)",
                category, via);
            return Resolve(via, effectiveSource, effectiveModel);
        }

        var capability = MapCategoryToCapability(category);
        var source = ResolveSource(effectiveSource, capability, effectiveModel);
        var requireMemberCapability =
            !string.IsNullOrWhiteSpace(effectiveSource) || string.IsNullOrWhiteSpace(effectiveModel);
        var member = SelectMember(source, effectiveSource, capability, requireMemberCapability);
        var adapter = ResolveAdapter(source);
        var resolvedModel = DetermineEffectiveModel(source, member, capability, effectiveModel);

        return new AiRouteResolution(source, member, adapter, category, resolvedModel);
    }

    /// <summary>
    /// Resolve specifically for chat requests (convenience for pipeline use).
    /// </summary>
    public AiRouteResolution ResolveChat(AiChatRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        // AI-0035: URL override path. Bypass source / member resolution when the caller has
        // supplied a URL directly. The caller owns health, enable/disable, and capability
        // tracking for ad-hoc targets; Koan acts as the HTTP+JSON executor.
        if (!string.IsNullOrWhiteSpace(request.OverrideUrl))
        {
            return SynthesizeOverrideResolution(
                url: request.OverrideUrl!,
                provider: request.OverrideProvider,
                category: AiCapability.Chat,
                model: request.Model);
        }

        var modelHint = request.Model;

        // Vision-aware routing: when request contains image content and no explicit model,
        // ask the advisor for a vision-capable model instead of a text-only chat model.
        if (string.IsNullOrEmpty(modelHint) && _advisor is not null)
        {
            var hasImage = request.Messages?.Any(m =>
                m.Parts?.Any(p => string.Equals(p.Type, "image", StringComparison.OrdinalIgnoreCase)) == true) == true;

            if (hasImage)
            {
                var visionModel = _advisor.GetRecommendedModel(AiCapability.Vision);
                if (visionModel is not null)
                {
                    _logger?.LogDebug("Vision content detected — using advisor-recommended vision model: {Model}", visionModel);
                    modelHint = visionModel;
                }
            }
        }

        return Resolve(AiCapability.Chat, request.Route?.Source, modelHint);
    }

    /// <summary>
    /// Resolve specifically for embedding requests (convenience for pipeline use).
    /// </summary>
    public AiRouteResolution ResolveEmbeddings(AiEmbeddingsRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        // AI-0035: URL override path. Same semantics as the chat-side short-circuit.
        if (!string.IsNullOrWhiteSpace(request.OverrideUrl))
        {
            return SynthesizeOverrideResolution(
                url: request.OverrideUrl!,
                provider: request.OverrideProvider,
                category: AiCapability.Embed,
                model: request.Model);
        }

        return Resolve(AiCapability.Embed, null, request.Model);
    }

    private bool HasDedicatedAdapter(Type adapterInterface)
    {
        return _adapterRegistry.All.Any(a => adapterInterface.IsInstanceOfType(a));
    }

    private AiCategoryOptions? GetCategoryOptions(string category) => category switch
    {
        AiCapability.Chat => _options.Chat,
        AiCapability.Embed => _options.Embed,
        AiCapability.Ocr => _options.Ocr,
        _ => null
    };

    private static string MapCategoryToCapability(string category) => category switch
    {
        AiCapability.Chat => "Chat",
        AiCapability.Embed => "Embedding",
        AiCapability.Ocr => "Ocr",
        _ => category
    };

    private AiSourceDefinition ResolveSource(string? sourceHint, string capabilityName, string? explicitModel)
    {
        if (!string.IsNullOrWhiteSpace(sourceHint))
        {
            var sourceName = GetSourceName(sourceHint);
            var source = _sourceRegistry.GetSource(sourceName);
            if (source is null)
            {
                var memberContext = sourceHint.Contains("::", StringComparison.Ordinal)
                    ? $" for member reference '{sourceHint}'"
                    : string.Empty;
                throw new InvalidOperationException(
                    $"Source '{sourceName}' not found{memberContext}. " +
                    $"Usable sources for capability '{capabilityName}': {FormatChoices(GetUsableSourceNames(capabilityName))}");
            }

            if (!source.IsEnabled)
            {
                throw new InvalidOperationException(
                    $"AI source '{source.Name}' is disabled. Enable it through IAiSourceControl or select another source. " +
                    $"Usable sources for capability '{capabilityName}': {FormatChoices(GetUsableSourceNames(capabilityName))}");
            }

            if (!SupportsCapability(source, capabilityName))
            {
                throw new InvalidOperationException(
                    $"Source '{source.Name}' does not support capability '{capabilityName}'. " +
                    $"Usable sources: {FormatChoices(GetUsableSourceNames(capabilityName))}");
            }

            return source;
        }

        if (!string.IsNullOrWhiteSpace(explicitModel))
        {
            var capabilitySources = _sourceRegistry.GetSourcesWithCapability(capabilityName);
            if (capabilitySources.Count > 0)
                return capabilitySources.First();

            var allSources = _sourceRegistry.GetAllSources();
            var enabledSources = allSources.Where(source => source.IsEnabled).ToArray();
            if (enabledSources.Length > 0)
            {
                _logger?.LogWarning(
                    "No source advertises capability {Capability}; falling back to first configured source",
                    capabilityName);
                return enabledSources.First();
            }

            throw new InvalidOperationException(
                "No AI sources available. Configure an adapter (e.g. Ollama) or enable discovery.");
        }

        var candidates = _sourceRegistry.GetSourcesWithCapability(capabilityName);
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"No source found with capability '{capabilityName}'. Configure a source or enable discovery.");
        }

        return candidates.First();
    }

    private IReadOnlyList<string> GetUsableSourceNames(string capabilityName)
        => _sourceRegistry.GetAllSources()
            .Where(source => source.IsEnabled && SupportsCapability(source, capabilityName))
            .OrderByDescending(source => source.Priority)
            .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .Select(source => source.Name)
            .ToArray();

    private static bool SupportsCapability(AiSourceDefinition source, string capabilityName)
    {
        if (source.Members.Count == 0)
            return source.Capabilities.ContainsKey(capabilityName);

        return source.Members.Any(member =>
            source.GetEffectiveCapabilities(member).ContainsKey(capabilityName));
    }

    private static string GetSourceName(string sourceHint)
    {
        var separator = sourceHint.IndexOf("::", StringComparison.Ordinal);
        return separator < 0 ? sourceHint : sourceHint[..separator];
    }

    private static string FormatChoices(IEnumerable<string> choices)
    {
        var values = choices.ToArray();
        return values.Length == 0 ? "(none)" : string.Join(", ", values);
    }

    private static AiMemberDefinition SelectMember(
        AiSourceDefinition source,
        string? sourceHint,
        string capabilityName,
        bool requireCapability)
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
                    $"Usable members for capability '{capabilityName}': " +
                    $"{FormatChoices(GetUsableMemberNames(source, capabilityName))}");
            }

            if (requireCapability &&
                !source.GetEffectiveCapabilities(pinned).ContainsKey(capabilityName))
            {
                throw new InvalidOperationException(
                    $"Member '{sourceHint}' does not support capability '{capabilityName}'. " +
                    $"Usable members in source '{source.Name}': " +
                    $"{FormatChoices(GetUsableMemberNames(source, capabilityName))}");
            }

            return pinned;
        }

        if (source.Members.Count == 0)
        {
            throw new InvalidOperationException(
                $"Source '{source.Name}' has no members. Check configuration or discovery results.");
        }

        var candidates = requireCapability
            ? source.Members
                .Where(member => source.GetEffectiveCapabilities(member).ContainsKey(capabilityName))
                .ToArray()
            : source.Members.ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                $"Source '{source.Name}' has no member supporting capability '{capabilityName}'.");
        }

        return candidates
            .OrderBy(m => m.Order)
            .FirstOrDefault(m => m.HealthState != MemberHealthState.Unhealthy)
            ?? candidates.OrderBy(m => m.Order).First();
    }

    private static IReadOnlyList<string> GetUsableMemberNames(
        AiSourceDefinition source,
        string capabilityName)
        => source.Members
            .Where(member => source.GetEffectiveCapabilities(member).ContainsKey(capabilityName))
            .OrderBy(member => member.Order)
            .Select(member => member.Name)
            .ToArray();

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

    /// <summary>
    /// AI-0035: synthesize a routing resolution for a caller-supplied URL override. Builds
    /// transient <see cref="AiSourceDefinition"/> + <see cref="AiMemberDefinition"/> instances
    /// that carry the URL through the same downstream path the registry-driven resolution uses;
    /// the adapter receives the URL via <c>request.InternalConnectionString</c> without caring
    /// whether the source was registered or synthesized.
    /// </summary>
    private AiRouteResolution SynthesizeOverrideResolution(
        string url, string? provider, string category, string? model)
    {
        var providerKey = string.IsNullOrWhiteSpace(provider) ? "ollama" : provider!.Trim();

        var adapter = _adapterRegistry.Get(providerKey)
            ?? throw new InvalidOperationException(
                $"AI-0035 URL override: no adapter registered for provider '{providerKey}'. " +
                $"Available adapters: {string.Join(", ", _adapterRegistry.All.Select(a => a.Id))}");

        var syntheticMember = new AiMemberDefinition
        {
            Name = $"{providerKey}::override",
            ConnectionString = url,
            Order = 0,
            Origin = "url-override",
            IsAutoDiscovered = false,
            HealthState = MemberHealthState.Healthy,
        };

        var syntheticSource = new AiSourceDefinition
        {
            Name = $"override::{providerKey}",
            Provider = providerKey,
            Priority = 0,
            Policy = "Fallback",
            Members = new List<AiMemberDefinition> { syntheticMember },
            Origin = "url-override",
            IsAutoDiscovered = false,
        };

        _logger?.LogDebug(
            "URL override resolved: provider={Provider} url={Url} adapter={AdapterId} model={Model}",
            providerKey, url, adapter.Id, model);

        return new AiRouteResolution(syntheticSource, syntheticMember, adapter, category, model);
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
