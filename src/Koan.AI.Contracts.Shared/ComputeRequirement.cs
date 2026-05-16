namespace Koan.AI.Contracts.Shared;

/// <summary>
/// Describes compute needs for a workload. Shared across Training, Model, and Eval contexts.
/// Used by the Compute Fabric to resolve where work should run.
/// </summary>
public sealed record ComputeRequirement(
    Accelerator Accelerator = Accelerator.Any,
    long? MinVramBytes = null,
    ComputeLocation? Location = null,
    string? PreferredNode = null)
{
    /// <summary>No specific requirements — use whatever is available.</summary>
    public static ComputeRequirement Default => new();

    /// <summary>Helper to specify VRAM in GiB.</summary>
    public static ComputeRequirement WithVram(long gib) =>
        new(MinVramBytes: gib * 1024 * 1024 * 1024);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Accelerator != Accelerator.Any) parts.Add($"accel={Accelerator}");
        if (MinVramBytes is not null) parts.Add($"vram>={MinVramBytes / (1024 * 1024 * 1024)}GiB");
        if (Location is not null) parts.Add($"loc={Location}");
        if (PreferredNode is not null) parts.Add($"node={PreferredNode}");
        return parts.Count > 0 ? string.Join(", ", parts) : "any compute";
    }
}
