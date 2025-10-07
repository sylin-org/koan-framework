using Koan.Data.Core.Events;
using Koan.Data.Core.Model;
using Koan.Tests.Data.Core.Support;

namespace Koan.Tests.Data.Core.Specs.Entity;

public sealed class EntityLifecycleSpec
{
    private readonly ITestOutputHelper _output;

    public EntityLifecycleSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    private static string EnsurePartition(TestContext ctx)
    {
        const string Key = "partition";
        if (!ctx.TryGetItem<string>(Key, out var partition))
        {
            partition = $"data-core-lifecycle-{ctx.ExecutionId:n}";
            ctx.SetItem(Key, partition);
        }

        return partition;
    }

    private static void ResetHooks(DataCoreRuntimeFixture runtime)
    {
        runtime.ResetEntityCaches();
        EntityEventTestHooks.Reset<LifecycleEntity, string>();
        TestHooks.ResetDataConfigs();
    }
    [Fact]
    public async Task Before_upsert_cancel_prevents_persistence()
    {
        await TestPipeline.For<EntityLifecycleSpec>(_output, nameof(Before_upsert_cancel_prevents_persistence))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                ResetHooks(runtime);
                EnsurePartition(ctx);

                LifecycleEntity.Events
                    .BeforeUpsert(c =>
                    {
                        if (c.Current.Title.Contains("blocked", StringComparison.OrdinalIgnoreCase))
                        {
                            return new ValueTask<EntityEventResult>(c.Cancel("blocked"));
                        }

                        return new ValueTask<EntityEventResult>(c.Proceed());
                    });

                return ValueTask.CompletedTask;
            })
            .Assert(async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");

                var partitionName = EnsurePartition(ctx);
                using var lease = runtime.UsePartition(partitionName);
                var blocked = new LifecycleEntity { Title = "Blocked Draft" };

                await Assert.ThrowsAsync<EntityEventCancelledException>(() => LifecycleEntity.UpsertAsync(blocked));

                var all = await LifecycleEntity.All(lease.Partition);
                all.Should().BeEmpty();
            })
            .RunAsync();
    }

    [Fact]
    public async Task Protect_all_blocks_mutations()
    {
        await TestPipeline.For<EntityLifecycleSpec>(_output, nameof(Protect_all_blocks_mutations))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                ResetHooks(runtime);
                EnsurePartition(ctx);

                LifecycleEntity.Events
                    .Setup(c =>
                    {
                        c.ProtectAll();
                        return ValueTask.CompletedTask;
                    })
                    .AfterUpsert(c =>
                    {
                        c.Current.Revision++;
                        return ValueTask.CompletedTask;
                    });

                return ValueTask.CompletedTask;
            })
            .Assert(async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                EnsurePartition(ctx);

                var entity = new LifecycleEntity { Title = "Immutable" };

                var act = async () => await LifecycleEntity.UpsertAsync(entity);
                await act.Should().ThrowAsync<InvalidOperationException>()
                    .WithMessage("*protected and cannot be mutated*");
            })
            .RunAsync();
    }

    [Fact]
    public async Task Allow_mutation_permits_whitelisted_changes()
    {
        await TestPipeline.For<EntityLifecycleSpec>(_output, nameof(Allow_mutation_permits_whitelisted_changes))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                ResetHooks(runtime);
                var partitionName = EnsurePartition(ctx);
                using var lease = runtime.UsePartition(partitionName);

                LifecycleEntity.Events
                    .Setup(c =>
                    {
                        c.Protect(nameof(LifecycleEntity.Title));
                        c.AllowMutation(nameof(LifecycleEntity.Id));
                        c.AllowMutation(nameof(LifecycleEntity.Title));
                        return ValueTask.CompletedTask;
                    })
                    .BeforeUpsert(c =>
                    {
                        c.Current.Title = "mutated";
                        return new ValueTask<EntityEventResult>(c.Proceed());
                    });

                return ValueTask.CompletedTask;
            })
            .Assert(async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partitionName = EnsurePartition(ctx);
                using var lease = runtime.UsePartition(partitionName);

                var entity = new LifecycleEntity { Title = "original" };
                var saved = await LifecycleEntity.UpsertAsync(entity);

                saved.Title.Should().Be("mutated");

                var persisted = await LifecycleEntity.Get(saved.Id, lease.Partition);
                persisted?.Title.Should().Be("mutated");
            })
            .RunAsync();
    }

    [Fact]
    public async Task Prior_snapshot_exposes_previous_version()
    {
        await TestPipeline.For<EntityLifecycleSpec>(_output, nameof(Prior_snapshot_exposes_previous_version))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                ResetHooks(runtime);
                EnsurePartition(ctx);
                return ValueTask.CompletedTask;
            })
            .Assert(async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partitionName = EnsurePartition(ctx);
                using var lease = runtime.UsePartition(partitionName);

                var priorTitles = new List<string?>();

                LifecycleEntity.Events
                    .BeforeUpsert(async c =>
                    {
                        var prior = await c.Prior.Get();
                        priorTitles.Add(prior?.Title);
                        return c.Proceed();
                    });

                var entity = new LifecycleEntity { Title = "v1" };
                var saved = await LifecycleEntity.UpsertAsync(entity);

                var updated = new LifecycleEntity
                {
                    Id = saved.Id,
                    Title = "v2",
                    Revision = saved.Revision,
                    IsPublished = saved.IsPublished
                };

                await LifecycleEntity.UpsertAsync(updated);

                priorTitles.Should().BeEquivalentTo(new[] { null, "v1" }, options => options.WithStrictOrdering());
            })
            .RunAsync();
    }

    [Fact]
    public async Task Atomic_batch_cancellation_prevents_partial_removes()
    {
        await TestPipeline.For<EntityLifecycleSpec>(_output, nameof(Atomic_batch_cancellation_prevents_partial_removes))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                ResetHooks(runtime);
                EnsurePartition(ctx);

                LifecycleEntity.Events
                    .BeforeRemove(c =>
                    {
                        if (c.Current.Title == "Block")
                        {
                            c.Operation.RequireAtomic();
                            return new ValueTask<EntityEventResult>(c.Cancel("blocked"));
                        }

                        return new ValueTask<EntityEventResult>(c.Proceed());
                    });

                return ValueTask.CompletedTask;
            })
            .Assert(async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partitionName = EnsurePartition(ctx);
                using var lease = runtime.UsePartition(partitionName);

                var keep = await LifecycleEntity.UpsertAsync(new LifecycleEntity { Title = "Keep" });
                var block = await LifecycleEntity.UpsertAsync(new LifecycleEntity { Title = "Block" });

                var act = async () => await LifecycleEntity.Remove(new[] { keep.Id, block.Id });
                await act.Should().ThrowAsync<EntityEventBatchCancelledException>();

                var keepPersisted = await LifecycleEntity.Get(keep.Id, lease.Partition);
                keepPersisted.Should().NotBeNull();
                keepPersisted!.Title.Should().Be("Keep");

                var blockPersisted = await LifecycleEntity.Get(block.Id, lease.Partition);
                blockPersisted.Should().NotBeNull();
                blockPersisted!.Title.Should().Be("Block");
            })
            .RunAsync();
    }

    [Fact]
    public async Task Load_pipeline_can_enrich_entities()
    {
        await TestPipeline.For<EntityLifecycleSpec>(_output, nameof(Load_pipeline_can_enrich_entities))
            .Using<DataCoreRuntimeFixture>("runtime", static (ctx) => DataCoreRuntimeFixture.CreateAsync(ctx))
            .Arrange(static ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                ResetHooks(runtime);
                EnsurePartition(ctx);
                return ValueTask.CompletedTask;
            })
            .Assert(async ctx =>
            {
                var runtime = ctx.GetRequiredItem<DataCoreRuntimeFixture>("runtime");
                var partitionName = EnsurePartition(ctx);
                using var lease = runtime.UsePartition(partitionName);

                LifecycleEntity.Events
                    .AfterLoad(c =>
                    {
                        c.Current.Revision += 10;
                        return ValueTask.CompletedTask;
                    });

                var saved = await LifecycleEntity.UpsertAsync(new LifecycleEntity { Title = "Load" });

                var originalRevision = saved.Revision;
                var loaded = await LifecycleEntity.Get(saved.Id, lease.Partition);
                loaded.Should().NotBeNull();
                loaded!.Revision.Should().Be(originalRevision + 10);
            })
            .RunAsync();
    }

    private sealed class LifecycleEntity : Entity<LifecycleEntity, string>
    {
        [Identifier]
        public override string Id { get; set; } = default!;
        public string Title { get; set; } = string.Empty;
        public int Revision { get; set; }
        public bool IsPublished { get; set; }
        public new static EntityEventsBuilder<LifecycleEntity, string> Events => Entity<LifecycleEntity, string>.Events;
    }
}