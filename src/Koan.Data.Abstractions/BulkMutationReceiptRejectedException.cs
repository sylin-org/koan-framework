using Koan.Data.Abstractions.Failures;

namespace Koan.Data.Abstractions;

/// <summary>Reports a bulk result whose affected count cannot prove the requested mutation set.</summary>
public sealed class BulkMutationReceiptRejectedException : InvalidOperationException
{
    public BulkMutationReceiptRejectedException(
        string entityType,
        int expected,
        int reported,
        DataCommitOutcome commitOutcome)
        : base(
            $"The bulk mutation receipt for '{entityType}' reported {reported} of {expected} prepared operations. " +
            "The operation will not be replayed; inspect current state before deciding on compensation.")
    {
        EntityType = entityType;
        Expected = expected;
        Reported = reported;
        CommitOutcome = commitOutcome;
    }

    public string EntityType { get; }
    public int Expected { get; }
    public int Reported { get; }
    public DataCommitOutcome CommitOutcome { get; }
}
