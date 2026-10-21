using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Abstractions.Filtering;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Koan.Data.Connector.Postgres.Tests.Specs;

/// <summary>
/// NK-057: PostgreSQL implements insert-only and conditional delete, so owner-stamped HTTP creates and guarded deletes
/// work here as on SQLite and MongoDB. Container-backed.
/// </summary>
public sealed class PostgresInsertOnlySpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    [Fact]
    public async Task Duplicate_is_a_conflict_that_leaves_the_row_and_ordinary_save_still_updates()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var repository = host.Services.GetRequiredService<IDataService>().GetRepository<PgInsertProbe, string>();
        DataCaps.Describe(repository, "postgres").Has(DataCaps.Write.InsertOnly).Should().BeTrue();
        var insert = (IInsertOnlyRepository<PgInsertProbe, string>)repository;
        var original = new PgInsertProbe { Id = Guid.NewGuid().ToString("N"), Value = "original" };
        (await insert.Insert(original)).Outcome.Should().Be(MutationOutcome.Inserted);

        var conflict = await insert.Insert(new PgInsertProbe { Id = original.Id, Value = "replacement" });
        conflict.Outcome.Should().Be(MutationOutcome.Conflict);
        conflict.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted);
        conflict.Entity.Should().BeNull();
        (await PgInsertProbe.Get(original.Id))!.Value.Should().Be("original");

        original.Value = "ordinary save";
        await original.Save();
        (await PgInsertProbe.Get(original.Id))!.Value.Should().Be("ordinary save");
    }

    [Fact]
    public async Task Concurrent_insertions_commit_exactly_one_identity()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var insert = (IInsertOnlyRepository<PgInsertProbe, string>)host.Services
            .GetRequiredService<IDataService>().GetRepository<PgInsertProbe, string>();
        var id = Guid.NewGuid().ToString("N");
        _ = await PgInsertProbe.Get(id);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 8).Select(index => Task.Run(async () =>
        {
            await gate.Task;
            return await insert.Insert(new PgInsertProbe { Id = id, Value = $"winner-{index}" });
        })).ToArray();
        gate.SetResult();
        var receipts = await Task.WhenAll(attempts);
        receipts.Count(result => result.Outcome == MutationOutcome.Inserted).Should().Be(1);
        receipts.Count(result => result.Outcome == MutationOutcome.Conflict).Should().Be(7);
    }

    [Fact]
    public async Task A_unique_violation_on_another_index_is_a_failure_not_a_conflict()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var insert = (IInsertOnlyRepository<PgUniqueProbe, string>)host.Services
            .GetRequiredService<IDataService>().GetRepository<PgUniqueProbe, string>();
        var email = $"{Guid.NewGuid():N}@example.test";
        (await insert.Insert(new PgUniqueProbe { Id = "u1-" + email, Email = email })).Outcome.Should().Be(MutationOutcome.Inserted);
        await FluentActions.Invoking(() => insert.Insert(new PgUniqueProbe { Id = "u2-" + email, Email = email }))
            .Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task Conditional_delete_removes_only_a_matching_row()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var repository = host.Services.GetRequiredService<IDataService>().GetRepository<PgInsertProbe, string>();
        DataCaps.Describe(repository, "postgres").Has(DataCaps.Write.ConditionalDelete).Should().BeTrue();
        var delete = (IConditionalDeleteRepository<PgInsertProbe, string>)repository;
        var row = await new PgInsertProbe { Id = Guid.NewGuid().ToString("N"), Value = "kept" }.Save();

        (await delete.ConditionalDeleteAsync(row.Id, Filter.Eq(nameof(PgInsertProbe.Value), "other"))).Should().BeFalse();
        (await PgInsertProbe.Get(row.Id)).Should().NotBeNull();
        (await delete.ConditionalDeleteAsync(row.Id, Filter.Eq(nameof(PgInsertProbe.Value), "kept"))).Should().BeTrue();
        (await PgInsertProbe.Get(row.Id)).Should().BeNull();
    }

    public sealed class PgInsertProbe : Entity<PgInsertProbe>
    {
        public string Value { get; set; } = "";
    }

    public sealed class PgUniqueProbe : Entity<PgUniqueProbe>
    {
        [Index(Unique = true)] public string Email { get; set; } = "";
    }
}
