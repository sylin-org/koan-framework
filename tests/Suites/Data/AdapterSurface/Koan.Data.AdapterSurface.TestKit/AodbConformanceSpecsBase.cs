using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing.Containers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>
/// The one reusable AODB conformance ledger (ARCH-0103 §6 / P5). A per-adapter cell subclasses this with the adapter's
/// container fixture and the per-source placement settings; the base proves, through a real <c>AddKoan()</c> boot, that
/// the adapter realizes ALL THREE AODB isolation modes — and that it <b>declares</b> the matching capability tokens.
/// <para>
/// This binds each token to its proof (ARCH-0094, co-defined): an adapter that declares
/// <see cref="DataCaps.Isolation.ContainerScoped"/>/<see cref="DataCaps.Isolation.DatabaseScoped"/> but does not
/// realize the mode fails the matching behavioral cell; one that realizes it but does not declare fails
/// <see cref="Declares_all_three_isolation_modes"/>. Over-claim and under-claim both go red.
/// </para>
/// <para>
/// The Shared cell delegates to the shared <see cref="ManagedFieldNoLeak"/> oracle; Container proves a distinct
/// physical container per ambient partition (isolation + concurrent no-leak); Database proves per-source physical
/// routing via the shared <see cref="ConformanceShardAxis"/> (and fail-closed on an unconfigured source). Subclasses
/// supply only <see cref="RoutedSourceSettings"/> — the adapter-specific config that places the two conformance
/// sources on distinct physical stores of the same backend.
/// </para>
/// </summary>
public abstract class AodbConformanceSpecsBase<TFixture> : KoanDataSpec<TFixture>
    where TFixture : KoanContainerFixture
{
    protected AodbConformanceSpecsBase(TFixture fixture, ITestOutputHelper output) : base(fixture, output) { }

    /// <summary>The two routed sources the Database cell drives — must match <see cref="RoutedSourceSettings"/>.</summary>
    protected const string SourceA = "conformance_a";
    protected const string SourceB = "conformance_b";

    /// <summary>A source the fixture never configures — the Database fail-closed proof routes to it.</summary>
    protected const string SourceUnconfigured = "conformance_unconfigured";

    /// <summary>
    /// Adapter-specific configuration mapping <see cref="SourceA"/> and <see cref="SourceB"/> to DISTINCT physical
    /// stores on the fixture's backend (same server/engine, different database/file/keyspace/index). Keys are under
    /// <c>Koan:Data:Sources:{conformance_a|conformance_b}:*</c>. E.g. Mongo: per-source <c>Database</c> names;
    /// SQLite: per-source files; Redis: per-source logical-database index.
    /// </summary>
    protected abstract IEnumerable<KeyValuePair<string, string?>> RoutedSourceSettings();

    /// <summary>Boot the conformance host: the fixture's Default source PLUS the two routed conformance sources, with
    /// the shared <see cref="ConformanceShardAxis"/> auto-discovered.</summary>
    private Task<BoundHost> BootConformanceAsync() => BootAsync(RoutedSourceSettings());

    // ==================== The co-definition: declare all three tokens ====================

    [Fact(DisplayName = "AODB ledger: the adapter declares all three isolation modes (RowScoped + ContainerScoped + DatabaseScoped)")]
    public async Task Declares_all_three_isolation_modes()
    {
        RequireBackingStore();
        await using var host = await BootConformanceAsync();
        var data = host.Services.GetRequiredService<IDataService>();
        var repo = data.GetRepository<ConformancePartitionDoc, string>();

        var caps = DataCaps.Describe(repo, repo.GetType().Name);
        caps.Has(DataCaps.Isolation.RowScoped).Should().BeTrue("Shared mode (FieldFilter) must be declared");
        caps.Has(DataCaps.Isolation.ContainerScoped).Should().BeTrue("Container mode (Particle) must be declared");
        caps.Has(DataCaps.Isolation.DatabaseScoped).Should().BeTrue("Database mode (Moniker) must be declared");
    }

    // ==================== Shared (FieldFilter → managed-record persistence) ====================

    [Fact(DisplayName = "AODB Shared: the framework-managed discriminator isolates reads/writes/deletes (no leak)")]
    public async Task Shared_isolation_holds()
    {
        RequireBackingStore();
        await using var host = await BootConformanceAsync();
        await ManagedFieldNoLeak.AssertNoLeakAsync();
    }

    // ==================== Container (Particle → distinct physical container per partition) ====================

    [Fact(DisplayName = "AODB Container: each ambient partition resolves to a distinct physical container (isolation + concurrent no-leak)")]
    public async Task Container_isolation_holds()
    {
        RequireBackingStore();
        await using var host = await BootConformanceAsync();

        var pA = NewPartition("ca");
        var pB = NewPartition("cb");

        ConformancePartitionDoc a, b;
        using (Lease(pA)) a = await new ConformancePartitionDoc { Title = "a" }.Save();
        using (Lease(pB)) b = await new ConformancePartitionDoc { Title = "b" }.Save();

        // Read isolation across partitions.
        using (Lease(pA))
        {
            (await ConformancePartitionDoc.All()).Select(d => d.Id).Should().Equal(a.Id);
            (await ConformancePartitionDoc.Get(b.Id)).Should().BeNull();   // pB's row is unreachable from pA
        }
        using (Lease(pB))
        {
            (await ConformancePartitionDoc.All()).Select(d => d.Id).Should().Equal(b.Id);
            (await ConformancePartitionDoc.Get(a.Id)).Should().BeNull();
        }

        // Concurrent interleaved writes across two FRESH partitions must not cross-contaminate (no shared mutable
        // per-partition state on the adapter): each partition ends with EXACTLY its own rows.
        const int perPartition = 15;
        var cA = NewPartition("cc");
        var cB = NewPartition("cd");
        await Task.WhenAll(
            Task.Run(async () =>
            {
                for (var i = 0; i < perPartition; i++)
                    using (Lease(cA)) await new ConformancePartitionDoc { Title = $"a{i}" }.Save();
            }),
            Task.Run(async () =>
            {
                for (var i = 0; i < perPartition; i++)
                    using (Lease(cB)) await new ConformancePartitionDoc { Title = $"b{i}" }.Save();
            }));

        using (Lease(cA)) (await ConformancePartitionDoc.All()).Should().HaveCount(perPartition);
        using (Lease(cB)) (await ConformancePartitionDoc.All()).Should().HaveCount(perPartition);
    }

    // ==================== Database (Moniker → per-source physical routing) ====================

    [Fact(DisplayName = "AODB Database: a Database-mode axis auto-routes by ambient shard to distinct physical sources, fail-closed on unconfigured")]
    public async Task Database_isolation_holds()
    {
        RequireBackingStore();
        await using var host = await BootConformanceAsync();

        // No explicit EntityContext.Source — only the ambient shard. The Database-mode axis derives the source.
        ConformanceShardedDoc a, b;
        using (ConformanceShardAmbient.Use(SourceA)) a = await new ConformanceShardedDoc { Title = "from-a" }.Save();
        using (ConformanceShardAmbient.Use(SourceB)) b = await new ConformanceShardedDoc { Title = "from-b" }.Save();

        using (ConformanceShardAmbient.Use(SourceA))
        {
            (await ConformanceShardedDoc.All()).Select(d => d.Title).Should().Equal("from-a");
            (await ConformanceShardedDoc.Get(b.Id)).Should().BeNull();   // b lives in the other physical source
        }
        using (ConformanceShardAmbient.Use(SourceB))
        {
            (await ConformanceShardedDoc.All()).Select(d => d.Title).Should().Equal("from-b");
            (await ConformanceShardedDoc.Get(a.Id)).Should().BeNull();
        }

        // Fail-closed (external-only posture): routing to an unconfigured source throws a self-explaining error.
        Func<Task> act = async () =>
        {
            using (ConformanceShardAmbient.Use(SourceUnconfigured))
                await new ConformanceShardedDoc { Title = "nope" }.Save();
        };
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{SourceUnconfigured}*not configured*");
    }
}
