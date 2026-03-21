namespace Koan.AI.Contracts.Shared;

/// <summary>
/// Full provenance chain of a model: which base + which data + which method
/// + which hyperparameters → this model. Shared across Model, Training, and Eval contexts.
/// </summary>
public sealed record Lineage(
    ModelRef? Base = null,
    string? Method = null,
    DatasetRef? Data = null,
    IReadOnlyList<EvalScore>? EvalScores = null,
    string? TrainedBy = null,
    DateTimeOffset? TrainedAt = null,
    string? ComputeUsed = null,
    string? Notes = null)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Base is not null) parts.Add($"base={Base}");
        if (Method is not null) parts.Add($"method={Method}");
        if (Data is not null) parts.Add($"data={Data}");
        if (TrainedBy is not null) parts.Add($"by={TrainedBy}");
        if (TrainedAt is not null) parts.Add($"at={TrainedAt:yyyy-MM-dd}");
        return string.Join(", ", parts);
    }
}
