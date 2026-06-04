namespace Koan.Jobs.Tests.Specs;

/// <summary>Declared linear chains: advance + saga state, stop, branch, and on-failure abort/continue.</summary>
public sealed class ChainSpec
{
    [Fact]
    public async Task linear_chain_advances_and_carries_saga_state()
    {
        await using var host = await JobsTestHost.StartAsync();
        var p = new Pipeline();
        var id = p.Id;

        await p.Job.Submit(Stage.Fetch);
        await host.Drain();

        var saved = await Pipeline.Get(id);
        saved!.Fetched.Should().Be("raw");
        saved.Parsed.Should().Be("raw-parsed");
        saved.Minted.Should().BeTrue();
        saved.Published.Should().BeTrue();
        saved.Trail.Should().Equal(Stage.Fetch, Stage.Parse, Stage.Mint, Stage.Publish);
        (await host.StatusOf<Pipeline>(id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task stop_chain_halts_after_current_step()
    {
        BranchJob.Reset();
        BranchJob.Mode = "stop";
        await using var host = await JobsTestHost.StartAsync();
        var j = new BranchJob();
        var id = j.Id;

        await j.Job.Submit("a");
        await host.Drain();

        (await BranchJob.Get(id))!.Trail.Should().Equal("a");
    }

    [Fact]
    public async Task continue_with_branches_to_an_off_chain_action()
    {
        BranchJob.Reset();
        BranchJob.Mode = "branch";
        await using var host = await JobsTestHost.StartAsync();
        var j = new BranchJob();
        var id = j.Id;

        await j.Job.Submit("a");
        await host.Drain();

        (await BranchJob.Get(id))!.Trail.Should().Equal("a", "z");
    }

    [Fact]
    public async Task on_failure_continue_advances_the_chain()
    {
        await using var host = await JobsTestHost.StartAsync();
        var j = new ContinueChain();
        var id = j.Id;

        await j.Job.Submit(Step.One);
        await host.Drain();

        (await ContinueChain.Get(id))!.TwoRan.Should().BeTrue();
    }

    [Fact]
    public async Task on_failure_abort_stops_the_chain()
    {
        await using var host = await JobsTestHost.StartAsync();
        var j = new AbortChain();
        var id = j.Id;

        await j.Job.Submit(Step.One);
        await host.Drain();

        (await AbortChain.Get(id))!.TwoRan.Should().BeFalse();
    }
}
