namespace Koan.Jobs;

/// <summary>A canceled Jobs submission carrying the confirmed accepted prefix.</summary>
public sealed class JobSubmissionCanceledException : OperationCanceledException
{
    internal JobSubmissionCanceledException(
        string message,
        JobSubmission submission,
        OperationCanceledException innerException,
        CancellationToken cancellationToken)
        : base(message, innerException, cancellationToken)
        => Submission = submission;

    /// <summary>The fixed-size confirmed prefix observed before cancellation.</summary>
    public JobSubmission Submission { get; }
}
