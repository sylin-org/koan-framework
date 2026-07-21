using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core.Axes;
using Koan.Data.Core.Model;
using Koan.Testing.Containers;
using Xunit;

namespace Koan.Data.Axes.Integration.Tests;

/// <summary>
/// ARCH-0101 §9 (E1) — the query-RSoP against a REAL store (ARCH-0079). <see cref="DataAxis.Explain"/> renders the full
/// isolation story for a <c>[Archived]</c> entity on the isolating SQLite adapter: the composing planes, the active
/// read-scope, the adapter fail-closed satisfaction (announces RowScoped + IQueryRepository), and that the predicate
/// fully pushes down — so it is NOT a leak. It reflects the ambient recycle-bin tri-state, and a non-axis entity
/// explains as unscoped + cacheable + not-a-leak.
/// </summary>
public sealed class ExplainIntegrationSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    [Archived]
    public sealed class ExplainDoc : Entity<ExplainDoc> { public string Title { get; set; } = ""; }

    public sealed class ExplainPlain : Entity<ExplainPlain> { public string Title { get; set; } = ""; }

    [Fact(DisplayName = ".Explain renders the full RSoP on an isolating adapter — scoped, satisfied, pushable, not a leak")]
    public async Task Explain_renders_the_full_rsop()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        var x = DataAxis.Explain<ExplainDoc>(host.Services);

        x.Planes.Select(p => p.Plane).Should().Contain(new[] { "managed-field", "read-filter", "operation-override" });
        x.ReadScopedNow.Should().BeTrue();              // hide-archived active by default
        x.CacheExcluded.Should().BeTrue();
        x.Adapter.Should().NotBeNull();
        x.Adapter!.IsolationSatisfied.Should().BeTrue();   // SQLite announces RowScoped + is an IQueryRepository
        x.Adapter.FailClosedReason.Should().BeNull();
        x.Adapter.Pushable.Should().BeTrue();              // the hide-archived predicate pushes down at SQLite
        x.IsLeak.Should().BeFalse();
    }

    [Fact(DisplayName = ".Explain reflects the recycle-bin ambient tri-state")]
    public async Task Explain_reflects_the_ambient()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        DataAxis.Explain<ExplainDoc>(host.Services).ReadScopedNow.Should().BeTrue();
        using (Archived.WithDeleted())
            DataAxis.Explain<ExplainDoc>(host.Services).ReadScopedNow.Should().BeFalse();   // recycle bin open ⇒ unscoped
    }

    [Fact(DisplayName = ".Explain on a non-axis entity: unscoped, cacheable, not a leak")]
    public async Task Explain_non_axis_entity()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        var x = DataAxis.Explain<ExplainPlain>(host.Services);
        x.Planes.Should().BeEmpty();
        x.ReadScopedNow.Should().BeFalse();
        x.CacheExcluded.Should().BeFalse();
        x.Adapter.Should().NotBeNull();
        x.Adapter!.IsolationSatisfied.Should().BeTrue();   // nothing to satisfy ⇒ ok
        x.IsLeak.Should().BeFalse();
    }
}
