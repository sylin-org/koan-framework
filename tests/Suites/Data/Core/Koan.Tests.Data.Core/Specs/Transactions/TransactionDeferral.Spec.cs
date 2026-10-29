using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core;
using Koan.Data.Core.Lifecycle;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transactions;
using Koan.Tests.Data.Core.Support;
using AwesomeAssertions;

namespace Koan.Tests.Data.Core.Specs.Transactions;

/// <summary>
/// DATA-0130 (NK-043, NK-044, NK-047, NK-012, NK-088): a write inside <c>EntityContext.Transaction</c> is either
/// deferred until Commit, in call order and under the context it was issued in, or refused before any write. No verb
/// dispatches immediately while the caller believes it is pending, and an auto-commit failure is never swallowed.
/// </summary>
public sealed class TransactionDeferralSpec
{
    [Fact]
    public async Task Bulk_save_and_remove_are_deferred_and_discarded_by_rollback()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        using var route = EntityContext.Partition($"tx-bulk-{Guid.CreateVersion7():n}");
        var kept = new TodoEntity { Title = "kept" };
        await kept.Save();
        var added = new[] { new TodoEntity { Title = "a" }, new TodoEntity { Title = "b" } };

        using (EntityContext.Transaction("bulk-rollback"))
        {
            (await added.Save()).Should().Be(2);
            (await new[] { kept }.Remove()).Should().Be(1);
            (await TodoEntity.All()).Select(e => e.Title).Should().Equal(["kept"], "nothing dispatched before commit");
            await EntityContext.Rollback();
        }

        (await TodoEntity.All()).Select(e => e.Title).Should().Equal("kept");
    }

    [Fact]
    public async Task Deferred_writes_commit_in_call_order_across_adapters()
    {
        var order = new List<string>();
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(configureServices: _ =>
            OrderedTodo.Lifecycle.AfterUpsert(context => { lock (order) order.Add(context.Current.Title); }));
        using var route = EntityContext.Partition($"tx-order-{Guid.CreateVersion7():n}");

        using (EntityContext.Transaction("ordered"))
        {
            await new OrderedTodo { Title = "1" }.Save();
            using (EntityContext.Adapter("json")) await new OrderedTodo { Title = "2" }.Save();
            await new[] { new OrderedTodo { Title = "3" } }.Save();
            await EntityContext.Commit();
        }

        order.Should().Equal(["1", "2", "3"], "commit dispatches in the order the application issued");
    }

    [Theory]
    [InlineData("DeleteAll")]
    [InlineData("RemoveAll")]
    [InlineData("Patch")]
    [InlineData("Batch")]
    [InlineData("Execute")]
    [InlineData("Copy")]
    [InlineData("UntypedUpsert")]
    public async Task Verbs_that_cannot_defer_refuse_before_any_write(string verb)
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        using var route = EntityContext.Partition($"tx-refuse-{Guid.CreateVersion7():n}");
        var existing = new TodoEntity { Title = "existing" };
        await existing.Save();

        using (EntityContext.Transaction("refusing"))
        {
            Func<Task> act = verb switch
            {
                "DeleteAll" => () => Data<TodoEntity, string>.DeleteAll(),
                "RemoveAll" => () => Data<TodoEntity, string>.RemoveAll(RemoveStrategy.Safe),
                "Patch" => () => TodoEntity.Patch(existing.Id, new { Title = "patched" }),
                "Batch" => () => TodoEntity.Batch().Delete(existing.Id).Save(),
                "Execute" => () => Data<TodoEntity, string>.Execute<int>(new Instruction(DataInstructions.Clear)),
                "Copy" => () => TodoEntity.Copy().To(partition: "copy-target").Run(),
                "UntypedUpsert" => () => AggregateExtensions.Upsert((object)new GuidTodo { Title = "g" }),
                _ => throw new ArgumentOutOfRangeException(nameof(verb))
            };
            if (verb == "UntypedUpsert")
            {
                await act();
                (await GuidTodo.All()).Should().BeEmpty("the untyped upsert is deferred like Save");
                await EntityContext.Rollback();
            }
            else
            {
                var assertion = await act.Should().ThrowAsync<Exception>();
                assertion.Which.Should().Match(e => e is NotSupportedException || e is InvalidOperationException);
                await EntityContext.Rollback();
            }
        }

        (await TodoEntity.Get(existing.Id))!.Title.Should().Be("existing");
        (await TodoEntity.All()).Should().HaveCount(1);
        (await GuidTodo.All()).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelled_commit_reports_the_operations_already_durable()
    {
        using var cancel = new CancellationTokenSource();
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(configureServices: _ =>
            CancellingTodo.Lifecycle.AfterUpsert(context =>
            {
                if (context.Current.CancelAfter) cancel.Cancel();
            }));
        // Sequential dispatch is the contract of a route without write.atomicCommit (DATA-0158); the default route
        // here is SQLite, which rolls a cancelled commit back, so the proof pins the JSON route.
        using var route = EntityContext.With(adapter: "json", partition: $"tx-cancel-{Guid.CreateVersion7():n}");
        TransactionException failure;

        using (EntityContext.Transaction("cancelled"))
        {
            await new CancellingTodo { Title = "one" }.Save();
            await new CancellingTodo { Title = "two", CancelAfter = true }.Save();
            await new CancellingTodo { Title = "three" }.Save();
            var commit = () => EntityContext.Commit(cancel.Token);
            failure = (await commit.Should().ThrowAsync<TransactionException>()).Which;
        }

        failure.CompletedOperationCount.Should().Be(2);
        (await CancellingTodo.All()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Auto_commit_on_dispose_reports_a_failed_commit()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(
            extraSettings: new Dictionary<string, string?> { ["Koan:Data:Transactions:AutoCommitOnDispose"] = "true" },
            configureServices: _ => FailingTodo.Lifecycle.BeforeUpsert(context =>
                context.Current.Title == "fails"
                    ? throw new InvalidOperationException("deliberate commit failure")
                    : EntityLifecycleResult.Proceed()));
        // On the JSON route the failed dispatch reports Unknown; on SQLite it would roll back and report NotCommitted.
        using var route = EntityContext.With(adapter: "json", partition: $"tx-auto-{Guid.CreateVersion7():n}");

        var scope = EntityContext.Transaction("auto-commit");
        await new FailingTodo { Title = "fails" }.Save();
        var dispose = () => scope.Dispose();

        dispose.Should().Throw<TransactionException>().Which.CommitOutcome.Should().Be(DataCommitOutcome.Unknown);
        EntityContext.Current?.TransactionCoordinator.Should().BeNull("the scope is left even when the commit fails");
    }

    private sealed class GuidTodo : Entity<GuidTodo, Guid>
    {
        public string Title { get; set; } = "";
    }

    private sealed class OrderedTodo : Entity<OrderedTodo>
    {
        public string Title { get; set; } = "";
    }

    private sealed class CancellingTodo : Entity<CancellingTodo>
    {
        public string Title { get; set; } = "";
        public bool CancelAfter { get; set; }
    }

    private sealed class FailingTodo : Entity<FailingTodo>
    {
        public string Title { get; set; } = "";
    }
}
