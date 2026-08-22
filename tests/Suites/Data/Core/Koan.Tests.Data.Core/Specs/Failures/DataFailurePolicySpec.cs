using AwesomeAssertions;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core.Failures;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Failures;

/// <summary>
/// The consumer half of the failure-classification seam. What matters here is what it refuses: a caller asking
/// "may I run that again?" is one wrong answer away from running a committed operation twice.
/// </summary>
public sealed class DataFailurePolicySpec
{
    private static readonly DataFailureContext Context = new(
        "probe", "Default", "probe.operation", DataOperationEffect.Write,
        Dispatched: true, CommitBoundaryCrossed: true);

    private static readonly Exception Failure = new InvalidOperationException("native");

    [Fact]
    public void An_unrecognised_failure_is_never_retried()
    {
        DataFailurePolicy.MayRetryIdempotent(Failure, Context, null).Should().BeFalse();
        DataFailurePolicy.MayRetryIdempotent(Failure, Context, [new Classifier(null)]).Should().BeFalse(
            "a classifier that does not recognise a failure leaves it exactly as raw as it was");
    }

    [Fact]
    public void A_rolled_back_conflict_may_be_retried_by_an_idempotent_caller()
    {
        var classified = new DataFailure(
            "probe:conflict", DataFailureKind.Conflict, DataCommitOutcome.NotCommitted,
            DataRetryDisposition.RequiresIdempotency, DataReplayDisposition.RequiresIdempotency);

        DataFailurePolicy.MayRetryIdempotent(Failure, Context, [new Classifier(classified)]).Should().BeTrue();
    }

    /// <summary>
    /// The one that matters. A retry disposition is permission, not proof: if the store cannot say the
    /// operation failed to commit, repeating it is how one write becomes two.
    /// </summary>
    [Fact]
    public void An_operation_that_may_have_committed_is_never_retried()
    {
        var committed = new DataFailure(
            "probe:committed", DataFailureKind.Timeout, DataCommitOutcome.Committed,
            DataRetryDisposition.RequiresIdempotency, DataReplayDisposition.Never);
        var unknown = new DataFailure(
            "probe:unknown", DataFailureKind.Timeout, DataCommitOutcome.Unknown,
            DataRetryDisposition.RequiresIdempotency, DataReplayDisposition.Never);

        DataFailurePolicy.MayRetryIdempotent(Failure, Context, [new Classifier(committed)]).Should().BeFalse();
        DataFailurePolicy.MayRetryIdempotent(Failure, Context, [new Classifier(unknown)]).Should().BeFalse();
    }

    [Fact]
    public void Before_dispatch_only_means_only_before_dispatch()
    {
        var reached = new DataFailure(
            "probe:reached", DataFailureKind.Conflict, DataCommitOutcome.NotCommitted,
            DataRetryDisposition.BeforeDispatchOnly, DataReplayDisposition.BeforeDispatchOnly);
        var never = new DataFailure(
            "probe:never", DataFailureKind.Conflict, DataCommitOutcome.NotDispatched,
            DataRetryDisposition.BeforeDispatchOnly, DataReplayDisposition.BeforeDispatchOnly);

        DataFailurePolicy.MayRetryIdempotent(Failure, Context, [new Classifier(reached)]).Should().BeFalse(
            "the operation reached the store, which is exactly what this disposition excludes");
        DataFailurePolicy.MayRetryIdempotent(Failure, Context, [new Classifier(never)]).Should().BeTrue();
    }

    [Fact]
    public void The_first_classifier_that_recognises_the_failure_answers()
    {
        var first = new DataFailure(
            "probe:first", DataFailureKind.Conflict, DataCommitOutcome.NotCommitted,
            DataRetryDisposition.Never, DataReplayDisposition.Never);
        var second = new DataFailure(
            "probe:second", DataFailureKind.Conflict, DataCommitOutcome.NotCommitted,
            DataRetryDisposition.RequiresIdempotency, DataReplayDisposition.RequiresIdempotency);

        DataFailurePolicy.TryClassify(Failure, Context, [new Classifier(null), new Classifier(first), new Classifier(second)], out var chosen)
            .Should().BeTrue();
        chosen.Code.Should().Be("probe:first");
    }

    private sealed class Classifier(DataFailure? answer) : IDataFailureClassifier
    {
        public bool TryClassify(Exception nativeFailure, DataFailureContext context, out DataFailure failure)
        {
            failure = answer!;
            return answer is not null;
        }
    }
}
