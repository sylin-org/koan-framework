using System.Collections.Generic;
using System.IO;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core.Lifecycle;
using Koan.Data.Core.Transactions;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// DATA-0158: on one SQLite route <c>EntityContext.Transaction</c> commits every tracked Save and Remove, across
/// entity types, inside one native transaction begun immediate. A failure anywhere before the commit leaves nothing
/// durable, and a scope that asks for <see cref="TransactionGuarantee.Atomic"/> gets exactly that or a refusal.
/// </summary>
public sealed class SqliteAtomicCommitSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    public sealed class Order : Entity<Order>
    {
        public string Title { get; set; } = "";
    }

    public sealed class Line : Entity<Line>
    {
        public string Sku { get; set; } = "";
        public bool FailOnSave { get; set; }
    }

    private static void FailFlaggedLines() =>
        Line.Lifecycle.BeforeUpsert(context => context.Current.FailOnSave
            ? throw new InvalidOperationException("deliberate second-row failure")
            : EntityLifecycleResult.Proceed());

    [Fact]
    public async Task A_required_scope_commits_two_entity_types_together_on_a_fresh_partition()
    {
        RequireBackingStore();
        await using var host = await BootAsync(FailFlaggedLines);
        using var _ = Lease(NewPartition("atomic-commit"));
        var order = new Order { Title = "ship it" };
        var line = new Line { Sku = "widget" };

        // The partition is new, so both tables are created inside the native transaction as well.
        using (EntityContext.Transaction("writer", TransactionGuarantee.Atomic))
        {
            await order.Save();
            await line.Save();
            await EntityContext.Commit();
        }

        (await Order.Get(order.Id))!.Title.Should().Be("ship it");
        (await Line.Get(line.Id))!.Sku.Should().Be("widget");
    }

    [Fact]
    public async Task A_required_scope_that_throws_between_two_saves_persists_neither()
    {
        RequireBackingStore();
        await using var host = await BootAsync(FailFlaggedLines);
        using var _ = Lease(NewPartition("atomic-throw"));
        var order = new Order { Title = "never" };
        var line = new Line { Sku = "never" };

        var work = async () =>
        {
            using (EntityContext.Transaction("writer", TransactionGuarantee.Atomic))
            {
                await order.Save();
                throw new InvalidOperationException("between the saves");
#pragma warning disable CS0162
                await line.Save();
#pragma warning restore CS0162
            }
        };
        await work.Should().ThrowAsync<InvalidOperationException>().WithMessage("between the saves");

        (await Order.Get(order.Id)).Should().BeNull();
        (await Line.Get(line.Id)).Should().BeNull();
    }

    [Fact]
    public async Task A_failure_on_the_second_dispatched_row_leaves_the_first_not_durable()
    {
        RequireBackingStore();
        await using var host = await BootAsync(FailFlaggedLines);
        using var _ = Lease(NewPartition("atomic-rollback"));
        var order = new Order { Title = "rolled back" };
        var line = new Line { Sku = "fails", FailOnSave = true };
        TransactionException failure;

        using (EntityContext.Transaction("writer", TransactionGuarantee.Atomic))
        {
            await order.Save();
            await line.Save();
            var commit = () => EntityContext.Commit();
            failure = (await commit.Should().ThrowAsync<TransactionException>()).Which;
        }

        failure.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted);
        failure.CompletedOperationCount.Should().Be(0, "the native transaction rolled the first row back");
        failure.Message.Should().Contain("nothing is durable");
        (await Order.Get(order.Id)).Should().BeNull("the order was dispatched first and must not survive the line's failure");
        (await Line.Get(line.Id)).Should().BeNull();
    }

    [Fact]
    public async Task A_coordinated_scope_on_one_sqlite_route_is_atomic_as_well()
    {
        RequireBackingStore();
        await using var host = await BootAsync(FailFlaggedLines);
        using var _ = Lease(NewPartition("coordinated-rollback"));
        var order = new Order { Title = "rolled back" };
        var line = new Line { Sku = "fails", FailOnSave = true };
        TransactionException failure;

        using (EntityContext.Transaction("writer"))
        {
            await order.Save();
            await line.Save();
            var commit = () => EntityContext.Commit();
            failure = (await commit.Should().ThrowAsync<TransactionException>()).Which;
        }

        failure.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted);
        (await Order.Get(order.Id)).Should().BeNull();
    }

    [Fact]
    public async Task An_update_and_a_remove_of_different_types_commit_together()
    {
        RequireBackingStore();
        await using var host = await BootAsync(FailFlaggedLines);
        using var _ = Lease(NewPartition("atomic-mixed"));
        var order = await new Order { Title = "draft" }.Save();
        var line = await new Line { Sku = "to remove" }.Save();

        using (EntityContext.Transaction("writer", TransactionGuarantee.Atomic))
        {
            order.Title = "final";
            await order.Save();
            await line.Remove();
            await EntityContext.Commit();
        }

        (await Order.Get(order.Id))!.Title.Should().Be("final");
        (await Line.Get(line.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Two_sources_that_spell_one_file_differently_commit_as_one_route()
    {
        RequireBackingStore();
        var path = Path.Combine(Path.GetTempPath(), $"koan-sqlite-one-store-{Guid.CreateVersion7():n}.db");
        // The same file through a directory spelled in upper case: one store, one ConnectionIdentity, two sources.
        var spelledDifferently = Path.Combine(Path.GetDirectoryName(path)!.ToUpperInvariant(), Path.GetFileName(path));
        try
        {
            await using var host = await BootAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path}",
                ["Koan:Data:Sources:Archive:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Archive:ConnectionString"] = $"Data Source={spelledDifferently}",
            });
            using var _ = Lease(NewPartition("one-store"));
            var order = new Order { Title = "default source" };
            var line = new Line { Sku = "archive source" };

            using (EntityContext.Transaction("writer", TransactionGuarantee.Atomic))
            {
                await order.Save();
                using (EntityContext.Source("Archive")) await line.Save();
                EntityContext.CurrentTransaction!.TrackedOperationCount.Should().Be(2, "one store is one route");
                await EntityContext.Commit();
            }

            (await Order.Get(order.Id))!.Title.Should().Be("default source");
            using (EntityContext.Source("Archive"))
                (await Line.Get(line.Id))!.Sku.Should().Be("archive source");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task A_required_scope_publishes_the_local_transaction_capability_and_a_coordinated_one_does_not()
    {
        RequireBackingStore();
        await using var host = await BootAsync(FailFlaggedLines);
        using var _ = Lease(NewPartition("atomic-capability"));

        using (EntityContext.Transaction("required", TransactionGuarantee.Atomic))
        {
            EntityContext.CurrentTransaction!.Guarantee.Should().Be(TransactionGuarantee.Atomic);
            EntityContext.CurrentTransaction.Capabilities.Has(TxCaps.Local).Should().BeTrue();
            await EntityContext.Rollback();
        }

        using (EntityContext.Transaction("coordinated"))
        {
            EntityContext.CurrentTransaction!.Guarantee.Should().Be(TransactionGuarantee.Coordinated);
            EntityContext.CurrentTransaction.Capabilities.Has(TxCaps.Local).Should().BeFalse();
            await EntityContext.Rollback();
        }
    }
}
