using Koan.AI.Contracts.Shared;

namespace Koan.AI.Compute;

/// <summary>
/// Readiness check specification: does the fleet have the models, capabilities, and connectivity required?
/// </summary>
public sealed record ReadinessSpec
{
    public required string[] RequiredModels { get; init; }
    public required ComputeCapability[] RequiredCapabilities { get; init; }
    public bool NetworkRequired { get; init; }
}

/// <summary>
/// Service interface for compute fabric operations: discovery, resolution, and readiness checks.
/// </summary>
public interface IComputeService
{
    /// <summary>Returns the best available compute resource for general use.</summary>
    Task<ComputeResource?> AvailableAsync(CancellationToken ct = default);

    /// <summary>Returns all known compute resources across local and network.</summary>
    Task<IReadOnlyList<ComputeResource>> FleetAsync(CancellationToken ct = default);

    /// <summary>Resolves the best compute target for a specific requirement.</summary>
    Task<ComputeResolution> ResolveAsync(ComputeRequirement requirement, CancellationToken ct = default);

    /// <summary>Checks whether the fleet satisfies a readiness specification.</summary>
    Task<bool> CheckAsync(ReadinessSpec spec, CancellationToken ct = default);
}
