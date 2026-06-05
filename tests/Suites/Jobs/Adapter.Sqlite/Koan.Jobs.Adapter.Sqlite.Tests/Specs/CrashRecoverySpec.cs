using System.IO;
using AwesomeAssertions;
using Koan.Jobs;
using Koan.Jobs.TestKit;
using Xunit;

namespace Koan.Jobs.Adapter.Sqlite.Tests.Specs;

/// <summary>Crash / restart recovery on a real durable store: the ledger is the truth, so a reboot loses nothing —
/// queued work survives, lapsed-lease jobs are reclaimed, and a mid-chain crash resumes at the next stage.</summary>
public sealed class CrashRecoverySpec
{
    private static string NewDbPath() => Path.Combine(Path.GetTempPath(), $"koan-jobs-crash-{Guid.NewGuid():n}.db");

    [Fact]
    public async Task queued_jobs_survive_a_restart()
    {
        GreetJob.Reset();
        var db = NewDbPath();
        try
        {
            string id;
            await using (var h1 = await JobsHarness.StartSqliteAtAsync(db, clearOnStart: true, ownsDb: false))
            {
                var j = new GreetJob { Name = "survivor" };
                id = j.Id;
                await j.Job.Submit();        // queued, not drained — then the process "crashes"
            }
            GreetJob.Executions.Should().Be(0);

            await using (var h2 = await JobsHarness.StartSqliteAtAsync(db, clearOnStart: false, ownsDb: false))
            {
                await h2.Drain();            // the queued job survived the restart
                GreetJob.Executions.Should().Be(1);
                (await h2.StatusOf<GreetJob>(id)).Should().Be(JobStatus.Completed);
            }
        }
        finally { if (File.Exists(db)) File.Delete(db); }
    }

    [Fact]
    public async Task running_job_with_a_lapsed_lease_is_reclaimed_after_restart()
    {
        GreetJob.Reset();
        var db = NewDbPath();
        try
        {
            var g = new GreetJob { Name = "reclaimed" };
            var id = g.Id;
            await using (var h1 = await JobsHarness.StartSqliteAtAsync(db, clearOnStart: true, ownsDb: false))
            {
                await GreetJob.Upsert(g);
                var now = h1.Clock.GetUtcNow();
                await h1.Ledger.Append(new JobRecord
                {
                    WorkType = typeof(GreetJob).FullName!, WorkId = id, Action = "",
                    Status = JobStatus.Running, Attempt = 1, Lane = "default",
                    FirstSubmittedAt = now, VisibleAt = now, LeaseUntil = now - TimeSpan.FromMinutes(1),
                    Owner = "crashed-worker",
                }, default);
            }

            await using (var h2 = await JobsHarness.StartSqliteAtAsync(db, clearOnStart: false, ownsDb: false))
            {
                await h2.Reap();             // boot recovery reclaims the lapsed-lease job
                await h2.Drain();
                GreetJob.Executions.Should().Be(1);
                (await h2.StatusOf<GreetJob>(id)).Should().Be(JobStatus.Completed);
            }
        }
        finally { if (File.Exists(db)) File.Delete(db); }
    }

    [Fact]
    public async Task a_crash_mid_chain_resumes_at_the_next_stage()
    {
        var db = NewDbPath();
        try
        {
            var p = new Pipeline { Fetched = "raw" };  // Fetch ran before the crash; Parse was queued
            var id = p.Id;
            await using (var h1 = await JobsHarness.StartSqliteAtAsync(db, clearOnStart: true, ownsDb: false))
            {
                await Pipeline.Upsert(p);
                var now = h1.Clock.GetUtcNow();
                await h1.Ledger.Append(new JobRecord
                {
                    WorkType = typeof(Pipeline).FullName!, WorkId = id, Action = Stage.Parse,
                    Status = JobStatus.Queued, Lane = Stage.Parse, FirstSubmittedAt = now, VisibleAt = now,
                }, default);
            }

            await using (var h2 = await JobsHarness.StartSqliteAtAsync(db, clearOnStart: false, ownsDb: false))
            {
                await h2.Drain();           // resumes at Parse → Mint → Publish
                var saved = await Pipeline.Get(id);
                saved!.Parsed.Should().Be("raw-parsed");
                saved.Minted.Should().BeTrue();
                saved.Published.Should().BeTrue();
                saved.Trail.Should().Equal(Stage.Parse, Stage.Mint, Stage.Publish);
            }
        }
        finally { if (File.Exists(db)) File.Delete(db); }
    }
}
