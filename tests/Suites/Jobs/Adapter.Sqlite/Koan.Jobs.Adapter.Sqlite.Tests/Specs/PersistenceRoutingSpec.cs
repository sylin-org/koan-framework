using System.Collections.Concurrent;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Jobs;
using Koan.Jobs.TestKit;
using Xunit;

namespace Koan.Jobs.Adapter.Sqlite.Tests.Specs;

/// <summary>An ephemeral job type — its ledger state stays in the volatile in-memory ledger even with a durable adapter.</summary>
[JobPersistence(JobPersistenceMode.InMemory)]
public sealed class EphemeralJob : Entity<EphemeralJob>, IKoanJob<EphemeralJob>
{
    public string Note { get; set; } = "";
    public static readonly ConcurrentBag<string> Ran = new();
    public static Task Execute(EphemeralJob job, JobContext ctx, CancellationToken ct) { Ran.Add(job.Id); return Task.CompletedTask; }
    public static void Reset() => Ran.Clear();
}

/// <summary>A durable job type — its ledger state is forced into the data-backed ledger.</summary>
[JobPersistence(JobPersistenceMode.DataStore)]
public sealed class DurableJob : Entity<DurableJob>, IKoanJob<DurableJob>
{
    public string Note { get; set; } = "";
    public static readonly ConcurrentBag<string> Ran = new();
    public static Task Execute(DurableJob job, JobContext ctx, CancellationToken ct) { Ran.Add(job.Id); return Task.CompletedTask; }
    public static void Reset() => Ran.Clear();
}

/// <summary><c>[JobPersistence]</c> routing: with a durable adapter present, <c>InMemory</c>-typed jobs keep their
/// ledger state volatile (no durable row) while <c>DataStore</c>-typed jobs persist — and both still execute.</summary>
public sealed class PersistenceRoutingSpec
{
    [Fact]
    public async Task inmemory_typed_jobs_bypass_the_durable_store_while_datastore_typed_jobs_persist()
    {
        EphemeralJob.Reset();
        DurableJob.Reset();
        await using var host = await JobsHarness.StartSqliteAsync();

        var mem = new EphemeralJob { Note = "x" };
        await mem.Job.Submit();
        var dur = new DurableJob { Note = "y" };
        await dur.Job.Submit();

        // Before draining: the durable JobRecord set holds the DataStore job's ledger row but NOT the InMemory job's.
        var durableRows = await JobRecord.All();
        durableRows.Should().Contain(r => r.WorkId == dur.Id, "DataStore-typed jobs persist their ledger state");
        durableRows.Should().NotContain(r => r.WorkId == mem.Id, "InMemory-typed jobs keep ledger state volatile");

        // Both still run — the orchestrator drains both ledgers transparently.
        await host.Drain();
        EphemeralJob.Ran.Should().Contain(mem.Id);
        DurableJob.Ran.Should().Contain(dur.Id);

        // The InMemory job never left a durable ledger trace, even after completing.
        (await JobRecord.All()).Should().NotContain(r => r.WorkId == mem.Id);
    }
}
