namespace Koan.AI.Eval;

/// <summary>
/// Severity of distribution drift between two evaluation snapshots.
/// </summary>
public enum DriftStatus
{
    /// <summary>No meaningful drift detected.</summary>
    OK,

    /// <summary>Minor drift — worth monitoring.</summary>
    Notice,

    /// <summary>Significant drift — action recommended.</summary>
    Warning
}

/// <summary>
/// Result of comparing two evaluation snapshots for distribution drift.
/// Score ranges from 0 (identical) to 1 (completely different).
/// </summary>
public sealed record DriftResult
{
    /// <summary>Overall drift score: 0 = identical, 1 = completely different.</summary>
    public required double Score { get; init; }

    /// <summary>Severity classification based on drift score.</summary>
    public required DriftStatus Status { get; init; }

    /// <summary>The metrics with the largest distribution shifts.</summary>
    public required List<string> TopShifts { get; init; }

    /// <summary>Human-readable recommendation, if any.</summary>
    public string? Recommendation { get; init; }
}
