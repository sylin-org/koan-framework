using Koan.AI.Contracts.Shared;

namespace Koan.AI.Training;

/// <summary>
/// Configuration for an alignment job (DPO, RLHF, KTO, ORPO).
/// </summary>
public sealed record AlignOptions
{
    /// <summary>Base model to align.</summary>
    public required ModelRef Base { get; init; }

    /// <summary>Preference dataset.</summary>
    public required DatasetRef Data { get; init; }

    /// <summary>Alignment method. Defaults to DPO.</summary>
    public AlignMethod Method { get; init; } = AlignMethod.DPO;

    /// <summary>Beta parameter controlling divergence from reference policy.</summary>
    public double Beta { get; init; } = 0.1;

    /// <summary>Compute requirements for the alignment job.</summary>
    public ComputeRequirement? Compute { get; init; }
}
