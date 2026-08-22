using Koan.Data.Abstractions.Failures;
using Microsoft.Data.SqlClient;

namespace Koan.Data.Connector.SqlServer.Runtime;

/// <summary>
/// What SQL Server's own error numbers mean, in Data's vocabulary.
///
/// <para>Only the deadlock victim is classified, and deliberately so. Every field of a
/// <see cref="DataFailure"/> is a claim a caller may act on — a wrong commit-outcome turns a retry into a
/// double execution — so this states what the store guarantees and nothing more. An unrecognised failure
/// returns <see langword="false"/> and stays raw, which is what every SQL Server failure did before this
/// existed.</para>
/// </summary>
internal sealed class SqlServerFailureClassifier : IDataFailureClassifier
{
    /// <summary>"Transaction was deadlocked on lock resources and has been chosen as the deadlock victim."</summary>
    internal const int DeadlockVictim = 1205;

    public bool TryClassify(Exception nativeFailure, DataFailureContext context, out DataFailure failure)
    {
        // By number, never by message: the contract forbids classifying on message text, and the text is
        // localized. A SqlException carries a collection — a deadlock can arrive behind another error — so the
        // whole collection is searched rather than just Number, which reports only the first.
        if (nativeFailure is SqlException sql)
        {
            foreach (SqlError error in sql.Errors)
            {
                if (error.Number != DeadlockVictim) continue;
                failure = Deadlocked();
                return true;
            }
        }

        failure = default!;
        return false;
    }

    /// <summary>
    /// The deadlock classification on its own, so a spec can assert it without a <see cref="SqlException"/> —
    /// that type cannot be constructed outside the driver.
    ///
    /// <para><see cref="DataCommitOutcome.NotCommitted"/> is the strong half: SQL Server has already rolled the
    /// victim back, so a repeat cannot double anything. <see cref="DataRetryDisposition.RequiresIdempotency"/>
    /// is deliberately weaker than the rollback alone would allow — the enum has no "always safe" and
    /// overstating safety here is the expensive direction to be wrong in.</para>
    /// </summary>
    internal static DataFailure Deadlocked() => new(
        $"sqlserver:{DeadlockVictim}",
        DataFailureKind.Conflict,
        DataCommitOutcome.NotCommitted,
        DataRetryDisposition.RequiresIdempotency,
        DataReplayDisposition.RequiresIdempotency);
}
