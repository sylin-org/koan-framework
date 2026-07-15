using Koan.Data.Core.Lifecycle;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transactions;
using Koan.Tests.Data.Core.Support;
using Koan.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using DataConstants = Koan.Data.Core.Infrastructure.Constants;

namespace Koan.Tests.Data.Core.Specs.Entity;

public sealed class EntityLifecycleSpec
{
    private static string NewPartition() => $"data-core-lifecycle-{Guid.CreateVersion7():n}";

    [Fact]
    public async Task Before_upsert_cancel_prevents_persistence()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(configureServices: _ =>
            LifecycleEntity.Lifecycle.BeforeUpsert(ctx =>
                ctx.Current.Title.Contains("blocked", StringComparison.OrdinalIgnoreCase)
                    ? ctx.Cancel("blocked", "title.blocked")
                    : ctx.Proceed()));

        using var partition = runtime.UsePartition(NewPartition());
        var action = () => LifecycleEntity.Upsert(new LifecycleEntity { Title = "Blocked Draft" });

        var error = await action.Should().ThrowAsync<EntityLifecycleCancelledException>();
        error.Which.ReasonCode.Should().Be("title.blocked");
        (await LifecycleEntity.All(partition.Partition)).Should().BeEmpty();
    }

    [Fact]
    public async Task Direct_Data_and_Entity_reads_share_the_same_lifecycle()
    {
        var loads = 0;
        var upserts = 0;
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(configureServices: _ =>
            LifecycleEntity.Lifecycle
                .AfterLoad(ctx => { loads++; ctx.Current.Revision++; })
                .AfterUpsert(_ => upserts++));

        using var partition = runtime.UsePartition(NewPartition());
        var saved = await Data<LifecycleEntity, string>.Upsert(new LifecycleEntity { Title = "canonical" });
        upserts.Should().Be(1);

        (await LifecycleEntity.Get(saved.Id))!.Revision.Should().Be(1);
        (await Data<LifecycleEntity, string>.Query(entity => entity.Id == saved.Id)).Single().Revision.Should().Be(1);
        loads.Should().Be(2, "key and query materialization use the same Data-owned lifecycle");
    }

    [Fact]
    public async Task Prior_and_protection_are_operation_scoped()
    {
        string? observedPrior = null;
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(configureServices: _ =>
            LifecycleEntity.Lifecycle.BeforeUpsert(async ctx =>
            {
                observedPrior = (await ctx.Prior.Get())?.Title;
                ctx.Protect(nameof(LifecycleEntity.Title));
                ctx.Current.Revision++;
                return ctx.Proceed();
            }));

        using var partition = runtime.UsePartition(NewPartition());
        var first = await LifecycleEntity.Upsert(new LifecycleEntity { Title = "v1" });
        observedPrior.Should().BeNull();

        await LifecycleEntity.Upsert(new LifecycleEntity { Id = first.Id, Title = "v2" });
        observedPrior.Should().Be("v1");
    }

    [Fact]
    public async Task Protected_mutation_fails_before_the_write()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(configureServices: _ =>
            LifecycleEntity.Lifecycle.BeforeUpsert(ctx =>
            {
                ctx.ProtectAll();
                ctx.Current.Title = "changed";
                return ctx.Proceed();
            }));

        using var partition = runtime.UsePartition(NewPartition());
        var action = () => LifecycleEntity.Upsert(new LifecycleEntity { Title = "original" });
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*protected and cannot be mutated*");
        (await LifecycleEntity.All()).Should().BeEmpty();
    }

    [Fact]
    public async Task Bulk_rejection_is_preflighted_before_the_first_write()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(configureServices: _ =>
            LifecycleEntity.Lifecycle.BeforeUpsert(ctx =>
                ctx.Current.Title == "blocked" ? ctx.Cancel("blocked") : ctx.Proceed()));

        using var partition = runtime.UsePartition(NewPartition());
        var action = () => Data<LifecycleEntity, string>.UpsertMany([
            new LifecycleEntity { Title = "allowed" },
            new LifecycleEntity { Title = "blocked" },
        ]);

        await action.Should().ThrowAsync<EntityLifecycleCancelledException>();
        (await LifecycleEntity.All()).Should().BeEmpty();
    }

    [Fact]
    public async Task After_upsert_waits_for_transaction_commit()
    {
        var after = 0;
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(configureServices: _ =>
            LifecycleEntity.Lifecycle.AfterUpsert(_ => after++));

        using var partition = runtime.UsePartition(NewPartition());
        using (EntityContext.Transaction("lifecycle-commit"))
        {
            await LifecycleEntity.Upsert(new LifecycleEntity { Title = "deferred" });
            after.Should().Be(0);
            await EntityContext.Commit();
            after.Should().Be(1);
        }
    }

    [Fact]
    public async Task Sequential_hosts_do_not_share_lifecycle_declarations()
    {
        var first = 0;
        await using (var host = await DataCoreRuntimeFixture.CreateAsync(configureServices: _ =>
                         LifecycleEntity.Lifecycle.AfterUpsert(_ => first++)))
        {
            using var partition = host.UsePartition(NewPartition());
            await LifecycleEntity.Upsert(new LifecycleEntity { Title = "first" });
            first.Should().Be(1);
        }

        var second = 0;
        await using (var host = await DataCoreRuntimeFixture.CreateAsync(configureServices: _ =>
                         LifecycleEntity.Lifecycle.AfterUpsert(_ => second++)))
        {
            using var partition = host.UsePartition(NewPartition());
            await LifecycleEntity.Upsert(new LifecycleEntity { Title = "second" });
            second.Should().Be(1);
            first.Should().Be(1, "the first host's plan is not process state");
        }
    }

    [Fact]
    public async Task Composed_lifecycle_is_visible_in_the_shared_runtime_facts()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(configureServices: _ =>
            LifecycleEntity.Lifecycle
                .BeforeUpsert(ctx => ctx.Proceed())
                .AfterUpsert(_ => { }));

        var fact = runtime.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts
            .Single(item => item.Code == DataConstants.Diagnostics.Codes.LifecycleSelected);

        fact.Subject.Should().Be("data:lifecycle:lifecycleentity");
        fact.Summary.Should().Contain("2 persistence lifecycle handler(s)");
    }

    [Fact]
    public void Declaration_outside_composition_fails_correctively()
    {
        var action = () => LifecycleEntity.Lifecycle.AfterUpsert(_ => { });
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Lifecycle*AddKoan*");
    }

    private sealed class LifecycleEntity : Entity<LifecycleEntity, string>
    {
        [Identifier]
        public override string Id { get; set; } = default!;
        public string Title { get; set; } = "";
        public int Revision { get; set; }
    }
}
