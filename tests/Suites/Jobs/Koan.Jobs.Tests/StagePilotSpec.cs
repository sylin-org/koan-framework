namespace Koan.Jobs.Tests;

/// <summary>
/// Validates the <see cref="JobStagePilot"/> harness: one stage at a time, signals captured, chain advance
/// assertable, entity mutations visible. These facts are the framework-side proof that consumer tests can
/// un-skip their stage-handler integration specs using this pattern (ARCH-0079).
/// </summary>
public sealed class StagePilotSpec
{
    [Fact]
    public async Task chain_stage_settle_and_successor_are_assertable()
    {
        await using var host = await JobsHarness.StartInMemoryAsync();
        var pipeline = new Pipeline();
        var id = pipeline.Id;

        var result = await host.Pilot.RunStageAsync(pipeline, Stage.Fetch);

        // Signal: no explicit verb called -- default chain advance.
        result.Run.Signal.Should().Be(JobSignal.None);
        result.Run.Action.Should().Be(Stage.Fetch);

        // Settle: Fetch ran to completion.
        result.Settled.Status.Should().Be(JobStatus.Completed);

        // Chain: Parse was appended as the successor, not yet run.
        result.Successor.Should().NotBeNull("the chain must advance to Parse after Fetch");
        result.Successor!.Action.Should().Be(Stage.Parse);
        result.Successor.Status.Should().Be(JobStatus.Queued);

        // Auto-save: the handler's entity mutation was persisted by the conditional auto-save.
        var saved = await Pipeline.Get(id);
        saved!.Fetched.Should().Be("raw");
        saved.Trail.Should().Equal(Stage.Fetch);

        // Parse stage has NOT been executed -- the pilot ran exactly one stage.
        saved.Parsed.Should().BeNull();
    }

    [Fact]
    public async Task stop_chain_signal_is_captured_and_no_successor_is_appended()
    {
        BranchJob.Reset();
        BranchJob.Mode = "stop";
        await using var host = await JobsHarness.StartInMemoryAsync();

        var job = new BranchJob();
        var result = await host.Pilot.RunStageAsync(job, "a");

        result.Run.Signal.Should().Be(JobSignal.StopChain);
        result.Settled.Status.Should().Be(JobStatus.Completed);
        result.Successor.Should().BeNull("StopChain must suppress chain advancement");
    }

    [Fact]
    public async Task continue_with_signal_is_captured_and_branches_to_named_action()
    {
        BranchJob.Reset();
        BranchJob.Mode = "branch";
        await using var host = await JobsHarness.StartInMemoryAsync();

        var job = new BranchJob();
        var result = await host.Pilot.RunStageAsync(job, "a");

        result.Run.Signal.Should().Be(JobSignal.ContinueWith);
        result.Run.NextAction.Should().Be("z");
        result.Settled.Status.Should().Be(JobStatus.Completed);
        result.Successor.Should().NotBeNull("ContinueWith must append the branch action");
        result.Successor!.Action.Should().Be("z");
    }

    [Fact]
    public async Task reschedule_signal_is_captured_with_defer_time()
    {
        RescheduleJob.Reset();
        RescheduleJob.RescheduleUntil = 1;
        await using var host = await JobsHarness.StartInMemoryAsync();

        var job = new RescheduleJob();
        var result = await host.Pilot.RunStageAsync(job, "");

        result.Run.Signal.Should().Be(JobSignal.Reschedule);
        result.Run.DeferUntil.Should().NotBeNull();
        result.Settled.Status.Should().Be(JobStatus.Queued, "Reschedule re-queues the same stage");
        result.Successor.Should().BeNull("a rescheduled job has no successor record");
    }
}
