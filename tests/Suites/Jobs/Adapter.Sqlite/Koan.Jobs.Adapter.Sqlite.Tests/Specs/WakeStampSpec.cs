using AwesomeAssertions;
using Koan.Jobs;
using Koan.Jobs.TestKit;
using Xunit;

namespace Koan.Jobs.Adapter.Sqlite.Tests.Specs;

/// <summary>JOBS-0009 §3 on the durable tier: submissions move the framework-owned wake stamp inside the
/// submission's own transaction, and foreign bumps are visible to peer probes.</summary>
public sealed class WakeStampSpec
{
    private const string StampId = WakeStamp.SingletonId;

    [Fact]
    public async Task durable_submit_moves_the_stamp_once_per_submission()
    {
        await using var host = await JobsHarness.StartSqliteAsync();
        GreetJob.Reset();
        var g = new GreetJob { Name = "woken" };
        await g.Job.Submit("");
        var afterFirst = await WakeStamp.Get(StampId);
        afterFirst.Should().NotBeNull("a durable append bumps the stamp");
        afterFirst!.Version.Should().Be(1);

        var h = new GreetJob { Name = "again" };
        await h.Job.Submit("");
        (await WakeStamp.Get(StampId))!.Version.Should().Be(2);

        await host.Drain();
    }

    [Fact]
    public async Task a_foreign_bump_is_visible_to_the_probe()
    {
        await using var host = await JobsHarness.StartSqliteAsync();
        (await WakeStamp.Get(StampId)).Should().BeNull();

        // Another node's shape: it owns the row and writes its own version.
        await WakeStamp.Upsert(new WakeStamp { Id = StampId, Version = 41, UpdatedAt = DateTimeOffset.UtcNow });
        (await WakeStamp.Get(StampId))!.Version.Should().Be(41);
    }
}
