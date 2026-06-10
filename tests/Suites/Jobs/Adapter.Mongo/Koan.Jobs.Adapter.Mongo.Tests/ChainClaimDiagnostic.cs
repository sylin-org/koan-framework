using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Jobs;
using Koan.Jobs.TestKit;
using Xunit;

namespace Koan.Jobs.Adapter.Mongo.Tests;

/// <summary>
/// Diagnostic for the chain-claim flake: reproduces the exact hop the orchestrator makes — settle the predecessor
/// (Running→Completed) and append a same-WorkId successor, then claim — and measures each read the claim depends on
/// INDEPENDENTLY, so the failing step is isolated rather than inferred:
///   • staleRunning   — does Query(Status==Running) still return the just-Completed predecessor? (would block the successor)
///   • scanMissedSucc — does the simple Queued query miss the just-appended successor?
///   • pagedMissedSucc — does the SORTED+PAGINATED claim scan (the real one) miss it?
///   • claimMissed    — does ClaimNext fail to return the successor end-to-end?
/// </summary>
public sealed class ChainClaimDiagnostic : IClassFixture<MongoJobsFixture>
{
    private readonly MongoJobsFixture _fx;
    public ChainClaimDiagnostic(MongoJobsFixture fx) => _fx = fx;

    [Fact]
    public async Task chain_successor_is_claimable_immediately_after_predecessor_settles()
    {
        await using var h = await JobsHarness.StartWithSettingsAsync(_fx.Settings);
        var wt = typeof(GreetJob).FullName!;
        var now = h.Clock.GetUtcNow();
        var pagedScan = QueryDefinition.All
            .WithSort(Koan.Data.Core.Sorting.SortBuilder<JobRecord>.Build(s => s.OrderBy(r => r.VisibleAt).ThenBy(r => r.FirstSubmittedAt)))
            .WithPagination(1, 64);

        int staleRunning = 0, scanMissedSucc = 0, pagedMissedSucc = 0, claimMissed = 0;
        for (var i = 0; i < 100; i++)
        {
            await JobRecord.RemoveAll(RemoveStrategy.Safe);
            var wid = $"w{i}";
            var pred = Rec($"pred{i}", wt, wid, JobStatus.Running, now); pred.Owner = "o";
            await pred.Save();
            // settle predecessor + append same-WorkId successor — exactly the orchestrator's chain advance
            pred.Status = JobStatus.Completed; pred.Owner = null; pred.LastSettledAt = now;
            await pred.Save();
            var succ = Rec($"succ{i}", wt, wid, JobStatus.Queued, now);
            await succ.Save();

            if ((await JobRecord.Query(r => r.Status == JobStatus.Running)).Any(r => r.Id == pred.Id)) staleRunning++;
            if (!(await JobRecord.Query(r => r.Status == JobStatus.Queued)).Any(r => r.Id == succ.Id)) scanMissedSucc++;
            if (!(await JobRecord.Query(r => r.Status == JobStatus.Queued && r.VisibleAt <= now && r.CancelRequestedAt == null, pagedScan)).Any(r => r.Id == succ.Id)) pagedMissedSucc++;
            var claimed = await h.Ledger.ClaimNext("o", now, now.AddMinutes(1), Array.Empty<string>(), default);
            if (claimed?.Id != succ.Id) claimMissed++;
        }

        claimMissed.Should().Be(0,
            $"staleRunning={staleRunning}, scanMissedSucc={scanMissedSucc}, pagedMissedSucc={pagedMissedSucc}, claimMissed={claimMissed}");
    }

    [Fact]
    public async Task chain_hop_under_per_spec_host_churn()
    {
        // Same hop, but a FRESH host (new MongoClient + DI) each iteration — mirrors the suite's per-spec churn against
        // one shared container, the only thing the single-host diagnostic doesn't exercise.
        var wt = typeof(GreetJob).FullName!;
        var claimMissed = 0;
        for (var i = 0; i < 25; i++)
        {
            await using var h = await JobsHarness.StartWithSettingsAsync(_fx.Settings);
            var now = h.Clock.GetUtcNow();
            var wid = $"w{i}";
            var pred = Rec($"pred{i}", wt, wid, JobStatus.Running, now); pred.Owner = "o"; await pred.Save();
            pred.Status = JobStatus.Completed; pred.Owner = null; pred.LastSettledAt = now; await pred.Save();
            await Rec($"succ{i}", wt, wid, JobStatus.Queued, now).Save();
            var claimed = await h.Ledger.ClaimNext("o", now, now.AddMinutes(1), Array.Empty<string>(), default);
            if (claimed?.WorkId != wid || claimed.Action != "") claimMissed++;
        }
        claimMissed.Should().Be(0, $"per-host-cycle chain hop missed {claimMissed}/25");
    }

    private static JobRecord Rec(string id, string wt, string wid, JobStatus status, System.DateTimeOffset at) => new()
    {
        Id = id, WorkType = wt, WorkId = wid, Action = "", Status = status,
        VisibleAt = at, FirstSubmittedAt = at, LastSettledAt = status >= JobStatus.Completed ? at : null, Exclusive = true,
    };
}
