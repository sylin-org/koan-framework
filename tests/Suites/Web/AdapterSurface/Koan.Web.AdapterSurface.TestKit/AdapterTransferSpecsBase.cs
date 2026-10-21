using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Xunit;

namespace Koan.Web.AdapterSurface.TestKit;

/// <summary>
/// Cross-partition transfer specs. Validates Entity&lt;T&gt;.Copy() / Move() / Mirror() and the
/// the equivalent generic Data&lt;T,K&gt; transfer entry points.
/// </summary>
public abstract class AdapterTransferSpecsBase<TFactory> : IClassFixture<TFactory>, IAsyncLifetime
    where TFactory : class, IAdapterTestFactory
{
    protected readonly TFactory Factory;
    private IDisposable? _scope;

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
        "to-clear", "same", "own", "ambient", "Cased", "cased"
    };

    public async ValueTask InitializeAsync()
    {
        if (!Factory.IsAvailable) return;
        // See AdapterSurfaceSpecsBase for the Phase 1c rationale on this reset.
        Koan.Data.Core.AggregateConfigs.Reset();
        _scope = AppHost.PushScope(Factory.Services);
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
            try { await Data<Widget, string>.DeleteAll(partition); } catch { }
        }
    }

    public ValueTask DisposeAsync()
    {
        _scope?.Dispose();
        _scope = null;
        return ValueTask.CompletedTask;
    }

    protected void SkipIfUnavailable()
        => Assert.SkipWhen(!Factory.IsAvailable, $"[{typeof(TFactory).Name}] {Factory.UnavailableReason ?? "Adapter infrastructure unavailable"}");

    protected void SkipIfTransferUnsupported()
        => Assert.SkipWhen(!Factory.SupportsCrossPartitionTransfer, $"[{typeof(TFactory).Name}] does not support cross-partition transfer.");

    /// <summary>
    /// Whether this adapter advertises provider-bounded paging (DATA-0107). Qualified adapters stream a
    /// transfer's source read; the three that do not — InMemory, JSON, Redis — materialize it instead and
    /// must say so on the result.
    /// </summary>
    protected virtual bool ProviderStreamsAreBounded => true;

    [Fact]
    public async Task Transfer_reports_a_materialized_read_exactly_when_the_provider_cannot_stream()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        await SeedPartition("src", count: 3);

        var result = await Data<Widget, string>.Copy()
            .From(partition: "src")
            .To(partition: "dst")
            .Run();

        result.CopiedCount.Should().Be(3);

        var materialized = result.Warnings.Any(
            warning => warning.Contains("provider-bounded paging", StringComparison.Ordinal));

        // Both directions matter. A qualified adapter must still stream, or the DATA-0107 guarantee has
        // quietly become "everything materializes"; an unqualified one must never materialize silently.
        materialized.Should().Be(
            !ProviderStreamsAreBounded,
            ProviderStreamsAreBounded
                ? "a stream-qualified adapter must keep streaming the source read"
                : "materializing on an unqualified adapter has to be reported, never silent");
    }

    // ============================================================================================
    // Entity<T>.Copy().From().To().Run()
    // ============================================================================================

    [Fact]
    public async Task EntityCopy_moves_rows_to_target_partition_and_leaves_source_intact()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

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

    [Fact]
    public async Task EntityMove_transfers_rows_and_clears_source_partition()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

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

    [Fact]
    public async Task EntityCopy_with_predicate_transfers_only_matching_rows()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

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
    // Data<T,K>.Copy (generic entry point)
    // ============================================================================================

    [Fact]
    public async Task DataCopy_returns_copied_count_and_preserves_source()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        await SeedPartition("src-direct", count: 5);

        var copied = (await Data<Widget, string>.Copy()
            .From(partition: "src-direct")
            .To(partition: "dst-direct")
            .Run()).CopiedCount;

        copied.Should().Be(5);
        (await CountIn("src-direct")).Should().Be(5);
        (await CountIn("dst-direct")).Should().Be(5);
    }

    // ============================================================================================
    // Data<T,K>.Move (generic entry point)
    // ============================================================================================

    [Fact]
    public async Task DataMove_returns_moved_count_and_drains_source()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        await SeedPartition("src-move", count: 3);

        var moved = (await Data<Widget, string>.Move()
            .From(partition: "src-move")
            .To(partition: "dst-move")
            .Run()).CopiedCount;

        moved.Should().Be(3);
        (await CountIn("src-move")).Should().Be(0);
        (await CountIn("dst-move")).Should().Be(3);
    }

    // ============================================================================================
    // Compact generic builders
    // ============================================================================================

    [Fact]
    public async Task Data_copy_builder_preserves_source()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        await SeedPartition("fl-src", count: 2);

        _ = await Data<Widget, string>.Copy()
            .From(partition: "fl-src")
            .To(partition: "fl-dst")
            .Batch(10)
            .Run();

        // Copy() keeps source intact.
        (await CountIn("fl-src")).Should().Be(2);
        (await CountIn("fl-dst")).Should().Be(2);
    }

    [Fact]
    public async Task Data_move_builder_drains_source()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        await SeedPartition("fl-src-move", count: 2);

        _ = await Data<Widget, string>.Move()
            .From(partition: "fl-src-move")
            .To(partition: "fl-dst-move")
            .Run();

        (await CountIn("fl-src-move")).Should().Be(0);
        (await CountIn("fl-dst-move")).Should().Be(2);
    }

    // ============================================================================================
    // Existing partition-aware deletion
    // ============================================================================================

    [Fact]
    public async Task DeleteAll_partition_removes_all_partition_rows()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        await SeedPartition("to-clear", count: 4);

        var removed = await Data<Widget, string>.DeleteAll("to-clear");

        removed.Should().BeGreaterThanOrEqualTo(0); // some adapters report removed count, others 0
        (await CountIn("to-clear")).Should().Be(0);
    }

    // ============================================================================================
    // No-op: src == dst
    // ============================================================================================

    [Fact]
    public async Task Copy_same_source_and_target_is_noop()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        await SeedPartition("same", count: 2);

        var copied = (await Data<Widget, string>.Copy()
            .From(partition: "same")
            .To(partition: "same")
            .Run()).CopiedCount;
        copied.Should().Be(0);
        (await CountIn("same")).Should().Be(2);
    }

    // ============================================================================================
    // One physical container, two spellings (NK-006 / NK-048, DATA-0125)
    // ============================================================================================

    [Fact]
    public async Task Move_onto_its_own_partition_is_refused_and_keeps_every_row()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        await SeedPartition("own", count: 2);

        var refusal = await Record.ExceptionAsync(() => Data<Widget, string>.Move()
            .From(partition: "own")
            .To(partition: "own")
            .Run());

        (await CountIn("own")).Should().Be(2);
        refusal.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Move_whose_destination_inherits_the_ambient_partition_is_refused()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        await SeedPartition("ambient", count: 2);

        Exception? refusal;
        using (EntityContext.With(partition: "ambient"))
            refusal = await Record.ExceptionAsync(() => Entity<Widget>.Move()
                .From(partition: "ambient")
                .To()
                .Run());

        (await CountIn("ambient")).Should().Be(2, "an unset To() axis inherits the ambient partition");
        refusal.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Move_between_partitions_that_differ_only_by_case_is_refused()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        await SeedPartition("Cased", count: 2);

        var refusal = await Record.ExceptionAsync(() => Data<Widget, string>.Move()
            .From(partition: "Cased")
            .To(partition: "cased")
            .Run());

        (await CountIn("Cased")).Should().Be(2, "several stores resolve both spellings to one container");
        refusal.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Mirror_onto_its_own_partition_is_refused()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        await SeedPartition("own", count: 2);

        var refusal = await Record.ExceptionAsync(() => Data<Widget, string>.Mirror()
            .From(partition: "own")
            .To(partition: "own")
            .Run());

        refusal.Should().BeOfType<InvalidOperationException>();
        (await CountIn("own")).Should().Be(2);
    }

    [Fact]
    public async Task Copy_onto_its_own_partition_reports_that_it_wrote_nothing()
    {
        SkipIfTransferUnsupported();
        SkipIfUnavailable();

        await SeedPartition("own", count: 2);

        var result = await Data<Widget, string>.Copy()
            .From(partition: "own")
            .To(partition: "own")
            .Run();

        result.CopiedCount.Should().Be(0);
        result.Warnings.Should().ContainSingle(warning => warning.Contains("was skipped", StringComparison.Ordinal));
        (await CountIn("own")).Should().Be(2);
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
