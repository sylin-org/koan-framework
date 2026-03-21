namespace Koan.AI.Compute;

/// <summary>
/// Result of resolving a compute requirement to a specific resource.
/// Contains the selected target, reasoning, alternatives, and optional local fallback.
/// </summary>
public sealed record ComputeResolution
{
    public required ComputeResource Target { get; init; }
    public required string Reason { get; init; }
    public required IReadOnlyList<ComputeResource> Alternatives { get; init; }

    /// <summary>Local fallback resource, or null if local hardware cannot handle the workload.</summary>
    public ComputeResource? LocalFallback { get; init; }
}
