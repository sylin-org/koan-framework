using Koan.Data.Abstractions.Failures;
using Npgsql;

namespace Koan.Data.Connector.Postgres.Runtime;

/// <summary>
/// What PostgreSQL's SQLSTATE codes mean, in Data's vocabulary. Only <c>23505 unique_violation</c> is classified, by
/// code and never by message text, so Data can report it as a conflict without naming the index (DATA-0150). A
/// statement that raises it is rolled back, so nothing was committed.
/// </summary>
internal sealed class PostgresFailureClassifier : IDataFailureClassifier
{
    public bool TryClassify(Exception nativeFailure, DataFailureContext context, out DataFailure failure)
    {
        if (nativeFailure is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            failure = new DataFailure(
                UniqueValueConflictException.FailureCode,
                DataFailureKind.Conflict,
                DataCommitOutcome.NotCommitted,
                DataRetryDisposition.Never,
                DataReplayDisposition.Never);
            return true;
        }

        failure = default!;
        return false;
    }
}
