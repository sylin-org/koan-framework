namespace Koan.AI.Contracts.Sources;

/// <summary>
/// Advanced runtime control plane for inspecting and changing AI sources.
/// Ordinary applications can rely on automatic source discovery and routing.
/// </summary>
public interface IAiSourceControl
{
    /// <summary>Inspect a provider endpoint without registering it.</summary>
    Task<AiSourceInspection> InspectAsync(
        AiSourceCandidate candidate,
        CancellationToken ct = default);

    /// <summary>Add or replace a source. Replacement is allowed only for the same origin.</summary>
    AiSourceDefinition Apply(AiSourceDefinition source);

    /// <summary>Include an existing source in routing.</summary>
    bool Enable(string name);

    /// <summary>Immediately exclude an existing source from routing and health probing.</summary>
    bool Disable(string name);

    /// <summary>Remove an existing source. An expected origin can protect a caller-owned source.</summary>
    bool Remove(string name, string? expectedOrigin = null);
}

/// <summary>A provider endpoint proposed for inspection before source registration.</summary>
public sealed record AiSourceCandidate
{
    public required string Provider { get; init; }
    public required string Endpoint { get; init; }
}

/// <summary>Structured provider-owned inspection of an AI endpoint.</summary>
public sealed record AiSourceInspection
{
    public required string Provider { get; init; }
    public required string Endpoint { get; init; }
    /// <summary>True when at least one provider-owned inspection facet answered successfully.</summary>
    public required bool Available { get; init; }
    /// <summary>The provider runtime version, when its protocol exposes one.</summary>
    public string? Version { get; init; }
    /// <summary>True when the provider version was inspected successfully.</summary>
    public bool VersionAvailable { get; init; }
    /// <summary>Installed models. An empty collection is meaningful when <see cref="ModelsAvailable"/> is true.</summary>
    public IReadOnlyList<string> Models { get; init; } = [];
    /// <summary>True when the installed-model catalog was inspected successfully.</summary>
    public bool ModelsAvailable { get; init; }
    /// <summary>Models currently resident in provider memory.</summary>
    public IReadOnlyList<string> ResidentModels { get; init; } = [];
    /// <summary>True when provider residency was inspected successfully.</summary>
    public bool ResidentModelsAvailable { get; init; }
    public IReadOnlySet<string> Capabilities { get; init; } = new HashSet<string>();
    /// <summary>Provider-neutral detail for unavailable or partially available inspection facets.</summary>
    public string? Detail { get; init; }
}
