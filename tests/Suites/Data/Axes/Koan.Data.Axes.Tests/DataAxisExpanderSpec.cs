using System;
using System.Linq;
using AwesomeAssertions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Axes.Tests.Support;
using Koan.Data.Core;
using Koan.Data.Core.Axes;
using Koan.Data.Core.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Axes.Tests;

/// <summary>
/// ARCH-0101 §7 (D0 + D2) — <see cref="DataAxisExpander"/> turns a declared <see cref="Axis"/> into registrations that
/// are <b>byte-identical</b> to the raw Phase A/B/C seams (the decisive spec). Proven structurally: an expanded
/// soft-delete-shaped axis produces the same <c>ManagedFieldDescriptor</c> + <c>OperationOverrideDescriptor</c> +
/// non-equality read contributor the hand-written <c>Koan.Data.SoftDelete</c> registrar produces; an equality axis
/// produces an auto-equality field with NO extra contributor; the off path registers nothing; N predicate axes yield
/// N distinct contributors (the TryAddEnumerable trap); and cross-axis collisions fail loud.
/// </summary>
public sealed class DataAxisExpanderSpec : IDisposable
{
    public DataAxisExpanderSpec() => AxisRegistries.ResetAll();
    public void Dispose() => AxisRegistries.ResetAll();

    private sealed class Doc { }
    private sealed class Other { }

    private static readonly Filter Hide = Filter.Eq("__archived", "v");

    // --- D0: off = structurally absent ---

    [Fact]
    public void Empty_batch_registers_nothing()
    {
        var services = new ServiceCollection();
        DataAxisExpander.ExpandAxes(Array.Empty<Axis>(), services);

        ManagedFieldRegistry.IsEmpty.Should().BeTrue();
        StorageNameParticleRegistry.IsEmpty.Should().BeTrue();
        OperationOverrideRegistry.IsEmpty.Should().BeTrue();
        services.Should().BeEmpty();
    }

    // --- D2a: a soft-delete-shaped axis expands byte-identically to the raw SoftDelete registrations ---

    [Fact]
    public void A_soft_delete_shaped_axis_expands_to_the_raw_seams()
    {
        var services = new ServiceCollection();
        DataAxisExpander.ExpandAxes(new[]
        {
            new Axis()
                .Named("archived").AppliesTo(t => t == typeof(Doc))
                .Field("__archived", () => null, typeof(bool))
                .Reads(_ => Hide)
                .OnDelete(Logical.SetTrue("__archived")),
        }, services);

        // The managed field == the raw SoftDelete __deleted descriptor shape: bool, RowScoped, indexed, AutoReadFilter
        // OFF (a .Reads predicate replaces the auto-equality), value absent on normal writes, applicable only to Doc.
        var field = ManagedFieldRegistry.ForType(typeof(Doc)).Should().ContainSingle().Subject;
        field.StorageName.Should().Be("__archived");
        field.ClrType.Should().Be(typeof(bool));
        field.RequiredCapability.Should().Be(DataCaps.Isolation.RowScoped);
        field.Indexed.Should().BeTrue();
        field.Priority.Should().Be(0);
        field.AutoReadFilter.Should().BeFalse();
        field.ValueProvider().Should().BeNull();
        ManagedFieldRegistry.ForType(typeof(Other)).Should().BeEmpty();

        // The operation override == Delete ⇒ __archived = true, applicable only to Doc.
        var ovr = OperationOverrideRegistry.ForDelete(typeof(Doc));
        ovr.Should().NotBeNull();
        ovr!.Field.Should().Be("__archived");
        ovr.OnDeleteValue.Should().Be(true);
        OperationOverrideRegistry.ForDelete(typeof(Other)).Should().BeNull();

        // The read contributor == a non-equality predicate, RowScoped, cache-excluding (the SoftDeleteReadContributor shape).
        var read = ResolveReadContributors(services).Should().ContainSingle().Subject;
        read.RequiredCapability.Should().Be(DataCaps.Isolation.RowScoped);
        read.ExcludesFromCache(typeof(Doc)).Should().BeTrue();
        read.ExcludesFromCache(typeof(Other)).Should().BeFalse();
        read.ReadFilter(typeof(Doc)).Should().BeSameAs(Hide);     // applicable ⇒ the predicate's filter
        read.ReadFilter(typeof(Other)).Should().BeNull();          // non-applicable ⇒ null (AppliesTo wrapper)
    }

    // --- D2b: an equality axis (Field only) auto-derives equality; NO extra contributor (the built-in handles it) ---

    [Fact]
    public void An_equality_axis_registers_an_auto_equality_field_and_no_extra_contributor()
    {
        var services = new ServiceCollection();
        DataAxisExpander.ExpandAxes(new[]
        {
            new Axis().Named("tenant").AppliesTo(t => t == typeof(Doc)).Field("__koan_tenant", () => "acme"),
        }, services);

        var field = ManagedFieldRegistry.ForType(typeof(Doc)).Should().ContainSingle().Subject;
        field.StorageName.Should().Be("__koan_tenant");
        field.ClrType.Should().Be(typeof(string));     // default clrType
        field.RequiredCapability.Should().Be(DataCaps.Isolation.RowScoped);
        field.Indexed.Should().BeTrue();
        field.AutoReadFilter.Should().BeTrue();         // equality fold is automatic via the built-in contributor
        field.ValueProvider().Should().Be("acme");

        // No DelegatingReadFilterContributor — the built-in ManagedEqualityReadContributor (added by data-core boot, not
        // here) folds the equality. So a .Field-only axis adds nothing to the read-contributor DI seam.
        ResolveReadContributors(services).Should().BeEmpty();
    }

    // --- D2c: the builder is order-independent — .Reads().Field() ≡ .Field().Reads() ---

    [Fact]
    public void Reads_then_Field_equals_Field_then_Reads()
    {
        AxisRegistries.ResetAll();
        DataAxisExpander.ExpandAxes(new[]
        {
            new Axis().Named("a").AppliesTo(t => t == typeof(Doc)).Reads(_ => Hide).Field("__a", () => null, typeof(bool)),
        }, new ServiceCollection());
        var readsFirst = ManagedFieldRegistry.ForType(typeof(Doc)).Single().AutoReadFilter;

        AxisRegistries.ResetAll();
        DataAxisExpander.ExpandAxes(new[]
        {
            new Axis().Named("a").AppliesTo(t => t == typeof(Doc)).Field("__a", () => null, typeof(bool)).Reads(_ => Hide),
        }, new ServiceCollection());
        var fieldFirst = ManagedFieldRegistry.ForType(typeof(Doc)).Single().AutoReadFilter;

        readsFirst.Should().BeFalse();
        fieldFirst.Should().BeFalse();   // identical regardless of verb order — derivation is a post-Declare resolve pass
    }

    // --- D2d: N predicate axes ⇒ N DISTINCT contributors (NOT collapsed by TryAddEnumerable dedup-by-type) ---

    [Fact]
    public void Multiple_reads_axes_register_distinct_contributors_not_one()
    {
        var services = new ServiceCollection();
        DataAxisExpander.ExpandAxes(new[]
        {
            new Axis().Named("a").AppliesTo(t => t == typeof(Doc)).Reads(_ => Hide),
            new Axis().Named("b").AppliesTo(t => t == typeof(Other)).Reads(_ => Hide),
            new Axis().Named("c").AppliesTo(t => t == typeof(Doc)).Reads(_ => Hide),
        }, services);

        // If the expander used TryAddEnumerable (dedup-by-impl-type) all three would collapse into one — a read-scope
        // hole. Plain Add(instance) keeps all three.
        ResolveReadContributors(services).Should().HaveCount(3);
    }

    // --- D2e: cross-axis collisions fail loud, naming the offending axes ---

    [Fact]
    public void Duplicate_axis_id_is_rejected()
        => FluentActions.Invoking(() => DataAxisExpander.ExpandAxes(new[]
        {
            new Axis().Named("dup").Field("__a", () => "v"),
            new Axis().Named("dup").Field("__b", () => "v"),
        }, new ServiceCollection()))
            .Should().Throw<InvalidOperationException>().WithMessage("*share the logical id 'dup'*");

    [Fact]
    public void Duplicate_managed_field_name_is_rejected()
        => FluentActions.Invoking(() => DataAxisExpander.ExpandAxes(new[]
        {
            new Axis().Named("a").Field("__same", () => "v"),
            new Axis().Named("b").Field("__same", () => "v"),
        }, new ServiceCollection()))
            .Should().Throw<InvalidOperationException>().WithMessage("*both declare the managed field '__same'*");

    [Fact]
    public void Duplicate_carrier_axis_key_is_rejected()
        => FluentActions.Invoking(() => DataAxisExpander.ExpandAxes(new[]
        {
            new Axis().Named("a").Field("__a", () => "v").Carries(new FakeCarrier("koan:dup")),
            new Axis().Named("b").Field("__b", () => "v").Carries(new FakeCarrier("koan:dup")),
        }, new ServiceCollection()))
            .Should().Throw<InvalidOperationException>().WithMessage("*both carry the ambient axis key 'koan:dup'*");

    [Fact]
    public void A_collision_in_the_batch_registers_nothing_first()
    {
        // Pass 1 validates + detects the collision before any registry write — never a half-applied batch.
        FluentActions.Invoking(() => DataAxisExpander.ExpandAxes(new[]
        {
            new Axis().Named("a").Field("__a", () => "v"),
            new Axis().Named("a").Field("__b", () => "v"),
        }, new ServiceCollection())).Should().Throw<InvalidOperationException>();

        ManagedFieldRegistry.IsEmpty.Should().BeTrue();
    }

    // --- D2f: CROSS-SOURCE collision — a [DataAxis] reusing a field a hand-written module already registered fails loud ---

    [Fact]
    public void A_cross_source_field_collision_is_rejected()
    {
        // Simulate a hand-written framework registrar (e.g. Koan.Data.SoftDelete) having already registered __deleted
        // into the static registry BEFORE the expander runs (the real boot order).
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor("__deleted", typeof(bool), () => null, _ => true));

        FluentActions.Invoking(() => DataAxisExpander.ExpandAxes(new[]
        {
            new Axis().Named("evil").AppliesTo(t => t == typeof(Doc)).Field("__deleted", () => null, typeof(bool)),
        }, new ServiceCollection()))
            .Should().Throw<InvalidOperationException>().WithMessage("*already registered by another source*");
    }

    [Fact]
    public void Re_entrant_same_axis_expansion_is_idempotent_not_a_collision()
    {
        // The same axis re-expanding (a second host in one process, the integration-boot reality) is NOT a cross-source
        // clash — the field-ownership ledger recognizes itself.
        static Axis Make() => new Axis().Named("a").AppliesTo(t => t == typeof(Doc)).Field("__a", () => "v");

        DataAxisExpander.ExpandAxes(new[] { Make() }, new ServiceCollection());
        FluentActions.Invoking(() => DataAxisExpander.ExpandAxes(new[] { Make() }, new ServiceCollection())).Should().NotThrow();
        ManagedFieldRegistry.ForType(typeof(Doc)).Should().ContainSingle(d => d.StorageName == "__a");
    }

    // --- D2g: the discovery skip guard — Expand(types) skips an abstract / no-parameterless-ctor axis type ---

    [Fact]
    public void Expand_skips_abstract_and_ctor_param_axis_types()
    {
        var services = new ServiceCollection();
        FluentActions.Invoking(() => DataAxisExpander.Expand(
            new[] { typeof(AbstractTestAxis), typeof(CtorParamTestAxis), typeof(GoodTestAxis) }, services))
            .Should().NotThrow();   // the two non-instantiable types are skipped, not a boot crash

        ManagedFieldRegistry.All.Select(d => d.StorageName).Should().Equal("__good");   // only the well-formed axis expanded
    }

    private static IReadFilterContributor[] ResolveReadContributors(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        return provider.GetServices<IReadFilterContributor>().ToArray();
    }

    private sealed class FakeCarrier(string key) : IAmbientSliceCarrier
    {
        public string AxisKey => key;
        public string? Capture() => null;
        public IDisposable Restore(string captured) => new Noop();
        public IDisposable Suppress() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}

// Test axis types for the Expand(types) skip-guard. Discoverable but inert in this boot-less unit project (no AddKoan ⇒
// ExpandDiscovered never runs here); they are reached only via the explicit DataAxisExpander.Expand([...]) call.
internal abstract class AbstractTestAxis : IDataAxis { public abstract void Declare(Axis axis); }
internal sealed class CtorParamTestAxis : IDataAxis
{
    public CtorParamTestAxis(int unused) => _ = unused;   // no public parameterless ctor ⇒ the expander skips it
    public void Declare(Axis axis) => axis.Named("ctorparam").Field("__cp", () => "v");
}
internal sealed class GoodTestAxis : IDataAxis
{
    public void Declare(Axis axis) => axis.Named("good").Field("__good", () => "v");
}
