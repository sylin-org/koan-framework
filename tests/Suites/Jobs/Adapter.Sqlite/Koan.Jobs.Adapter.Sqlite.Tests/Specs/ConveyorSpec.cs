using AwesomeAssertions;
using Koan.Jobs;
using Koan.Jobs.TestKit;
using Xunit;

namespace Koan.Jobs.Adapter.Sqlite.Tests.Specs;

/// <summary>
/// JOBS-0005 §19.4 — the Koan-native bulk answer. A cursor-conveyor drains a large source by re-queuing itself
/// window-by-window (<c>ctx.ContinueWith</c> on a mutable-offset entity, conditional auto-save advancing the cursor),
/// so the ledger holds one row per <em>window</em>, never a row per item. The proof: 5000 items drain through 50
/// ledger rows with full coverage — the anti-pattern would have been 5000 rows.
/// </summary>
public sealed class ConveyorSpec
{
    [Fact]
    public async Task cursor_conveyor_drains_a_large_source_with_a_bounded_ledger()
    {
        Conveyor.Reset();
        await using var h = await JobsHarness.StartSqliteAsync();

        await new Conveyor { Total = 5000, Window = 100 }.Job.Submit(Conveyor.Pull);   // ONE ledger row to start
        await h.Drain();

        Conveyor.ItemsProcessed.Should().Be(5000);     // every item covered
        Conveyor.WindowsRun.Should().Be(50);           // 50 windows ran, not 5000 jobs

        var rows = await h.Ledger.Query(new JobQuery(WorkType: typeof(Conveyor).FullName!), default);
        rows.Should().HaveCount(50);                    // one ledger row per window — bounded, not 5000
        rows.Should().OnlyContain(r => r.Status == JobStatus.Completed);
    }
}
