using System;
using System.Linq;
using System.Threading;
using AwesomeAssertions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Axes.Tests.Support;
using Koan.Data.Core.Axes;
using Koan.Data.Core.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Axes.Tests;

/// <summary>
/// ARCH-0101 §9 (E1/E2) — the self-reporting surface. <see cref="DataAxis.Explain"/> renders the registry-level RSoP
/// (composing planes + the ambient read-scope fold + cache-exclusion) even without a resolvable adapter; it reflects
/// the live ambient tri-state; and <see cref="DataAxisReport.Summarize"/> renders the app-wide axis listing for the
/// boot report (off = no line). Adapter fail-closed satisfaction is proven against a real store in the integration suite.
/// </summary>
public sealed class ExplainSpec : IDisposable
{
    public ExplainSpec() => AxisRegistries.ResetAll();
    public void Dispose() { _show.Value = false; AxisRegistries.ResetAll(); }

    private sealed class Doc { }
    private sealed class Plain { }
    private static readonly AsyncLocal<bool> _show = new();
    private static readonly Filter Hide = Filter.Eq("__archived", "v");

    private static ServiceProvider ExpandAndBuild(params Axis[] axes)
    {
        var services = new ServiceCollection();
        DataAxisExpander.ExpandAxes(axes, services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void No_axis_explains_as_unscoped_and_cacheable()
    {
        using var sp = ExpandAndBuild();
        var x = DataAxis.Explain(sp, typeof(Doc));
        x.Planes.Should().BeEmpty();
        x.ReadScope.Should().BeNull();
        x.ReadScopedNow.Should().BeFalse();
        x.CacheExcluded.Should().BeFalse();
        x.Adapter.Should().BeNull();      // a bare provider has no IDataService ⇒ registry-level RSoP only
        x.IsLeak.Should().BeFalse();
    }

    [Fact]
    public void A_soft_delete_shaped_axis_explains_its_full_composition()
    {
        using var sp = ExpandAndBuild(new Axis()
            .Named("archived").AppliesTo(t => t == typeof(Doc))
            .Field("__archived", () => null, typeof(bool))
            .Reads(_ => _show.Value ? null : Hide)
            .OnDelete(Logical.SetTrue("__archived")));

        var x = DataAxis.Explain(sp, typeof(Doc));
        x.Planes.Select(p => p.Plane).Should().Contain(new[] { "managed-field", "read-filter", "operation-override" });
        x.Planes.Single(p => p.Plane == "read-filter").Key.Should().Be("axis:archived");
        x.CacheExcluded.Should().BeTrue();         // non-equality predicate ⇒ excluded from cache (DATA-0106 §5)
        x.ReadScope.Should().NotBeNull();          // hide-archived active (show=false)
        x.ReadScopedNow.Should().BeTrue();
    }

    [Fact]
    public void Explain_reflects_the_ambient_tri_state()
    {
        using var sp = ExpandAndBuild(new Axis()
            .Named("archived").AppliesTo(t => t == typeof(Doc))
            .Field("__archived", () => null, typeof(bool))
            .Reads(_ => _show.Value ? null : Hide));

        _show.Value = false;
        DataAxis.Explain(sp, typeof(Doc)).ReadScopedNow.Should().BeTrue();   // hiding ⇒ scoped
        _show.Value = true;
        DataAxis.Explain(sp, typeof(Doc)).ReadScopedNow.Should().BeFalse();  // recycle bin open ⇒ unscoped
    }

    [Fact]
    public void A_non_applicable_entity_explains_as_unscoped()
    {
        using var sp = ExpandAndBuild(new Axis()
            .Named("archived").AppliesTo(t => t == typeof(Doc))
            .Field("__archived", () => null, typeof(bool)).Reads(_ => Hide));

        var x = DataAxis.Explain(sp, typeof(Plain));
        x.Planes.Should().BeEmpty();
        x.ReadScopedNow.Should().BeFalse();
        x.CacheExcluded.Should().BeFalse();
    }

    [Fact]
    public void A_field_transform_entity_is_cache_excluded_even_with_no_axis()
    {
        // CacheExcluded must mirror CachedRepository's THREE legs — a [Classified]/encrypted field transform excludes
        // from cache even with no managed axis and no excluding contributor (the security-sensitive shape).
        var services = new ServiceCollection();
        services.AddSingleton<IFieldTransformInspector>(new TestTransformInspector(typeof(Doc)));
        using var sp = services.BuildServiceProvider();
        DataAxis.Explain(sp, typeof(Doc)).CacheExcluded.Should().BeTrue();
        DataAxis.Explain(sp, typeof(Doc)).Planes.Should().ContainSingle(plane =>
            plane.Plane == "field-transform" && plane.Key == "test-transform");
        DataAxis.Explain(sp, typeof(Plain)).CacheExcluded.Should().BeFalse();
    }

    [Fact]
    public void A_scoped_entity_on_an_unresolvable_adapter_reports_as_a_leak()
    {
        // Bias-to-strict: a CONFIGURED route that fails to resolve must NOT read as safe for a scopable entity.
        var services = new ServiceCollection();
        DataAxisExpander.ExpandAxes(new[]
        {
            new Axis().Named("archived").AppliesTo(t => t == typeof(ScopedEntity))
                .Field("__archived", () => null, typeof(bool)).Reads(_ => Hide),
        }, services);
        services.AddSingleton<Koan.Data.Core.IDataService>(new ThrowingDataService());
        using var sp = services.BuildServiceProvider();

        var x = DataAxis.Explain(sp, typeof(ScopedEntity));
        x.ReadScopedNow.Should().BeTrue();
        x.Adapter.Should().NotBeNull();
        x.Adapter!.IsolationSatisfied.Should().BeFalse();
        x.Adapter.FailClosedReason.Should().Contain("could not be resolved");
        x.IsLeak.Should().BeTrue();
    }

    [Fact]
    public void A_non_scoped_entity_on_an_unresolvable_adapter_is_not_a_leak()
    {
        // A resolution failure for an entity NOTHING scopes is not a leak (couldScope=false ⇒ no adapter claim).
        var services = new ServiceCollection();
        DataAxisExpander.ExpandAxes(new[]
        {
            new Axis().Named("archived").AppliesTo(t => t == typeof(ScopedEntity))
                .Field("__archived", () => null, typeof(bool)).Reads(_ => Hide),
        }, services);
        services.AddSingleton<Koan.Data.Core.IDataService>(new ThrowingDataService());
        using var sp = services.BuildServiceProvider();

        var x = DataAxis.Explain(sp, typeof(PlainEntity));
        x.ReadScopedNow.Should().BeFalse();
        x.Adapter.Should().BeNull();
        x.IsLeak.Should().BeFalse();
    }

    private sealed class ScopedEntity : Koan.Data.Abstractions.IEntity<string> { public string Id { get; set; } = ""; }
    private sealed class PlainEntity : Koan.Data.Abstractions.IEntity<string> { public string Id { get; set; } = ""; }

    private sealed class TestTransformInspector(Type applicable) : IFieldTransformInspector
    {
        public bool HasTransformsFor(Type entityType) => entityType == applicable;
        public IReadOnlyList<string> ContributorIdsFor(Type entityType)
            => entityType == applicable ? ["test-transform"] : [];
    }

    private sealed class ThrowingDataService : Koan.Data.Core.IDataService
    {
        public Koan.Data.Abstractions.IDataRepository<TEntity, TKey> GetRepository<TEntity, TKey>()
            where TEntity : class, Koan.Data.Abstractions.IEntity<TKey> where TKey : notnull
            => throw new InvalidOperationException("no adapter");
        public Koan.Data.Core.Axes.IAxisScopeDiagnostics GetScopeDiagnostics<TEntity, TKey>()
            where TEntity : class, Koan.Data.Abstractions.IEntity<TKey> where TKey : notnull
            => throw new InvalidOperationException("No data adapter factory for provider 'ghost'");
        public Koan.Data.Core.Direct.IDirectSession Direct(string? source = null, string? adapter = null)
            => throw new NotImplementedException();
    }

    [Fact]
    public void Summarize_is_null_when_no_axis_is_registered()
        => DataAxisReport.Summarize().Should().BeNull();

    [Fact]
    public void Summarize_lists_the_active_planes()
    {
        DataAxisExpander.ExpandAxes(new[]
        {
            new Axis().Named("tenant").AppliesTo(t => t == typeof(Doc)).Field("__koan_tenant", () => "acme"),
            new Axis().Named("archived").AppliesTo(t => t == typeof(Plain))
                .Field("__archived", () => null, typeof(bool)).Reads(_ => Hide).OnDelete(Logical.SetTrue("__archived")),
        }, new ServiceCollection());

        var summary = DataAxisReport.Summarize();
        summary.Should().NotBeNull();
        summary.Should().Contain("__koan_tenant:isolation.rowScoped");
        summary.Should().Contain("__archived:isolation.rowScoped/predicate");   // AutoReadFilter=false ⇒ predicate axis
        summary.Should().Contain("operation-overrides=on");
    }
}
