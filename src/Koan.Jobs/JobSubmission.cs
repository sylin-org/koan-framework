namespace Koan.Jobs;

/// <summary>
/// A fixed-size summary of one finite or asynchronous Entity job-submission operation.
/// </summary>
/// <remarks>
/// The summary describes acceptance by the Jobs ledger boundary, not handler completion. It never
/// retains per-item handles, so its size is independent of the source size.
/// </remarks>
public sealed class JobSubmission
{
    internal JobSubmission(
        string workType,
        string action,
        long enumerated,
        long submitted,
        long coalesced,
        long failed,
        bool sourceCompleted,
        bool pendingCommit)
    {
        WorkType = workType;
        Action = action;
        Enumerated = enumerated;
        Submitted = submitted;
        Coalesced = coalesced;
        Failed = failed;
        SourceCompleted = sourceCompleted;
        PendingCommit = pendingCommit;
    }

    /// <summary>The job Entity contract processed by this operation.</summary>
    public string WorkType { get; }

    /// <summary>The requested action; empty identifies the job type's default action.</summary>
    public string Action { get; }

    /// <summary>Source items whose submission processing began.</summary>
    public long Enumerated { get; }

    /// <summary>New work records whose ledger append completed or enlisted in the ambient transaction.</summary>
    public long Submitted { get; }

    /// <summary>Source items accepted by an already-active idempotent work record.</summary>
    public long Coalesced { get; }

    /// <summary>Source items whose submission processing failed before confirmed acceptance.</summary>
    public long Failed { get; }

    /// <summary>The confirmed accepted prefix: newly submitted plus explicitly coalesced items.</summary>
    public long Accepted => Submitted + Coalesced;

    /// <summary>True when the source reached its natural end.</summary>
    public bool SourceCompleted { get; }

    /// <summary>
    /// True when new ledger rows are enlisted in an ambient transaction and remain contingent on commit.
    /// </summary>
    public bool PendingCommit { get; }
}
