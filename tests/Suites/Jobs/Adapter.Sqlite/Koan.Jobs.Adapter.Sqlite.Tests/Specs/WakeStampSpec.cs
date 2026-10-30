using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Jobs;
using Koan.Jobs.TestKit;
using Xunit;

namespace Koan.Jobs.Adapter.Sqlite.Tests.Specs;

/// <summary>JOBS-0009 §3 and JOBS-0010 on the durable tier: a submission moves one shard of the framework-owned wake
/// stamp inside its own transaction, submitting transactions spread across the shards instead of meeting on one row,
/// a rolled-back submission moves nothing, and foreign bumps are visible to peer probes.</summary>
public sealed class WakeStampSpec
{
    [Fact]
    public async Task durable_submit_moves_the_stamp()
    {
        await using var host = await JobsHarness.StartSqliteAsync();
        GreetJob.Reset();
        var before = await WakeStamp.ReadVersion(default);

        await new GreetJob { Name = "woken" }.Job.Submit("");
        var afterFirst = await WakeStamp.ReadVersion(default);
        afterFirst.Should().NotBe(before, "a durable append bumps the stamp");

        await new GreetJob { Name = "again" }.Job.Submit("");
        (await WakeStamp.ReadVersion(default)).Should().NotBe(afterFirst);

        await host.Drain();
    }

    [Fact]
    public async Task submitting_transactions_spread_their_bumps_across_shards()
    {
        // JOBS-0010: every submit used to update one row inside its transaction, so concurrent submitting transactions
        // serialized on it until commit. A bump now writes one shard at random, without reading it first.
        await using var host = await JobsHarness.StartSqliteAsync();
        GreetJob.Reset();

        await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Task.Run(async () =>
        {
            using (EntityContext.Transaction($"submit-{i}"))
            {
                await new GreetJob { Name = $"t{i}" }.Job.Submit("");
                await EntityContext.Commit();
            }
        })));

        (await WakeStamp.All()).Select(s => s.Id).Distinct().Count().Should().BeGreaterThan(1,
            "thirty-two submitting transactions do not all write one row");
        (await JobRecord.Query(r => r.WorkType == typeof(GreetJob).FullName!)).Should().HaveCount(32);
        await host.Drain();
        GreetJob.Executions.Should().Be(32);
    }

    [Fact]
    public async Task a_rolled_back_submission_moves_no_stamp()
    {
        await using var host = await JobsHarness.StartSqliteAsync();
        var before = await WakeStamp.ReadVersion(default);

        using (EntityContext.Transaction("rolled-back-submit"))
        {
            await new GreetJob { Name = "never" }.Job.Submit("");
            await EntityContext.Rollback();
        }

        (await WakeStamp.ReadVersion(default)).Should().Be(before);
    }

    [Fact]
    public async Task a_foreign_bump_is_visible_to_the_probe()
    {
        await using var host = await JobsHarness.StartSqliteAsync();
        var before = await WakeStamp.ReadVersion(default);

        // Another node's shape: it writes one shard with its own version.
        await WakeStamp.Upsert(new WakeStamp { Id = WakeStamp.ShardPrefix + 3, Version = 41, UpdatedAt = DateTimeOffset.UtcNow });
        (await WakeStamp.ReadVersion(default)).Should().NotBe(before);
    }
}
