using Koan.Data.Abstractions.Failures;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo.Runtime;

/// <summary>
/// What MongoDB's server errors mean, in Data's vocabulary. Only a duplicate key is classified, by its category and
/// never by message text, so Data can report it as a conflict without naming the index (DATA-0150). A duplicate
/// identity across scopes is reported by the repository before this is asked. An ordered bulk write may have applied
/// the writes before the failing one, so its commit outcome is unknown.
/// </summary>
internal sealed class MongoFailureClassifier : IDataFailureClassifier
{
    public bool TryClassify(Exception nativeFailure, DataFailureContext context, out DataFailure failure)
    {
        var outcome = nativeFailure switch
        {
            MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey } => DataCommitOutcome.NotCommitted,
            MongoBulkWriteException bulk when bulk.WriteErrors.Any(static error =>
                error.Category == ServerErrorCategory.DuplicateKey) => DataCommitOutcome.Unknown,
            _ => (DataCommitOutcome?)null
        };
        if (outcome is { } committed)
        {
            failure = new DataFailure(
                UniqueValueConflictException.FailureCode,
                DataFailureKind.Conflict,
                committed,
                DataRetryDisposition.Never,
                DataReplayDisposition.Never);
            return true;
        }

        failure = default!;
        return false;
    }
}
