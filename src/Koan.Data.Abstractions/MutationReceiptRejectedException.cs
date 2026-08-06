using Koan.Data.Abstractions.Failures;

namespace Koan.Data.Abstractions;

/// <summary>Rejects an impossible native mutation result after dispatch without replaying it.</summary>
public sealed class MutationReceiptRejectedException : InvalidOperationException
{
    public MutationReceiptRejectedException(string entityType, string correction, DataCommitOutcome commitOutcome)
        : base($"The mutation receipt for '{entityType}' is inconsistent. {correction} The operation will not be replayed.")
    {
        EntityType = entityType;
        Correction = correction;
        CommitOutcome = commitOutcome;
    }

    public string EntityType { get; }
    public string Correction { get; }
    public DataCommitOutcome CommitOutcome { get; }
}
