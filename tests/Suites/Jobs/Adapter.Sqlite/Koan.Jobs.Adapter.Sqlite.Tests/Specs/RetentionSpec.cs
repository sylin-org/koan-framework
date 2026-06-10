using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Jobs;
using Koan.Jobs.TestKit;
using Xunit;

namespace Koan.Jobs.Adapter.Sqlite.Tests.Specs;

/// <summary>
/// JOBS-0005 §19.3 — retention completion on a real store. The archival sweep bounds growth three ways: a window for
/// benign terminals (Completed/Cancelled), a separate window for Failed/Dead (which were retained forever), and a
/// per-work-type count cap for burst protection. All pushed down (predicate + bounded page), not materialize-then-filter.
/// </summary>
public sealed class RetentionSpec
{
    // A real, discovered work-type — the per-work-type cap iterates the job-type registry, so the seeded rows must
    // belong to a registered type (production work-types always are). The window purges are global and type-agnostic.
    private static readonly string WT = typeof(GreetJob).FullName!;

    [Fact]
    public async Task archive_purges_old_completed_and_old_failed_but_keeps_recent_and_active()
    {
        await using var h = await JobsHarness.StartSqliteAsync(o =>
        {
            o.ArchiveAfter = TimeSpan.FromDays(7);
            o.FailedAfter = TimeSpan.FromDays(30);
            o.RetainPerWorkType = 0;
        });
        var now = h.Clock.GetUtcNow();
        await new[]
        {
            Rec("c-old", JobStatus.Completed, now.AddDays(-10)),  // > 7d  → purged
            Rec("c-new", JobStatus.Completed, now.AddDays(-1)),   // < 7d  → kept
            Rec("f-old", JobStatus.Failed, now.AddDays(-40)),     // > 30d → purged (the formerly-forever-retained half)
            Rec("f-new", JobStatus.Failed, now.AddDays(-5)),      // < 30d → kept (replayable)
            Rec("d-old", JobStatus.Dead, now.AddDays(-40)),       // > 30d → purged
            Active("q1"),                                         // active → never purged
        }.Save();

        var purged = await h.Archive();

        purged.Should().Be(3);
        var remaining = (await h.Ledger.Query(new JobQuery(WorkType: WT), default)).Select(r => r.Id).ToHashSet();
        remaining.Should().BeEquivalentTo(new[] { "c-new", "f-new", "q1" });
    }

    [Fact]
    public async Task cap_trims_terminal_to_the_newest_n_per_worktype()
    {
        await using var h = await JobsHarness.StartSqliteAsync(o =>
        {
            o.ArchiveAfter = TimeSpan.Zero;   // windows off — exercise the cap alone
            o.FailedAfter = TimeSpan.Zero;
            o.RetainPerWorkType = 10;
        });
        var now = h.Clock.GetUtcNow();
        // t000 newest (now) … t099 oldest (now-99s)
        var rows = Enumerable.Range(0, 100)
            .Select(i => Rec($"t{i:D3}", JobStatus.Completed, now.AddSeconds(-i)))
            .ToList();
        await rows.Save();

        var purged = await h.Archive();

        purged.Should().Be(90);                                  // keep newest 10, trim 90
        var remaining = await h.Ledger.Query(new JobQuery(WorkType: WT, Status: JobStatus.Completed), default);
        remaining.Should().HaveCount(10);
        remaining.Select(r => r.Id).Should().OnlyContain(id => string.CompareOrdinal(id, "t010") < 0);  // t000..t009
    }

    [Fact]
    public async Task count_active_counts_only_non_terminal_rows()
    {
        await using var h = await JobsHarness.StartSqliteAsync();
        var now = h.Clock.GetUtcNow();
        var rows = new List<JobRecord>();
        for (var i = 0; i < 30; i++) rows.Add(Active($"a{i}"));                  // 30 active (Queued)
        for (var i = 0; i < 70; i++) rows.Add(Rec($"t{i}", JobStatus.Completed, now));  // 70 terminal
        await rows.Save();

        // The §19.4 guardrail samples this per work-type each sweep and warns over the threshold.
        (await h.Ledger.CountActive(WT, default)).Should().Be(30);
    }

    private static JobRecord Rec(string id, JobStatus status, DateTimeOffset settled) => new()
    {
        Id = id,
        WorkType = WT,
        WorkId = id,
        Action = "",
        Status = status,
        VisibleAt = settled,
        FirstSubmittedAt = settled,
        LastSettledAt = settled,
        Exclusive = true,
    };

    private static JobRecord Active(string id) => new()
    {
        Id = id,
        WorkType = WT,
        WorkId = id,
        Action = "",
        Status = JobStatus.Queued,
        VisibleAt = DateTimeOffset.UnixEpoch,
        FirstSubmittedAt = DateTimeOffset.UnixEpoch,
        LastSettledAt = null,
        Exclusive = true,
    };
}
