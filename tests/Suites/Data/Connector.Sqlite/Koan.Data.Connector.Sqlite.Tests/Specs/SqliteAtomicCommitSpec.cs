using Koan.Core;
using System.Collections.Generic;
using System.IO;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core.Lifecycle;
using Koan.Data.Core.Transactions;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// DATA-0158: on one SQLite route <c>EntityContext.Transaction</c> commits every tracked Save and Remove, across
/// entity types, inside one native transaction begun immediate. A failure anywhere before the commit leaves nothing
/// durable, and a scope that asks for <see cref="TransactionGuarantee.Atomic"/> gets exactly that or a refusal.
/// DATA-0161: a failure before the native transaction opens reports <see cref="DataCommitOutcome.NotCommitted"/>; only
/// a failure of the native commit itself is <see cref="DataCommitOutcome.Unknown"/>.
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

    [Storage(Name = "KOAN_ATOMIC_CLAIMANT")]
    public sealed class Claimant : Entity<Claimant>
    {
        [Index(Name = "ix_atomic_claimed_code")] public string Code { get; set; } = "";
    }

    [Storage(Name = "KOAN_ATOMIC_INTRUDER")]
    public sealed class Intruder : Entity<Intruder>
    {
        [Index(Name = "ix_atomic_claimed_code")] public string Code { get; set; } = "";
    }

    public sealed class Pinned : Entity<Pinned>
    {
        public string Label { get; set; } = "";
    }

    public sealed class Receipt : Entity<Receipt>
    {
        public string Note { get; set; } = "";
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

    [Fact]
    public async Task A_provisioning_failure_before_the_native_transaction_opens_reports_not_committed()
    {
        RequireBackingStore();
        await using var host = await BootAsync(FailFlaggedLines);
        // The two entities declare one index name, and Claimant's own table holds it first, so Intruder's own table
        // cannot be provisioned: the coordinator fails before it opens the native transaction.
        await new Claimant { Code = "c-1" }.Save();
        var order = new Order { Title = "never" };
        var intruder = new Intruder { Code = "c-1" };
        TransactionException failure;

        using (EntityContext.Transaction("writer", TransactionGuarantee.Atomic))
        {
            await order.Save();
            await intruder.Save();
            var commit = () => EntityContext.Commit();
            failure = (await commit.Should().ThrowAsync<TransactionException>()).Which;
        }

        failure.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted, "nothing was dispatched, so nothing can have landed");
        failure.CompletedOperationCount.Should().Be(0);
        failure.Message.Should().Contain("before any tracked operation was dispatched");
        failure.InnerException!.Message.Should().Contain("already owned by SQLite table 'KOAN_ATOMIC_CLAIMANT'");
        (await Order.Get(order.Id)).Should().BeNull();
    }

    /// <summary>
    /// DATA-0161: a store that refuses to provision with <see cref="NotSupportedException"/> before dispatch is a
    /// failure before dispatch like any other — <see cref="DataCommitOutcome.NotCommitted"/> with the refusal inside —
    /// not an exception without an outcome. An explicit SQLite map pins one container and refuses an ambient partition.
    /// </summary>
    [Fact]
    public async Task A_store_refusing_to_provision_before_dispatch_reports_not_committed_with_its_refusal_inside()
    {
        RequireBackingStore();
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(Fixture.SettingsForBoot())
            .ConfigureServices(services => services.AddKoan(koan =>
                koan.Data.Source("Default").Map<Pinned>(map => map
                    .Container("KOAN_ATOMIC_PINNED")
                    .Key(item => item.Id).Name("ID")
                    .Property(item => item.Label).Name("LABEL"))))
            .StartAsync(TestContext.Current.CancellationToken);
        using var _ = Lease(NewPartition("atomic-pinned"));
        var order = new Order { Title = "never" };
        TransactionException failure;

        using (EntityContext.Transaction("writer", TransactionGuarantee.Atomic))
        {
            await order.Save();
            await new Pinned { Label = "pinned" }.Save();
            var commit = () => EntityContext.Commit();
            failure = (await commit.Should().ThrowAsync<TransactionException>()).Which;
        }

        failure.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted, "nothing was dispatched, so nothing can have landed");
        failure.CompletedOperationCount.Should().Be(0);
        failure.Message.Should().Contain("before any tracked operation was dispatched");
        failure.InnerException.Should().BeOfType<NotSupportedException>()
            .Which.Message.Should().Contain("cannot accept an ambient partition");
        (await Order.Get(order.Id)).Should().BeNull();
    }

    [Fact]
    public async Task A_native_transaction_that_cannot_begin_reports_not_committed()
    {
        RequireBackingStore();
        var path = Path.Combine(Path.GetTempPath(), $"koan-sqlite-atomic-begin-{Guid.CreateVersion7():n}.db");
        try
        {
            await using var host = await BootAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path}",
                ["Koan:Data:Sqlite:BusyTimeout"] = "00:00:01",
            });
            using var _ = Lease(NewPartition("atomic-begin"));
            // Saved outside the scope, so the table is provisioned and the scope needs no schema work of its own.
            var order = await new Order { Title = "kept" }.Save();

            // Another writer holds the database's write lock, so BEGIN IMMEDIATE waits out the busy timeout and fails.
            await using var holder = new SqliteConnection($"Data Source={path};Pooling=False");
            await holder.OpenAsync();
            await using (var begin = holder.CreateCommand())
            {
                begin.CommandText = "BEGIN IMMEDIATE";
                await begin.ExecuteNonQueryAsync();
            }

            TransactionException failure;
            using (EntityContext.Transaction("writer", TransactionGuarantee.Atomic))
            {
                order.Title = "blocked";
                await order.Save();
                var commit = () => EntityContext.Commit();
                failure = (await commit.Should().ThrowAsync<TransactionException>()).Which;
            }

            await using (var rollback = holder.CreateCommand())
            {
                rollback.CommandText = "ROLLBACK";
                await rollback.ExecuteNonQueryAsync();
            }

            failure.CommitOutcome.Should().Be(DataCommitOutcome.NotCommitted, "the native transaction never began");
            failure.CompletedOperationCount.Should().Be(0);
            (await Order.Get(order.Id))!.Title.Should().Be("kept");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task A_failure_of_the_native_commit_itself_still_reports_unknown()
    {
        RequireBackingStore();
        using var cancel = new CancellationTokenSource();
        // The last dispatched row cancels the commit's token, so the native commit is the step that fails.
        await using var host = await BootAsync(() => Receipt.Lifecycle.AfterUpsert(_ => cancel.Cancel()));
        using var _ = Lease(NewPartition("atomic-commit-failure"));
        var receipt = new Receipt { Note = "dispatched before the commit failed" };
        TransactionException failure;

        using (EntityContext.Transaction("writer", TransactionGuarantee.Atomic))
        {
            await receipt.Save();
            var commit = () => EntityContext.Commit(cancel.Token);
            failure = (await commit.Should().ThrowAsync<TransactionException>()).Which;
        }

        failure.CommitOutcome.Should().Be(DataCommitOutcome.Unknown, "a failed commit cannot say whether the store committed");
        failure.Message.Should().Contain("while committing its native transaction");
    }
}
