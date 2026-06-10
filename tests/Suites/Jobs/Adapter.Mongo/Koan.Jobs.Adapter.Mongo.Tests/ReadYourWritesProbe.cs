using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Jobs.TestKit;
using Xunit;

namespace Koan.Jobs.Adapter.Mongo.Tests;

/// <summary>
/// Diagnostic (not a Jobs spec): isolates the read-after-write question the Jobs ledger surfaced. The orchestrator
/// writes a follow-on then re-discovers it via a query in the same drain pass — which only works if the store gives
/// read-your-writes. This probes that directly through the data layer: save a row, immediately query for it by a
/// field, count misses. A non-zero miss count on Mongo confirms the gap is a data-layer consistency property, not a
/// Jobs bug. (The Jobs flakiness should disappear once this is green.)
/// </summary>
public sealed class ReadYourWritesProbe : IClassFixture<MongoJobsFixture>
{
    private readonly MongoJobsFixture _fx;
    public ReadYourWritesProbe(MongoJobsFixture fx) => _fx = fx;

    public sealed class Probe : Entity<Probe> { public int N { get; set; } }

    [Fact]
    public async Task immediate_query_sees_the_just_saved_row()
    {
        await using var host = await JobsHarness.StartWithSettingsAsync(_fx.Settings);
        try { await Probe.RemoveAll(); } catch { /* fresh */ }

        var misses = 0;
        for (var i = 0; i < 200; i++)
        {
            await new Probe { N = i }.Save();                 // write
            var hits = await Probe.Query(p => p.N == i);      // immediate read-by-field, like the claim's query
            if (hits.Count == 0) misses++;
        }

        misses.Should().Be(0, "every immediate query should see its just-saved row (read-your-writes)");
    }

    public sealed class Phased : Entity<Phased> { public string Phase { get; set; } = ""; }

    [Fact]
    public async Task immediate_query_reflects_the_just_updated_value()
    {
        // Mirrors the claim's read of a just-SETTLED job: an existing row's field is updated, then immediately queried
        // for the OLD value. The settle is Status Running->Completed; the next claim queries Status==Queued/Running.
        await using var host = await JobsHarness.StartWithSettingsAsync(_fx.Settings);
        try { await Phased.RemoveAll(); } catch { /* fresh */ }

        var stale = 0;
        for (var i = 0; i < 200; i++)
        {
            var p = new Phased { Id = $"p{i}", Phase = "running" };
            await p.Save();
            p.Phase = "done";
            await p.Save();                                              // UPDATE running -> done
            var hits = await Phased.Query(x => x.Phase == "running");    // query the OLD value, expecting it gone
            if (hits.Any(h => h.Id == p.Id)) stale++;                    // still "running" => update not visible
        }

        stale.Should().Be(0, "an immediate query must reflect the just-updated value (read-your-writes for updates)");
    }
}
