namespace Koan.Jobs;

/// <summary>A Jobs source or acceptance failure carrying the confirmed submission prefix.</summary>
public sealed class JobSubmissionException : Exception
{
    public enum FailureKind
    {
        /// <summary>The Entity source failed while being enumerated or disposed.</summary>
        SourceFailed,

        /// <summary>An Entity could not be prepared, persisted, or accepted by the ledger boundary.</summary>
        SubmissionFailed
    }

    internal JobSubmissionException(
        FailureKind failure,
        string message,
        JobSubmission submission,
        Exception innerException)
        : base(message, innerException)
    {
        Failure = failure;
        Submission = submission;
    }

    public FailureKind Failure { get; }

    /// <summary>The fixed-size confirmed prefix observed before the failure.</summary>
    public JobSubmission Submission { get; }
}
