using Koan.Data.Core.Model;
using Koan.Data.Core.Transfers;
using Koan.Tests.Data.Core.Support;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Tests.Data.Core.Specs.Transfers;

public sealed class EntityTransferDslSpec
{
    private static readonly string[] Partitions =
    [
        "active",
        "inactive",
        "hot",
        "archive",
        "batch",
        "dest",
        "mirror",
        "filtered",
        "sync",
        "sync-target",
        "reporting",
        "secondary"
    ];

    private readonly ITestOutputHelper _output;

    public EntityTransferDslSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Copy_ToPartition_CopiesFilteredEntities()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        await Reset(runtime);

        runtime.BindHost();

        using (runtime.UsePartition("active"))
        {
            await new TransferTodo { Title = "A", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
            await new TransferTodo { Title = "B", Active = false, UpdatedAt = DateTime.UtcNow }.Save();
        }

        var audits = new List<TransferAuditBatch>();
        var result = await TransferTodo.Copy(p => p.Active)
            .From(partition: "active")
            .To(partition: "inactive")
            .Audit(audits.Add)
            .Run();

        result.Kind.Should().Be(TransferKind.Copy);
        result.CopiedCount.Should().Be(1);
        result.DeletedCount.Should().Be(0);
        result.ReadCount.Should().Be(1);
        result.Warnings.Should().BeEmpty();
        audits.Should().NotBeEmpty();
        audits.Last().IsSummary.Should().BeTrue();

        using (runtime.UsePartition("inactive"))
        {
            var items = await TransferTodo.All();
            items.Should().ContainSingle(x => x.Title == "A");
        }

        using (runtime.UsePartition("active"))
        {
            var items = await TransferTodo.All();
            items.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task Copy_QueryShaper_AppliesFilter()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        await Reset(runtime);

        runtime.BindHost();

        await new TransferTodo { Title = "keep", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
        await new TransferTodo { Title = "drop", Active = true, UpdatedAt = DateTime.UtcNow }.Save();

        var result = await TransferTodo.Copy(query => query.Where(t => t.Title == "keep"))
            .To(partition: "filtered")
            .Run();

        result.CopiedCount.Should().Be(1);

        using (runtime.UsePartition("filtered"))
        {
            var items = await TransferTodo.All();
            items.Should().ContainSingle(t => t.Title == "keep");
        }
    }

    [Fact]
    public async Task Move_DefaultStrategy_RemovesFromSource()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        await Reset(runtime);

        runtime.BindHost();

        using (runtime.UsePartition("hot"))
        {
            for (var i = 0; i < 3; i++)
            {
                await new TransferTodo { Title = $"Item {i}", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
            }
        }

        var result = await TransferTodo.Move(p => true)
            .From(partition: "hot")
            .To(partition: "archive")
            .Run();

        result.Kind.Should().Be(TransferKind.Move);
        result.CopiedCount.Should().Be(3);
        result.DeletedCount.Should().Be(3);

        using (runtime.UsePartition("hot"))
        {
            (await TransferTodo.All()).Should().BeEmpty();
        }

        using (runtime.UsePartition("archive"))
        {
            (await TransferTodo.All()).Should().HaveCount(3);
        }
    }

    [Fact]
    public async Task Move_BatchedStrategy_RespectsBatching()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        await Reset(runtime);

        runtime.BindHost();

        using (runtime.UsePartition("batch"))
        {
            for (var i = 0; i < 4; i++)
            {
                await new TransferTodo { Title = $"Batch {i}", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
            }
        }

        var result = await TransferTodo.Move()
            .From(partition: "batch")
            .To(partition: "dest")
            .BatchSize(1)
            .WithDeleteStrategy(DeleteStrategy.Batched)
            .Run();

        result.CopiedCount.Should().Be(4);
        result.DeletedCount.Should().Be(4);

        using (runtime.UsePartition("batch"))
        {
            (await TransferTodo.All()).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Move_SyncedStrategy_RemovesAsItGoes()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        await Reset(runtime);

        runtime.BindHost();

        using (runtime.UsePartition("sync"))
        {
            for (var i = 0; i < 2; i++)
            {
                await new TransferTodo { Title = $"Sync {i}", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
            }
        }

        var result = await TransferTodo.Move()
            .From(partition: "sync")
            .To(partition: "sync-target")
            .BatchSize(1)
            .WithDeleteStrategy(DeleteStrategy.Synced)
            .Run();

        result.CopiedCount.Should().Be(2);
        result.DeletedCount.Should().Be(2);

        using (runtime.UsePartition("sync"))
        {
            (await TransferTodo.All()).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Mirror_Push_SynchronizesToTarget()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        await Reset(runtime);

        runtime.BindHost();

        await new TransferTodo { Title = "primary", Active = true, UpdatedAt = DateTime.UtcNow }.Save();

        var result = await TransferTodo.Mirror()
            .To(partition: "mirror")
            .Run();

        result.Kind.Should().Be(TransferKind.Mirror);
        result.CopiedCount.Should().Be(1);
        result.DeletedCount.Should().Be(0);
        result.Audit.Last().IsSummary.Should().BeTrue();

        using (runtime.UsePartition("mirror"))
        {
            (await TransferTodo.All()).Should().ContainSingle(x => x.Title == "primary");
        }
    }

    [Fact]
    public async Task Mirror_Pull_SynchronizesBackToDefault()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        await Reset(runtime);

        runtime.BindHost();

        using (runtime.UsePartition("mirror"))
        {
            await new TransferTodo { Title = "remote", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
        }

        var result = await TransferTodo.Mirror(mode: MirrorMode.Pull)
            .To(partition: "mirror")
            .Run();

        result.CopiedCount.Should().Be(1);

        var all = await TransferTodo.All();
        all.Should().ContainSingle(x => x.Title == "remote");
    }

    [Fact]
    public async Task Mirror_Bidirectional_UsesTimestampForResolution()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        await Reset(runtime);

        runtime.BindHost();

        var primary = await new TransferTodo { Title = "v1", Active = true, UpdatedAt = DateTime.UtcNow.AddMinutes(-2) }.Save();

        using (runtime.UsePartition("reporting"))
        {
            await new TransferTodo { Id = primary.Id, Title = "v2", Active = true, UpdatedAt = DateTime.UtcNow }.Save();
        }

        var result = await TransferTodo.Mirror(mode: MirrorMode.Bidirectional)
            .To(partition: "reporting")
            .Run();

        result.Conflicts.Should().BeEmpty();
        result.Audit.Last().IsSummary.Should().BeTrue();

        var updated = await TransferTodo.Get(primary.Id);
        updated.Should().NotBeNull();
        updated!.Title.Should().Be("v2");

        using (runtime.UsePartition("reporting"))
        {
            var target = await TransferTodo.Get(primary.Id);
            target.Should().NotBeNull();
            target!.Title.Should().Be("v2");
        }
    }

    [Fact]
    public async Task Mirror_Bidirectional_WithoutTimestamp_ReportsConflicts()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        await Reset(runtime);

        runtime.BindHost();

        var baseNote = await new BasicNote { Content = "default" }.Save();

        using (runtime.UsePartition("secondary"))
        {
            await new BasicNote { Id = baseNote.Id, Content = "secondary" }.Save();
        }

        var result = await BasicNote.Mirror(mode: MirrorMode.Bidirectional)
            .To(partition: "secondary")
            .Run();

        result.Conflicts.Should().NotBeEmpty();
        result.CopiedCount.Should().Be(0);
        result.Warnings.Should().Contain(w => w.Contains("No [Timestamp]"));

        var defaultNote = await BasicNote.Get(baseNote.Id);
        defaultNote.Should().NotBeNull();
        defaultNote!.Content.Should().Be("default");
    }

    [Fact]
    public async Task To_WithSourceAndAdapter_ShouldThrow()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync();
        await Reset(runtime);

        Action act = () => TransferTodo.Copy().To(source: "primary", adapter: "sqlite");
        act.Should().Throw<InvalidOperationException>();
    }

    private static async ValueTask Reset(DataCoreRuntimeFixture runtime)
    {
        runtime.ResetEntityCaches();

        await TransferTodo.RemoveAll();
        await BasicNote.RemoveAll();

        foreach (var partition in Partitions)
        {
            using var lease = runtime.UsePartition(partition);
            await TransferTodo.RemoveAll();
            await BasicNote.RemoveAll();
        }
    }

    private sealed class TransferTodo : Entity<TransferTodo>
    {
        public string Title { get; set; } = "";
        public bool Active { get; set; }

        [Timestamp(OnSave = true)]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    private sealed class BasicNote : Entity<BasicNote>
    {
        public string Content { get; set; } = "";
    }
}
