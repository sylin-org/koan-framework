using Koan.Data.Abstractions.Failures;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Runtime;

/// <summary>
/// What SQLite's own error codes mean, in Data's vocabulary. Only a unique-index violation is classified, by its
/// extended code and never by message text, so Data can report it as a conflict without naming the index (DATA-0150).
/// </summary>
internal sealed class SqliteFailureClassifier : IDataFailureClassifier
{
    public bool TryClassify(Exception nativeFailure, DataFailureContext context, out DataFailure failure)
    {
        if (nativeFailure is SqliteException { SqliteExtendedErrorCode: Infrastructure.Constants.UniqueConstraint })
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
