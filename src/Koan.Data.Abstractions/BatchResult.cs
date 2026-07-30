using Koan.Data.Abstractions.Failures;

namespace Koan.Data.Abstractions;

/// <summary>Provider-neutral outcome and execution receipt for one completed batch.</summary>
public sealed record BatchResult(int Added, int Updated, int Deleted)
{
    /// <summary>Atomicity the execution actually realized. The conservative default is non-atomic.</summary>
    public BatchAtomicity Atomicity { get; init; } = BatchAtomicity.NotGuaranteed;

    /// <summary>A successful return represents a committed result; typed failures carry unknown/non-commit facts.</summary>
    public DataCommitOutcome CommitOutcome { get; init; } = DataCommitOutcome.Committed;

    /// <summary>One ordered outcome per logical operation when the native batch supports complete outcomes.</summary>
    public IReadOnlyList<BatchItemResult> Items { get; init; } = Array.Empty<BatchItemResult>();

    public bool HasCompleteItemOutcomes { get; init; }
}
