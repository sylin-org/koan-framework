using Koan.Data.Abstractions.Failures;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Connector.SqlServer.Runtime;
using Koan.Data.Core.Failures;

namespace Koan.Data.Connector.SqlServer.Tests.Specs;

/// <summary>
/// What SQL Server's deadlock means in Data's vocabulary, and what that meaning earns.
///
/// <para>Dockerless: the classification is asserted directly, because a <c>SqlException</c> cannot be
/// constructed outside the driver and provoking a real deadlock on demand is not a test, it is a coin toss.
/// The deadlock itself is real and reproduced — eight concurrent claimers, about one full jobs-suite run in
/// five (PMC-056) — and what this pins is the decision taken about it.</para>
/// </summary>
public sealed class SqlServerFailureClassifierSpec
{
    private static readonly DataFailureContext Context = new(
        "sqlserver", "Default", "job.claim", DataOperationEffect.Write,
        Dispatched: true, CommitBoundaryCrossed: true);

    [Fact(DisplayName = "SQL Server: a deadlock victim is a conflict that did not commit")]
    public void A_deadlock_victim_is_a_rolled_back_conflict()
    {
        var failure = SqlServerFailureClassifier.Deadlocked();

        failure.Code.Should().Be("sqlserver:1205", "the number is the stable fact; the message is localized");
        failure.Kind.Should().Be(DataFailureKind.Conflict);
        failure.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted,
            "SQL Server rolls the victim back, which is what makes another attempt safe");
        failure.Retry.Should().Be(DataRetryDisposition.RequiresIdempotency);
    }

    [Fact(DisplayName = "SQL Server: a deadlock permits an idempotent caller to try again")]
    public void A_deadlock_permits_an_idempotent_retry()
    {
        // The two halves of the seam, joined: the adapter says what happened, the policy says what it earns.
        DataFailurePolicy.MayRetryIdempotent(
            new ProbeFailure(SqlServerFailureClassifier.Deadlocked()),
            Context,
            [new ProbeClassifier()]).Should().BeTrue();
    }

    [Fact(DisplayName = "SQL Server: a failure it does not recognise stays raw")]
    public void An_unrecognised_failure_is_not_classified()
    {
        new SqlServerFailureClassifier()
            .TryClassify(new InvalidOperationException("something else"), Context, out _)
            .Should().BeFalse("every SQL Server failure but this one is exactly as raw as it was before");
    }

    private sealed class ProbeFailure(DataFailure classification) : Exception
    {
        public DataFailure Classification => classification;
    }

    /// <summary>Stands in for the driver exception the real classifier matches on, carrying the same verdict.</summary>
    private sealed class ProbeClassifier : IDataFailureClassifier
    {
        public bool TryClassify(Exception nativeFailure, DataFailureContext context, out DataFailure failure)
        {
            if (nativeFailure is ProbeFailure probe) { failure = probe.Classification; return true; }
            failure = default!;
            return false;
        }
    }
}
