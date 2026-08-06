using Koan.Data.Abstractions.Failures;

namespace Koan.Data.Abstractions;

/// <summary>Reports a batch receipt that does not prove the execution guarantee selected before dispatch.</summary>
public sealed class BatchReceiptRejectedException : InvalidOperationException
{
    public BatchReceiptRejectedException(
        string entityType,
        string correction,
        DataCommitOutcome commitOutcome)
        : base(
            $"The batch receipt for '{entityType}' is incomplete or inconsistent. {correction} " +
            "The batch will not be replayed.")
    {
        EntityType = entityType;
        Correction = correction;
        CommitOutcome = commitOutcome;
    }

    public string EntityType { get; }
    public string Correction { get; }
    public DataCommitOutcome CommitOutcome { get; }
}
