using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Jobs;
using Koan.Jobs.TestKit;
using Xunit;

namespace Koan.Jobs.Adapter.Sqlite.Tests.Specs;

/// <summary>Tier-specific durable proofs (the shared behaviors run via <c>SqliteBehaviors</c>): the election picks
/// the data-backed ledger, and the transactional outbox enlists in an ambient transaction.</summary>
public sealed class DurableSqliteSpec
{
    [Fact]
    public async Task election_picks_the_data_backed_ledger()
    {
        await using var host = await JobsHarness.StartSqliteAsync();
        host.Ledger.Should().BeOfType<DataJobLedger>();
    }

    [Fact]
    public async Task submit_in_a_rolled_back_transaction_never_enqueues()
    {
        GreetJob.Reset();
        await using var host = await JobsHarness.StartSqliteAsync();
        var j = new GreetJob { Name = "x" };
        var id = j.Id;

        using (EntityContext.Transaction("rollback"))
        {
            await j.Job.Submit();
            await EntityContext.Rollback();
        }

        await host.Drain();
        GreetJob.Executions.Should().Be(0, "a rolled-back transaction must not enqueue the job");
        (await host.StatusOf<GreetJob>(id)).Should().BeNull();
    }

    [Fact]
    public async Task submit_in_a_committed_transaction_enqueues_once_on_commit()
    {
        GreetJob.Reset();
        await using var host = await JobsHarness.StartSqliteAsync();
        var j = new GreetJob { Name = "y" };
        var id = j.Id;

        using (EntityContext.Transaction("commit"))
        {
            await j.Job.Submit();
            (await host.StatusOf<GreetJob>(id)).Should().BeNull("deferred until commit (outbox)");
            await EntityContext.Commit();
        }

        await host.Drain();
        GreetJob.Executions.Should().Be(1);
        (await host.StatusOf<GreetJob>(id)).Should().Be(JobStatus.Completed);
    }
}
