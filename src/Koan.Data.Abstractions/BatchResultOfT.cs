using Koan.Data.Abstractions.Failures;

namespace Koan.Data.Abstractions;

/// <summary>Ordered keyed outcomes and execution truth for one completed batch.</summary>
public sealed record BatchResult<TKey>
    where TKey : notnull
{
    public BatchResult(IEnumerable<BatchItemResult<TKey>> items, BatchAtomicity atomicity = BatchAtomicity.NotGuaranteed)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = Array.AsReadOnly(items.ToArray());
        Atomicity = atomicity;
    }

    public IReadOnlyList<BatchItemResult<TKey>> Items { get; }
    public BatchAtomicity Atomicity { get; }
    public DataCommitOutcome CommitOutcome { get; init; } = DataCommitOutcome.Committed;
}
