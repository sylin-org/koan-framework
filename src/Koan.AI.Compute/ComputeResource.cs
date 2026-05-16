using Koan.AI.Contracts.Shared;

namespace Koan.AI.Compute;

/// <summary>
/// Lifecycle status of a compute resource.
/// </summary>
public enum ComputeStatus
{
    Available,
    Busy,
    Offline
}

/// <summary>
/// A single compute resource — GPU, CPU, or network node — with its capabilities and current status.
/// </summary>
public sealed record ComputeResource
{
    public required string Id { get; init; }
    public required Accelerator Accelerator { get; init; }
    public required long VramBytes { get; init; }
    public string? DeviceName { get; init; }
    public required ComputeLocation Location { get; init; }
    public required string[] Runtimes { get; init; }
    public string? StoneId { get; init; }
    public ComputeStatus Status { get; init; } = ComputeStatus.Available;

    /// <summary>
    /// Whether this resource meets the given compute requirement.
    /// </summary>
    public bool Satisfies(ComputeRequirement requirement)
    {
        if (Status != ComputeStatus.Available)
            return false;

        if (requirement.Accelerator != Accelerator.Any && requirement.Accelerator != Accelerator)
            return false;

        if (requirement.MinVramBytes is not null && VramBytes < requirement.MinVramBytes)
            return false;

        if (requirement.Location is not null && requirement.Location != Location)
            return false;

        return true;
    }
}
