namespace Koan.Data.Abstractions.Failures;

/// <summary>Stable, redacted failure facts returned by an adapter translator to Data.</summary>
public sealed class DataFailure
{
    public DataFailure(
        string code,
        DataFailureKind kind,
        DataCommitOutcome commitOutcome,
        DataRetryDisposition retry,
        DataReplayDisposition replay,
        string? evidenceReference = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (commitOutcome is DataCommitOutcome.Committed or DataCommitOutcome.Unknown &&
            replay != DataReplayDisposition.Never)
        {
            throw new ArgumentException(
                "A committed or outcome-unknown operation can never be replayed automatically.",
                nameof(replay));
        }

        Code = code.Trim();
        Kind = kind;
        CommitOutcome = commitOutcome;
        Retry = retry;
        Replay = replay;
        EvidenceReference = string.IsNullOrWhiteSpace(evidenceReference) ? null : evidenceReference.Trim();
    }

    public string Code { get; }
    public DataFailureKind Kind { get; }
    public DataCommitOutcome CommitOutcome { get; }
    public DataRetryDisposition Retry { get; }
    public DataReplayDisposition Replay { get; }
    public string Message => DataFailureCorrections.Message(Kind);
    public string Correction => DataFailureCorrections.Correction(Kind);
    public string? EvidenceReference { get; }
}
