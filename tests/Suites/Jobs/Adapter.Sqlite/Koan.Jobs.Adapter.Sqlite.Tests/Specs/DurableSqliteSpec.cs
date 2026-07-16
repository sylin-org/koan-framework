using System.IO;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Jobs;
using Koan.Jobs.TestKit;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Koan.Jobs.Adapter.Sqlite.Tests.Specs;

/// <summary>Tier-specific durable proofs (the shared behaviors run via <c>SqliteBehaviors</c>): the election picks
/// the data-backed ledger, and the transactional outbox enlists in an ambient transaction.</summary>
public sealed class DurableSqliteSpec
{
    [Fact]
    public async Task explicit_default_source_owns_the_database_file()
    {
        var db = Path.Combine(Path.GetTempPath(), $"koan-jobs-placement-{Guid.NewGuid():n}.db");
        var fallback = Path.Combine(Path.GetTempPath(), $"koan-jobs-fallback-{Guid.NewGuid():n}.db");
        try
        {
            var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Koan:Environment"] = "Test",
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={db};Pooling=False",
                ["Koan:Data:Sqlite:ConnectionString"] = $"Data Source={fallback};Pooling=False",
            };
            await using (var host = await JobsHarness.StartWithSettingsAsync(settings)) { }

            await using var connection = new SqliteConnection($"Data Source={db};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            Convert.ToInt64(await command.ExecuteScalarAsync()).Should().BeGreaterThanOrEqualTo(4,
                "the configured file must contain the framework-owned job ledger schema, not merely a readiness probe");
            File.Exists(fallback).Should().BeFalse(
                "the unused provider fallback must not be materialized alongside the authoritative Default source");
        }
        finally
        {
            if (File.Exists(db)) File.Delete(db);
            if (File.Exists(fallback)) File.Delete(fallback);
        }
    }

    [Fact]
    public async Task election_picks_the_routing_ledger_over_a_durable_adapter()
    {
        await using var host = await JobsHarness.StartSqliteAsync();
        // A durable adapter elects the RoutingJobLedger: durable for Auto/DataStore types, volatile for InMemory.
        host.Ledger.Should().BeOfType<RoutingJobLedger>();
    }

    [Fact]
    public async Task submit_in_a_rolled_back_transaction_never_enqueues()
    {
        GreetJob.Reset();
        await using var host = await JobsHarness.StartSqliteAsync();
        var j = new GreetJob { Name = "x" };
        var id = j.Id;

        using (EntityContext.Transaction("rollback"))
        {
            await j.Job.Submit();
            await EntityContext.Rollback();
        }

        await host.Drain();
        GreetJob.Executions.Should().Be(0, "a rolled-back transaction must not enqueue the job");
        (await host.StatusOf<GreetJob>(id)).Should().BeNull();
    }

    [Fact]
    public async Task submit_in_a_committed_transaction_enqueues_once_on_commit()
    {
        GreetJob.Reset();
        await using var host = await JobsHarness.StartSqliteAsync();
        var j = new GreetJob { Name = "y" };
        var id = j.Id;

        using (EntityContext.Transaction("commit"))
        {
            await j.Job.Submit();
            (await host.StatusOf<GreetJob>(id)).Should().BeNull("deferred until commit (outbox)");
            await EntityContext.Commit();
        }

        await host.Drain();
        GreetJob.Executions.Should().Be(1);
        (await host.StatusOf<GreetJob>(id)).Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task collection_submission_reports_transaction_enlistment_until_commit()
    {
        await using var host = await JobsHarness.StartSqliteAsync();
        var jobs = new[]
        {
            new GreetJob { Name = "one" },
            new GreetJob { Name = "two" }
        };

        using (EntityContext.Transaction("collection-commit"))
        {
            var submission = await jobs.Submit();

            submission.Accepted.Should().Be(2);
            submission.Submitted.Should().Be(2);
            submission.PendingCommit.Should().BeTrue();
            (await host.StatusOf<GreetJob>(jobs[0].Id)).Should().BeNull("the ledger rows are not visible before commit");
            await EntityContext.Commit();
        }

        (await host.StatusOf<GreetJob>(jobs[0].Id)).Should().Be(JobStatus.Queued);
        (await host.StatusOf<GreetJob>(jobs[1].Id)).Should().Be(JobStatus.Queued);
    }
}
