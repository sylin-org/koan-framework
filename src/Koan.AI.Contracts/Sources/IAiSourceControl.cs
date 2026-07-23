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
    public required bool Available { get; init; }
    public IReadOnlyList<string> Models { get; init; } = [];
    public IReadOnlySet<string> Capabilities { get; init; } = new HashSet<string>();
    public string? Detail { get; init; }
}
