using Koan.Core;
using Koan.Data.Core.Transfers;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// NK-049. A Move deletes its source rows only after the whole source was copied. It used to delete every journaled
/// id blindly, so a row another writer updated after its batch was copied was deleted while the destination kept the
/// older copy, and a row another writer deleted made the exact-count check throw after earlier groups were gone.
/// The per-batch <c>Audit</c> callback runs between batches, which is where the other writer acts here.
/// </summary>
[Collection(nameof(SqliteMoveConcurrencySpec))]
[CollectionDefinition(nameof(SqliteMoveConcurrencySpec), DisableParallelization = true)]
public sealed class SqliteMoveConcurrencySpec
{
    public sealed class Job : Entity<Job>
    {
        public string Title { get; set; } = "";
    }

    [Fact]
    public async Task A_row_updated_after_it_was_copied_stays_in_the_source_and_is_reported()
    {
        await WithHost(async () =>
        {
            await Seed();

            var result = await Job.Move()
                .From(partition: "hot")
                .To(partition: "cold")
                .Batch(1)
                .Audit(batch =>
                {
                    if (batch is { IsSummary: false, BatchNumber: 1 })
                        InHot(() => new Job { Id = "a", Title = "updated after copy" }.Save());
                })
                .Run();

            result.CopiedCount.Should().Be(3);
            result.DeletedCount.Should().Be(2);
            result.Conflicts.Should().ContainSingle(conflict => conflict.Id == "a");
            using (EntityContext.Partition("hot"))
                (await Job.Get("a"))!.Title.Should().Be("updated after copy", "the newer source row must survive");
            using (EntityContext.Partition("hot"))
                (await Job.All()).Select(job => job.Id).Should().Equal("a");
        });
    }

    [Fact]
    public async Task A_row_deleted_after_it_was_copied_is_reported_and_the_move_completes()
    {
        await WithHost(async () =>
        {
            await Seed();

            var result = await Job.Move()
                .From(partition: "hot")
                .To(partition: "cold")
                .Batch(1)
                .Audit(batch =>
                {
                    // After the last copy, so the row is gone only by the time the Move removes its copies.
                    if (batch is { IsSummary: false, BatchNumber: 3 })
                        InHot(async () => { await Data<Job, string>.Delete("b"); });
                })
                .Run();

            result.CopiedCount.Should().Be(3);
            result.DeletedCount.Should().Be(2, "the row another writer removed is not counted as moved");
            result.Warnings.Should().Contain(warning => warning.Contains("no longer in the source", StringComparison.Ordinal));
            using (EntityContext.Partition("hot"))
                (await Job.All()).Should().BeEmpty();
            using (EntityContext.Partition("cold"))
                (await Job.All()).Should().HaveCount(3);
        });
    }

    /// <summary>
    /// NK-306. A row the Move already copied is deleted while the source is still being read. A numbered page then
    /// started one row late, so the next row was never copied and stayed in the source. The read continues after the
    /// last identity it returned, so the delete behind it moves nothing.
    /// </summary>
    [Fact]
    public async Task A_row_deleted_behind_the_read_does_not_make_the_move_skip_a_row()
    {
        await WithHost(async () =>
        {
            await Seed();

            var result = await Job.Move()
                .From(partition: "hot")
                .To(partition: "cold")
                .Batch(1)
                .Audit(batch =>
                {
                    if (batch is { IsSummary: false, BatchNumber: 1 })
                        InHot(async () => { await Data<Job, string>.Delete("a"); });
                })
                .Run();

            result.CopiedCount.Should().Be(3, "b and c were in the source for the whole read");
            result.DeletedCount.Should().Be(2);
            using (EntityContext.Partition("hot"))
                (await Job.All()).Should().BeEmpty();
            using (EntityContext.Partition("cold"))
                (await Job.All()).Select(job => job.Id).Should().BeEquivalentTo(["a", "b", "c"]);
        });
    }

    private static async Task Seed()
    {
        using var hot = EntityContext.Partition("hot");
        foreach (var id in new[] { "a", "b", "c" })
            await new Job { Id = id, Title = $"job {id}" }.Save();
    }

    // The audit callback is synchronous; the other writer runs to completion before the transfer continues.
    private static void InHot(Func<Task> write)
        => Task.Run(async () =>
        {
            using var hot = EntityContext.Partition("hot");
            await write();
        }).GetAwaiter().GetResult();

    private static async Task WithHost(Func<Task> body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-sqlite-move-race-{Guid.CreateVersion7():n}.db");
        try
        {
            await using var host = await KoanIntegrationHost.Configure()
                .WithSetting("Koan:Environment", "Test")
                .WithSetting("Koan:Data:Sqlite:DdlPolicy", "AutoCreate")
                .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
                .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={path}")
                .ConfigureServices(services => services.AddKoan())
                .StartAsync();
            await body();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
