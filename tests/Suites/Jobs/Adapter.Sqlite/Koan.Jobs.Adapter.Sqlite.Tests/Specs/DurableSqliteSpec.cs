using Koan.Jobs.Adapter.Sqlite.Tests.Support;

namespace Koan.Jobs.Adapter.Sqlite.Tests.Specs;

/// <summary>Proves the data-backed ledger works on a real SQLite store: election, durable persistence + claim
/// (query translation), chain advance, and lease reclaim. ARCH-0079 — through real AddKoan() discovery.</summary>
public sealed class DurableSqliteSpec
{
    [Fact]
    public async Task election_picks_the_data_backed_ledger_when_a_durable_adapter_is_present()
    {
        await using var host = await DurableHost.StartAsync();
        host.Ledger.Should().BeOfType<DataJobLedger>();
    }

    [Fact]
    public async Task single_action_persists_and_completes_over_sqlite()
    {
        DurableJob.Reset();
        await using var host = await DurableHost.StartAsync();
        var j = new DurableJob { Input = "x" };
        var id = j.Id;

        await j.Job.Submit();
        await host.Drain();

        DurableJob.Executions.Should().Be(1);
        (await DurableJob.Get(id))!.Output.Should().Be("x-done");
        (await host.StatusOf<DurableJob>(id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task chain_advances_over_sqlite()
    {
        await using var host = await DurableHost.StartAsync();
        var p = new DurablePipeline();
        var id = p.Id;

        await p.Job.Submit(DStage.A);
        await host.Drain();

        (await DurablePipeline.Get(id))!.Trail.Should().Equal(DStage.A, DStage.B, DStage.C);
        (await host.StatusOf<DurablePipeline>(id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task reaper_reclaims_a_lapsed_lease_over_sqlite()
    {
        DurableJob.Reset();
        await using var host = await DurableHost.StartAsync();

        var g = new DurableJob { Input = "r" };
        var id = g.Id;
        await DurableJob.Upsert(g);

        var now = host.Clock.GetUtcNow();
        await host.Ledger.Append(new JobRecord
        {
            WorkType = typeof(DurableJob).FullName!,
            WorkId = id,
            Action = "",
            Status = JobStatus.Running,
            Attempt = 1,
            Lane = "default",
            FirstSubmittedAt = now,
            VisibleAt = now,
            LeaseUntil = now - TimeSpan.FromMinutes(1),
            Owner = "dead-worker",
        }, default);

        await host.Reap();
        await host.Drain();

        DurableJob.Executions.Should().Be(1);
        (await host.StatusOf<DurableJob>(id)).Should().Be(JobStatus.Completed);
    }
}
