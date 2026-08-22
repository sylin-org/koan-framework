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
/// filter in memory) blows the coarse regression ceilings here. Seeds ~100k rows directly into the ledger (bypassing
/// the orchestrator) so the assertions are about the read path alone. These ceilings are not latency SLAs.
/// </summary>
[Trait("category", "scale")]
public sealed class HighVolumeScanShapeSpec(ITestOutputHelper output)
{
    private const string WorkType = "bulk-work";

    /// <summary>Rows the per-row baseline writes one at a time, enough to average out a slow individual write.</summary>
    private const int BaselineRows = 200;

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
        // LIMIT pushed: the claim reads a bounded ordered window, not all 100k queued. Keep shared-runner headroom;
        // this is a gross fallback sentinel, not a production latency SLA.
        sw.ElapsedMilliseconds.Should().BeLessThan(3000);
    }

    /// <summary>
    /// A bulk save must batch. The claim is about shape, not speed, so it is asserted as one: what a row costs
    /// through the bulk path, against what the same row costs saved on its own, measured in the same run.
    ///
    /// <para>This used to assert a wall clock — under ten seconds — and failed on a cold run while passing on a
    /// warm one, which reports a defect where there is none and costs whoever next reads a red suite (PMC-044).
    /// A ratio has no such problem: JIT and page cache land on both measurements, so they cancel. Measured
    /// 2026-08-21 on a loaded machine in Debug: 0.25ms per row batched against 7.43ms per row individually, a
    /// factor of thirty. A bulk path that had degenerated into per-row writes would score about one, because it
    /// would be doing the very thing the baseline does. The bar is five, which is six times below what a
    /// working implementation measures and five times above a broken one.</para>
    /// </summary>
    [Fact]
    public async Task bulk_save_of_a_large_batch_is_a_single_batched_write()
    {
        await using var h = await JobsHarness.StartSqliteAsync();
        var now = h.Clock.GetUtcNow();
        var records = Enumerable.Range(0, 50_000)
            .Select(i => Make($"b{i}", JobStatus.Completed, now, now))
            .ToList();

        // Batched first, while the process is coldest, and the per-row baseline second, once it is warm. That
        // is the hard direction for the comparison below: anything JIT or page cache contributes now counts
        // against the batched path and for the baseline.
        var batched = Stopwatch.StartNew();
        await records.Save();   // IEnumerable<T>.Save() → one UpsertMany → one batched transaction (not 50k fsyncs)
        batched.Stop();

        var perRow = Stopwatch.StartNew();
        for (var i = 0; i < BaselineRows; i++) await Make($"s{i}", JobStatus.Completed, now, now).Save();
        perRow.Stop();

        (await h.Ledger.Query(new JobQuery(WorkType: WorkType, Status: JobStatus.Completed), default))
            .Should().HaveCount(50_000 + BaselineRows);

        var batchedCost = batched.Elapsed.TotalMilliseconds / records.Count;
        var perRowCost = perRow.Elapsed.TotalMilliseconds / BaselineRows;
        output.WriteLine(
            $"batched {batchedCost:F3}ms/row over {records.Count} rows; " +
            $"individual {perRowCost:F3}ms/row over {BaselineRows} rows; ratio {perRowCost / batchedCost:F1}x");

        (perRowCost / batchedCost).Should().BeGreaterThan(5,
            "a batched write amortizes the commit across the batch, so a row costs a fraction of what it costs "
            + "written on its own; a ratio near one is a bulk path that has degenerated into per-row writes");
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
