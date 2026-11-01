using Koan.Data.Core.Transactions;
using Koan.Tests.Data.Core.Support;
using AwesomeAssertions;

namespace Koan.Tests.Data.Core.Specs.Transactions;

/// <summary>
/// DATA-0158: a scope opened with <see cref="TransactionGuarantee.Atomic"/> is one native transaction or a refusal.
/// A route whose provider does not declare <c>write.atomicCommit</c>, or a second route, is refused when the
/// operation is tracked, before any write, with the correction in the message. A coordinated scope keeps today's
/// sequential dispatch on those routes. DATA-0166: <see cref="EntityContext.Hold"/> is refused where its route cannot hold
/// a native transaction.
/// </summary>
public sealed class TransactionGuaranteeSpec
{
    [Theory]
    [InlineData("json")]
    [InlineData("inmemory")]
    public async Task Atomic_scope_refuses_a_route_whose_provider_cannot_commit_atomically(string adapter)
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        using var route = EntityContext.With(adapter: adapter, partition: $"tx-atomic-{Guid.CreateVersion7():n}");
        var entity = new TodoEntity { Title = "refused" };

        using (EntityContext.Transaction("writer", TransactionGuarantee.Atomic))
        {
            var save = () => entity.Save();
            var refusal = (await save.Should().ThrowAsync<NotSupportedException>()).Which;
            refusal.Message.Should().Contain("requires an atomic commit")
                .And.Contain(adapter)
                .And.Contain("write.atomicCommit")
                .And.Contain("TransactionGuarantee.Coordinated");
            EntityContext.CurrentTransaction!.TrackedOperationCount.Should().Be(0, "a refused operation is never held");
            await EntityContext.Commit();
        }

        (await TodoEntity.Get(entity.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Atomic_scope_refuses_a_second_route_even_when_it_could_commit_atomically()
    {
        // Two SQLite databases are two routes; one native transaction covers one of them.
        var archive = Path.Combine(Path.GetTempPath(), $"koan-tx-archive-{Guid.CreateVersion7():n}.sqlite");
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(
            includeSqlite: true,
            extraSettings: new Dictionary<string, string?>
            {
                ["Koan:Data:Sources:Archive:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Archive:ConnectionString"] = $"Data Source={archive}",
            });
        var partition = $"tx-atomic-two-routes-{Guid.CreateVersion7():n}";
        var first = new TodoEntity { Title = "default route" };
        var second = new TodoEntity { Title = "archive route" };

        try
        {
            using (EntityContext.Partition(partition))
            using (EntityContext.Transaction("writer", TransactionGuarantee.Atomic))
            {
                await first.Save();
                using (EntityContext.Source("Archive"))
                {
                    var save = () => second.Save();
                    (await save.Should().ThrowAsync<NotSupportedException>()).Which.Message
                        .Should().Contain("one native transaction covers one route").And.Contain("Archive");
                }
                EntityContext.CurrentTransaction!.TrackedOperationCount.Should().Be(1);
                await EntityContext.Rollback();
            }

            using (EntityContext.Partition(partition))
                (await TodoEntity.Get(first.Id)).Should().BeNull();
            using (EntityContext.Source("Archive"))
            using (EntityContext.Partition(partition))
                (await TodoEntity.Get(second.Id)).Should().BeNull();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var file in new[] { archive, archive + "-wal", archive + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }

    /// <summary>DATA-0166: a route that cannot hold a transaction refuses the request instead of quietly deferring.</summary>
    [Theory]
    [InlineData("json")]
    [InlineData("inmemory")]
    public async Task A_hold_refuses_a_route_whose_provider_cannot_hold_a_transaction(string adapter)
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        using var route = EntityContext.With(adapter: adapter, partition: $"tx-hold-{Guid.CreateVersion7():n}");

        var hold = () => EntityContext.Hold("writer");
        var refusal = hold.Should().Throw<NotSupportedException>().Which;
        refusal.Message.Should().Contain("requires a held transaction")
            .And.Contain(adapter)
            .And.Contain("write.heldTransaction")
            .And.Contain("EntityContext.Transaction");
        EntityContext.InTransaction.Should().BeFalse("a refused hold leaves nothing ambient");

        var entity = await new TodoEntity { Title = "the flow goes on" }.Save();
        (await TodoEntity.Get(entity.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Coordinated_scope_on_a_route_without_atomic_commit_still_dispatches_sequentially()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        using var route = EntityContext.With(adapter: "json", partition: $"tx-coordinated-{Guid.CreateVersion7():n}");
        var entity = new TodoEntity { Title = "deferred" };

        using (EntityContext.Transaction("writer"))
        {
            await entity.Save();
            EntityContext.CurrentTransaction!.Capabilities.Has(TxCaps.Local).Should().BeFalse();
            await EntityContext.Commit();
        }

        (await TodoEntity.Get(entity.Id))!.Title.Should().Be("deferred");
    }
}
