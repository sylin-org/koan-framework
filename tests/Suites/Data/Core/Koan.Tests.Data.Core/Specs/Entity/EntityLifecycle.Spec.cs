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

    private static string NewPartition() => $"data-core-lifecycle-{Guid.CreateVersion7():n}";

    private static void ResetHooks(DataCoreRuntimeFixture runtime)
    {
        runtime.ResetEntityCaches();
        EntityEventTestHooks.Reset<LifecycleEntity, string>();
        TestHooks.ResetDataConfigs();
    }

    [Fact]
    public async Task Before_upsert_cancel_prevents_persistence()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        ResetHooks(runtime);

        LifecycleEntity.Events
            .BeforeUpsert(c =>
            {
                if (c.Current.Title.Contains("blocked", StringComparison.OrdinalIgnoreCase))
                {
                    return new ValueTask<EntityEventResult>(c.Cancel("blocked"));
                }

                return new ValueTask<EntityEventResult>(c.Proceed());
            });

        var partitionName = NewPartition();
        using var lease = runtime.UsePartition(partitionName);
        var blocked = new LifecycleEntity { Title = "Blocked Draft" };

        await Assert.ThrowsAsync<EntityEventCancelledException>(() => LifecycleEntity.Upsert(blocked));

        var all = await LifecycleEntity.All(lease.Partition);
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task Protect_all_blocks_mutations()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        ResetHooks(runtime);

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

        var entity = new LifecycleEntity { Title = "Immutable" };

        var act = async () => await LifecycleEntity.Upsert(entity);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*protected and cannot be mutated*");
    }

    [Fact]
    public async Task Allow_mutation_permits_whitelisted_changes()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        ResetHooks(runtime);
        var partitionName = NewPartition();

        using (var arrangeLease = runtime.UsePartition(partitionName))
        {
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
        }

        using var lease = runtime.UsePartition(partitionName);

        var entity = new LifecycleEntity { Title = "original" };
        var saved = await LifecycleEntity.Upsert(entity);

        saved.Title.Should().Be("mutated");

        var persisted = await LifecycleEntity.Get(saved.Id, lease.Partition);
        persisted?.Title.Should().Be("mutated");
    }

    [Fact]
    public async Task Prior_snapshot_exposes_previous_version()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        ResetHooks(runtime);

        var partitionName = NewPartition();
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
        var saved = await LifecycleEntity.Upsert(entity);

        var updated = new LifecycleEntity
        {
            Id = saved.Id,
            Title = "v2",
            Revision = saved.Revision,
            IsPublished = saved.IsPublished
        };

        await LifecycleEntity.Upsert(updated);

        priorTitles.Should().BeEquivalentTo(new[] { null, "v1" }, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Atomic_batch_cancellation_prevents_partial_removes()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        ResetHooks(runtime);

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

        var partitionName = NewPartition();
        using var lease = runtime.UsePartition(partitionName);

        var keep = await LifecycleEntity.Upsert(new LifecycleEntity { Title = "Keep" });
        var block = await LifecycleEntity.Upsert(new LifecycleEntity { Title = "Block" });

        var act = async () => await LifecycleEntity.Remove(new[] { keep.Id, block.Id });
        await act.Should().ThrowAsync<EntityEventBatchCancelledException>();

        var keepPersisted = await LifecycleEntity.Get(keep.Id, lease.Partition);
        keepPersisted.Should().NotBeNull();
        keepPersisted!.Title.Should().Be("Keep");

        var blockPersisted = await LifecycleEntity.Get(block.Id, lease.Partition);
        blockPersisted.Should().NotBeNull();
        blockPersisted!.Title.Should().Be("Block");
    }

    [Fact]
    public async Task Load_pipeline_can_enrich_entities()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        ResetHooks(runtime);

        var partitionName = NewPartition();
        using var lease = runtime.UsePartition(partitionName);

        LifecycleEntity.Events
            .AfterLoad(c =>
            {
                c.Current.Revision += 10;
                return ValueTask.CompletedTask;
            });

        var saved = await LifecycleEntity.Upsert(new LifecycleEntity { Title = "Load" });

        var originalRevision = saved.Revision;
        var loaded = await LifecycleEntity.Get(saved.Id, lease.Partition);
        loaded.Should().NotBeNull();
        loaded!.Revision.Should().Be(originalRevision + 10);
    }

    [Fact]
    public async Task UpsertIfChanged_identical_write_skips_persist_and_events()
    {
        var afterUpsertCount = 0;

        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        ResetHooks(runtime);

        LifecycleEntity.Events
            .AfterUpsert(c =>
            {
                afterUpsertCount++;
                return ValueTask.CompletedTask;
            });

        var partitionName = NewPartition();
        using var lease = runtime.UsePartition(partitionName);

        var entity = new LifecycleEntity { Title = "Initial" };
        var saved = await LifecycleEntity.Upsert(entity);
        afterUpsertCount.Should().Be(1);

        var sameData = new LifecycleEntity
        {
            Id = saved.Id,
            Title = saved.Title,
            Revision = saved.Revision,
            IsPublished = saved.IsPublished
        };

        var written = await LifecycleEntity.UpsertIfChanged(sameData);
        written.Should().BeFalse("identical data should not be persisted");
        afterUpsertCount.Should().Be(1, "AfterUpsert must not fire when write is suppressed");
    }

    [Fact]
    public async Task UpsertIfChanged_changed_write_persists_and_fires_events()
    {
        var afterUpsertCount = 0;

        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        ResetHooks(runtime);

        LifecycleEntity.Events
            .AfterUpsert(c =>
            {
                afterUpsertCount++;
                return ValueTask.CompletedTask;
            });

        var partitionName = NewPartition();
        using var lease = runtime.UsePartition(partitionName);

        var entity = new LifecycleEntity { Title = "v1" };
        var saved = await LifecycleEntity.Upsert(entity);
        afterUpsertCount.Should().Be(1);

        var updated = new LifecycleEntity
        {
            Id = saved.Id,
            Title = "v2",
            Revision = saved.Revision,
            IsPublished = saved.IsPublished
        };

        var written = await LifecycleEntity.UpsertIfChanged(updated);
        written.Should().BeTrue("changed data should be persisted");
        afterUpsertCount.Should().Be(2, "AfterUpsert must fire for a real change");

        var persisted = await LifecycleEntity.Get(saved.Id, lease.Partition);
        persisted?.Title.Should().Be("v2");
    }

    [Fact]
    public async Task UpsertIfChanged_new_entity_always_writes()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        ResetHooks(runtime);

        var partitionName = NewPartition();
        using var lease = runtime.UsePartition(partitionName);

        var entity = new LifecycleEntity { Title = "Brand New" };
        var written = await LifecycleEntity.UpsertIfChanged(entity);
        written.Should().BeTrue("a new entity with no prior always writes");

        var all = await LifecycleEntity.All(lease.Partition);
        all.Should().ContainSingle(e => e.Title == "Brand New");
    }

    [Fact]
    public async Task Upsert_is_unconditional_regardless_of_content()
    {
        var afterUpsertCount = 0;

        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();

        ResetHooks(runtime);

        LifecycleEntity.Events
            .AfterUpsert(c =>
            {
                afterUpsertCount++;
                return ValueTask.CompletedTask;
            });

        var partitionName = NewPartition();
        using var lease = runtime.UsePartition(partitionName);

        var entity = new LifecycleEntity { Title = "Stable" };
        var saved = await LifecycleEntity.Upsert(entity);

        var sameData = new LifecycleEntity
        {
            Id = saved.Id,
            Title = saved.Title,
            Revision = saved.Revision,
            IsPublished = saved.IsPublished
        };

        await LifecycleEntity.Upsert(sameData);
        afterUpsertCount.Should().Be(2, "plain Upsert always persists and fires events");
    }

    private sealed class LifecycleEntity : Entity<LifecycleEntity, string>
    {
        [Identifier]
        public override string Id { get; set; } = default!;
        public string Title { get; set; } = "";
        public int Revision { get; set; }
        public bool IsPublished { get; set; }
        public new static EntityEventsBuilder<LifecycleEntity, string> Events => Entity<LifecycleEntity, string>.Events;
    }
}
