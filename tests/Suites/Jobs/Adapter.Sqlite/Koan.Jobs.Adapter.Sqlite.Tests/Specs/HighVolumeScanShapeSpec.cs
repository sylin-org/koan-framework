using System.Diagnostics;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Jobs;
using Koan.Jobs.TestKit;
using Xunit;

namespace Koan.Jobs.Adapter.Sqlite.Tests.Specs;

/// <summary>
/// JOBS-0005 §19 — Tier-0 push-down proven on a real indexed store at volume. The dashboard query and the claim
/// loop must stay O(matches)/O(batch), not O(ledger): a pre-push-down ledger (materialize the whole work-type, then
/// filter in memory) blows both the absolute ceiling and the sublinearity ratio here. Seeds ~100k rows directly into
/// the ledger (bypassing the orchestrator) so the assertions are about the read path alone.
/// </summary>
[Trait("category", "scale")]
public sealed class HighVolumeScanShapeSpec
{
    private const string WorkType = "bulk-work";

    [Fact]
    public async Task dashboard_query_returns_only_matches_at_volume()
    {
        await using var h = await JobsHarness.StartSqliteAsync();
        // 100k Completed noise + 5 Queued needles, one work-type.
        await SeedCompletedAsync(100_000, h.Clock.GetUtcNow());
        await SeedQueuedAsync(5, h.Clock.GetUtcNow());

        var sw = Stopwatch.StartNew();
        var active = await h.Ledger.Query(new JobQuery(WorkType: WorkType, Status: JobStatus.Queued), default);
        sw.Stop();

        active.Should().HaveCount(5);                                  // the predicate is applied — only the needles
        active.Should().OnlyContain(r => r.Status == JobStatus.Queued);
        // Push-down: a SQL-side filter returns 5 rows in ms; materializing 100k JobRecords in memory takes seconds.
        sw.ElapsedMilliseconds.Should().BeLessThan(2000);
    }

    [Fact]
    public async Task claim_finds_the_fifo_head_among_a_large_backlog_in_bounded_time()
    {
        await using var h = await JobsHarness.StartSqliteAsync();
        var now = h.Clock.GetUtcNow();
        await SeedQueuedAsync(100_000, now);   // all visible (seeded in the past), ordered by FirstSubmittedAt

        var sw = Stopwatch.StartNew();
        var claimed = await h.Ledger.ClaimNext("owner-1", now, now.AddMinutes(1), Array.Empty<string>(), default);
        sw.Stop();

        claimed.Should().NotBeNull();
        claimed!.WorkId.Should().Be("q0");              // seq 0 = earliest FirstSubmittedAt → ORDER pushed (true FIFO head)
        claimed.Status.Should().Be(JobStatus.Running);  // CAS claimed
        // LIMIT pushed: the claim reads a bounded ordered window, not all 100k queued.
        sw.ElapsedMilliseconds.Should().BeLessThan(1500);
    }

    [Fact]
    public async Task bulk_save_of_a_large_batch_is_a_single_batched_write()
    {
        await using var h = await JobsHarness.StartSqliteAsync();
        var now = h.Clock.GetUtcNow();
        var records = Enumerable.Range(0, 50_000)
            .Select(i => Make($"b{i}", JobStatus.Completed, now, now))
            .ToList();

        var sw = Stopwatch.StartNew();
        await records.Save();   // IEnumerable<T>.Save() → one UpsertMany → one batched transaction (not 50k fsyncs)
        sw.Stop();

        (await h.Ledger.Query(new JobQuery(WorkType: WorkType, Status: JobStatus.Completed), default))
            .Should().HaveCount(50_000);
        // Batched: 50k rows commit in one transaction (seconds). Per-row autocommit would be ~50k fsyncs (minutes).
        sw.ElapsedMilliseconds.Should().BeLessThan(10_000);
    }

    private static Task SeedCompletedAsync(int count, DateTimeOffset baseTime, int idOffset = 0)
        => SeedAsync(count, idOffset, i =>
        {
            var t = baseTime.AddSeconds(-i - 1);
            return Make($"c{idOffset + i}", JobStatus.Completed, t, settled: t);
        });

    private static Task SeedQueuedAsync(int count, DateTimeOffset baseTime)
        => SeedAsync(count, 0, i =>
        {
            // all in the past (visible) and strictly ordered so the FIFO head is deterministic (seq 0)
            var t = baseTime.AddDays(-1).AddMilliseconds(i);
            return Make($"q{i}", JobStatus.Queued, t, settled: null);
        });

    private static async Task SeedAsync(int count, int idOffset, Func<int, JobRecord> make)
    {
        var batch = new List<JobRecord>(5_000);
        for (var i = 0; i < count; i++)
        {
            batch.Add(make(i));
            if (batch.Count >= 5_000) { await JobRecord.UpsertMany(batch); batch.Clear(); }
        }
        if (batch.Count > 0) await JobRecord.UpsertMany(batch);
    }

    private static JobRecord Make(string id, JobStatus status, DateTimeOffset submitted, DateTimeOffset? settled) => new()
    {
        Id = id,
        WorkType = WorkType,
        WorkId = id,
        Action = "",
        Status = status,
        Lane = "default", // JobTypeBinding's production invariant for the empty single-action token
        VisibleAt = submitted,
        FirstSubmittedAt = submitted,
        LastSettledAt = settled,
        Exclusive = true,
    };
}
