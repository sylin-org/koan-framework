using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Koan.Jobs.TestKit;

namespace Koan.Jobs.Tests;

public sealed class SubmissionSpec
{
    [Fact]
    public async Task Async_submission_is_single_pass_backpressured_and_preserves_multiplicity()
    {
        GreetJob.Reset();
        await using var host = await JobsHarness.StartInMemoryAsync();
        var first = new GreetJob { Name = "first" };
        var second = new GreetJob { Name = "second" };
        var enumerations = 0;

        async IAsyncEnumerable<GreetJob> Source()
        {
            enumerations++;
            yield return first;
            (await host.JobFor<GreetJob>(first.Id)).Should().NotBeNull(
                "the source must not be asked for item two until item one is ledger-accepted");
            yield return second;
            yield return first;
            await Task.CompletedTask;
        }

        var submission = await Source().Submit();

        enumerations.Should().Be(1);
        submission.Enumerated.Should().Be(3);
        submission.Accepted.Should().Be(3);
        submission.Submitted.Should().Be(3);
        submission.Coalesced.Should().Be(0);
        submission.SourceCompleted.Should().BeTrue();
        (await host.Coordinator.WhereAsync(
                new JobQuery(WorkType: typeof(GreetJob).FullName!, WorkId: first.Id),
                default))
            .Should().HaveCount(2, "yielding the same non-idempotent Entity twice is two deliberate submissions");

        await host.Drain();
        GreetJob.Executions.Should().Be(3);
    }

    [Fact]
    public async Task Submission_reports_declared_coalescing_without_hiding_source_multiplicity()
    {
        DedupeJob.Reset();
        await using var host = await JobsHarness.StartInMemoryAsync();
        var source = new[]
        {
            new DedupeJob { Key = "same" },
            new DedupeJob { Key = "same" },
            new DedupeJob { Key = "different" }
        };

        var submission = await source.Submit();

        submission.Enumerated.Should().Be(3);
        submission.Accepted.Should().Be(3);
        submission.Submitted.Should().Be(2);
        submission.Coalesced.Should().Be(1);
        submission.Failed.Should().Be(0);
        await host.Drain();
        DedupeJob.Executions.Should().Be(2);
    }

    [Fact]
    public async Task Source_failure_carries_the_confirmed_ledger_prefix()
    {
        await using var host = await JobsHarness.StartInMemoryAsync();

        async IAsyncEnumerable<GreetJob> Source()
        {
            yield return new GreetJob { Name = "accepted-1" };
            yield return new GreetJob { Name = "accepted-2" };
            await Task.Yield();
            throw new InvalidOperationException("source failed deliberately");
        }

        var failure = await Assert.ThrowsAsync<JobSubmissionException>(() => Source().Submit());

        failure.Failure.Should().Be(JobSubmissionException.FailureKind.SourceFailed);
        failure.Submission.Enumerated.Should().Be(2);
        failure.Submission.Accepted.Should().Be(2);
        failure.Submission.Submitted.Should().Be(2);
        failure.Submission.Failed.Should().Be(0);
        failure.Submission.SourceCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Item_acceptance_failure_is_distinct_and_carries_the_confirmed_prefix()
    {
        await using var host = await JobsHarness.StartInMemoryAsync();

        async IAsyncEnumerable<SubmissionGateJob> Source()
        {
            yield return new SubmissionGateJob();
            yield return new SubmissionGateJob { Reject = true };
            await Task.CompletedTask;
        }

        var failure = await Assert.ThrowsAsync<JobSubmissionException>(() => Source().Submit());

        failure.Failure.Should().Be(JobSubmissionException.FailureKind.SubmissionFailed);
        failure.InnerException.Should().BeOfType<InvalidOperationException>();
        failure.Submission.Enumerated.Should().Be(2);
        failure.Submission.Accepted.Should().Be(1);
        failure.Submission.Failed.Should().Be(1);
        failure.Submission.SourceCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Cancellation_remains_an_operation_canceled_exception_with_the_confirmed_prefix()
    {
        await using var host = await JobsHarness.StartInMemoryAsync();
        using var cts = new CancellationTokenSource();

        async IAsyncEnumerable<GreetJob> Source(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new GreetJob { Name = "accepted" };
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            await Task.CompletedTask;
        }

        var failure = await Assert.ThrowsAsync<JobSubmissionCanceledException>(
            () => Source(cts.Token).Submit(ct: cts.Token));

        failure.Should().BeAssignableTo<OperationCanceledException>();
        failure.Submission.Enumerated.Should().Be(1);
        failure.Submission.Accepted.Should().Be(1);
        failure.Submission.Submitted.Should().Be(1);
        failure.Submission.SourceCompleted.Should().BeFalse();
    }
}
