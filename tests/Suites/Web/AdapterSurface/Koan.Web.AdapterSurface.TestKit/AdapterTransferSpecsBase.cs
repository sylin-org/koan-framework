using FluentAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Xunit;

namespace Koan.Web.AdapterSurface.TestKit;

/// <summary>
/// Cross-partition transfer specs. Validates Entity&lt;T&gt;.Copy() / Move() / Mirror() and the
/// direct Data&lt;T,K&gt;.CopyPartition / MovePartition / ReplacePartition / MoveFrom().To() APIs.
/// </summary>
public abstract class AdapterTransferSpecsBase<TFactory> : IClassFixture<TFactory>, IAsyncLifetime
    where TFactory : class, IAdapterTestFactory
{
    protected readonly TFactory Factory;

    protected AdapterTransferSpecsBase(TFactory factory) => Factory = factory;

    /// <summary>
    /// Test partitions exercised by these specs. Override to extend / replace.
    /// InitializeAsync clears each before every test for isolation.
    /// </summary>
    protected virtual IReadOnlyList<string> KnownPartitions { get; } = new[]
    {
        "src", "dst", "staging", "prod", "mixed", "high",
        "src-direct", "dst-direct", "src-move", "dst-move",
        "fl-src", "fl-dst", "fl-src-move", "fl-dst-move",
        "to-clear", "same"
    };

    public async Task InitializeAsync()
    {
        if (!Factory.IsAvailable) return;
        AppHost.Current = Factory.Services;
        await Factory.ResetAsync();

        try
        {
            await Koan.Data.Core.Data<Widget, string>.Execute<int>(
                new Koan.Data.Abstractions.Instructions.Instruction("data.ensureCreated"));
        }
        catch { /* not all adapters support this instruction */ }

        // Relational adapters need ensureCreated under each partition context — partition-suffixed
        // tables aren't auto-created on first write. See AdapterPartitionSpecsBase for full context.
        foreach (var partition in KnownPartitions)
        {
            try
            {
                using var _ = EntityContext.With(partition: partition);
                await Data<Widget, string>.Execute<int>(
                    new Koan.Data.Abstractions.Instructions.Instruction("data.ensureCreated"));
            }
            catch { /* adapter may not support per-partition schema or the instruction */ }
        }

        // Each transfer spec needs both source and destination empty.
        foreach (var partition in KnownPartitions)
        {
            try { await Data<Widget, string>.ClearPartition(partition); } catch { }
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    protected void SkipIfUnavailable()
        => Skip.If(!Factory.IsAvailable, $"[{typeof(TFactory).Name}] {Factory.UnavailableReason ?? "Adapter infrastructure unavailable"}");

    protected void SkipIfTransferUnsupported()
        => Skip.If(!Factory.SupportsCrossPartitionTransfer, $"[{typeof(TFactory).Name}] does not support cross-partition transfer.");

    // ============================================================================================
    // Entity<T>.Copy().From().To().Run()
    // ============================================================================================

    [SkippableFact]
    public async Task EntityCopy_moves_rows_to_target_partition_and_leaves_source_intact()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        AppHost.Current = Factory.Services;
        await SeedPartition("src", count: 3);

        var result = await Entity<Widget>.Copy()
            .From(partition: "src")
            .To(partition: "dst")
            .Run();

        result.CopiedCount.Should().Be(3);
        result.DeletedCount.Should().Be(0);

        (await CountIn("src")).Should().Be(3);
        (await CountIn("dst")).Should().Be(3);
    }

    // ============================================================================================
    // Entity<T>.Move().From().To().Run()
    // ============================================================================================

    [SkippableFact]
    public async Task EntityMove_transfers_rows_and_clears_source_partition()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        AppHost.Current = Factory.Services;
        await SeedPartition("staging", count: 4);

        var result = await Entity<Widget>.Move()
            .From(partition: "staging")
            .To(partition: "prod")
            .Run();

        result.CopiedCount.Should().Be(4);
        result.DeletedCount.Should().Be(4);

        (await CountIn("staging")).Should().Be(0);
        (await CountIn("prod")).Should().Be(4);
    }

    // ============================================================================================
    // Entity<T>.Copy(predicate).From().To().Run() — filtered transfer
    // ============================================================================================

    [SkippableFact]
    public async Task EntityCopy_with_predicate_transfers_only_matching_rows()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        AppHost.Current = Factory.Services;
        using (EntityContext.With(partition: "mixed"))
        {
            await Widget.Upsert(new Widget { Id = "lo-1", Name = "Lo", Priority = 1 });
            await Widget.Upsert(new Widget { Id = "lo-2", Name = "Lo", Priority = 2 });
            await Widget.Upsert(new Widget { Id = "hi-1", Name = "Hi", Priority = 99 });
        }

        var result = await Entity<Widget>.Copy(w => w.Priority >= 10)
            .From(partition: "mixed")
            .To(partition: "high")
            .Run();

        result.CopiedCount.Should().Be(1);
        (await CountIn("high")).Should().Be(1);
    }

    // ============================================================================================
    // Data<T,K>.CopyPartition (direct API)
    // ============================================================================================

    [SkippableFact]
    public async Task DataCopyPartition_returns_copied_count_and_preserves_source()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        AppHost.Current = Factory.Services;
        await SeedPartition("src-direct", count: 5);

        var copied = await Data<Widget, string>.CopyPartition("src-direct", "dst-direct");

        copied.Should().Be(5);
        (await CountIn("src-direct")).Should().Be(5);
        (await CountIn("dst-direct")).Should().Be(5);
    }

    // ============================================================================================
    // Data<T,K>.MovePartition (direct API)
    // ============================================================================================

    [SkippableFact]
    public async Task DataMovePartition_returns_moved_count_and_drains_source()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        AppHost.Current = Factory.Services;
        await SeedPartition("src-move", count: 3);

        var moved = await Data<Widget, string>.MovePartition("src-move", "dst-move");

        moved.Should().Be(3);
        (await CountIn("src-move")).Should().Be(0);
        (await CountIn("dst-move")).Should().Be(3);
    }

    // ============================================================================================
    // Data<T,K>.MoveFrom(...).To(...) fluent builder
    // ============================================================================================

    [SkippableFact]
    public async Task MoveFrom_fluent_builder_copies_with_Copy_flag()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        AppHost.Current = Factory.Services;
        await SeedPartition("fl-src", count: 2);

        var result = await Data<Widget, string>.MoveFrom("fl-src")
            .Copy()
            .BatchSize(10)
            .To("fl-dst");

        // Copy() keeps source intact.
        (await CountIn("fl-src")).Should().Be(2);
        (await CountIn("fl-dst")).Should().Be(2);
    }

    [SkippableFact]
    public async Task MoveFrom_fluent_builder_move_drains_source()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        AppHost.Current = Factory.Services;
        await SeedPartition("fl-src-move", count: 2);

        await Data<Widget, string>.MoveFrom("fl-src-move").To("fl-dst-move");

        (await CountIn("fl-src-move")).Should().Be(0);
        (await CountIn("fl-dst-move")).Should().Be(2);
    }

    // ============================================================================================
    // Data<T,K>.ClearPartition
    // ============================================================================================

    [SkippableFact]
    public async Task ClearPartition_removes_all_partition_rows()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        AppHost.Current = Factory.Services;
        await SeedPartition("to-clear", count: 4);

        var removed = await Data<Widget, string>.ClearPartition("to-clear");

        removed.Should().BeGreaterThanOrEqualTo(0); // some adapters report removed count, others 0
        (await CountIn("to-clear")).Should().Be(0);
    }

    // ============================================================================================
    // No-op: src == dst
    // ============================================================================================

    [SkippableFact]
    public async Task CopyPartition_same_source_and_target_is_noop()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        AppHost.Current = Factory.Services;
        await SeedPartition("same", count: 2);

        var copied = await Data<Widget, string>.CopyPartition("same", "same");
        copied.Should().Be(0);
        (await CountIn("same")).Should().Be(2);
    }

    // ============================================================================================
    // Helpers
    // ============================================================================================

    private async Task SeedPartition(string partition, int count)
    {
        using var _ = EntityContext.With(partition: partition);
        for (var i = 0; i < count; i++)
        {
            await Widget.Upsert(new Widget { Id = $"{partition}-{i:D2}", Name = $"{partition}-{i}", Priority = i });
        }
    }

    private async Task<int> CountIn(string partition)
    {
        using var _ = EntityContext.With(partition: partition);
        var all = await Data<Widget, string>.All();
        return all.Count;
    }
}
