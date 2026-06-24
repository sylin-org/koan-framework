using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Context;

/// <summary>
/// ARCH-0100: the durable ambient carrier. A cross-cutting module registers an <see cref="IAmbientSliceCarrier"/>;
/// the <see cref="AmbientCarrierRegistry"/> snapshots all carriable ambient slices into a portable bag at submit
/// and rehydrates them at execute — surviving the async-hop the <see cref="EntityContext"/> AsyncLocal cannot.
/// Pins capture→bag→restore round-trip across a simulated hop, the null/empty/unregistered-axis trichotomy
/// (fail-closed on an unregistered axis), reverse-order + partial-failure unwind, and registration-order determinism.
/// </summary>
public class AmbientCarrierRegistrySpec
{
    // Fake carriers ride the real EntityContext slice machinery, so the round-trip is faithful.
    private sealed record FooSlice(string V);
    private sealed record BarSlice(string V);

    private sealed class FooCarrier : IAmbientSliceCarrier
    {
        public string AxisKey => "test:foo";
        public string? Capture() => EntityContext.GetSlice<FooSlice>()?.V;
        public IDisposable Restore(string captured) => EntityContext.WithSlice(new FooSlice(captured));
        public IDisposable Suppress() => EntityContext.WithSlice<FooSlice>(null);
    }

    private sealed class BarCarrier : IAmbientSliceCarrier
    {
        public string AxisKey => "test:bar";
        public string? Capture() => EntityContext.GetSlice<BarSlice>()?.V;
        public IDisposable Restore(string captured) => EntityContext.WithSlice(new BarSlice(captured));
        public IDisposable Suppress() => EntityContext.WithSlice<BarSlice>(null);
    }

    private sealed class ThrowingCarrier : IAmbientSliceCarrier
    {
        public string AxisKey => "test:throw";
        public string? Capture() => "x";
        public IDisposable Restore(string captured) => throw new InvalidOperationException("boom");
        public IDisposable Suppress() => EntityContext.WithSlice<BarSlice>(null);
    }

    private static AmbientCarrierRegistry Registry(params IAmbientSliceCarrier[] carriers) => new(carriers);

    [Fact]
    public void Capture_returns_null_when_no_slice_in_scope()
    {
        // The hot-path common case: a registered carrier, but nothing in scope → null (no allocation, no field).
        Registry(new FooCarrier()).Capture().Should().BeNull();
    }

    [Fact]
    public void Capture_returns_null_when_registry_is_empty()
        => Registry().Capture().Should().BeNull();

    [Fact]
    public void Capture_includes_only_carriers_that_yield_a_value()
    {
        using (EntityContext.WithSlice(new FooSlice("acme")))
        {
            var bag = Registry(new FooCarrier(), new BarCarrier()).Capture();   // only Foo is in scope
            bag.Should().NotBeNull();
            bag!.Should().ContainKey("test:foo").WhoseValue.Should().Be("acme");
            bag.Should().NotContainKey("test:bar");
        }
    }

    [Fact]
    public void Capture_then_Restore_round_trips_a_slice_across_a_hop()
    {
        var registry = Registry(new FooCarrier());

        IReadOnlyDictionary<string, string>? bag;
        using (EntityContext.WithSlice(new FooSlice("acme")))
            bag = registry.Capture();                                   // submit-side: slice present

        EntityContext.GetSlice<FooSlice>().Should().BeNull();           // the hop lost the AsyncLocal
        using (registry.Restore(bag))                                   // execute-side: rehydrate
            EntityContext.GetSlice<FooSlice>()!.V.Should().Be("acme");
        EntityContext.GetSlice<FooSlice>().Should().BeNull();           // restored scope unwound
    }

    [Fact]
    public void Restore_of_null_bag_suppresses_the_axis_and_does_not_inherit()
    {
        // The inline-drain hole: a null bag must CLEAR the axis, not inherit the caller's ambient. Set an outer
        // slice (the carrier-thread ambient), restore a null bag, and assert the axis is suppressed to null.
        using (EntityContext.WithSlice(new FooSlice("carrier-thread")))
        using (Registry(new FooCarrier()).Restore(null))
            EntityContext.GetSlice<FooSlice>().Should().BeNull();   // suppressed, NOT inherited
    }

    [Fact]
    public void Restore_of_empty_bag_suppresses_the_axis_and_does_not_inherit()
    {
        using (EntityContext.WithSlice(new FooSlice("carrier-thread")))
        using (Registry(new FooCarrier()).Restore(new Dictionary<string, string>()))
            EntityContext.GetSlice<FooSlice>().Should().BeNull();
    }

    [Fact]
    public void Restore_with_no_registered_carriers_is_a_true_noop()
        // The non-tenancy hot path: nothing to restore or suppress, no allocation, never throws.
        => Registry().Restore(null).Should().NotBeNull();

    [Fact]
    public void Restore_throws_fail_closed_on_an_unregistered_axis()
    {
        // The carrier's one self-owned fail-closed decision: a captured axis with no carrier here must NOT
        // silently drop and run fail-open.
        var bag = new Dictionary<string, string> { ["test:unknown"] = "v" };
        var act = () => Registry(new FooCarrier()).Restore(bag);
        act.Should().Throw<AmbientCarrierException>().Which.Message.Should().Contain("test:unknown");
    }

    [Fact]
    public void Restore_restores_multiple_axes_and_unwinds_all_on_dispose()
    {
        var registry = Registry(new FooCarrier(), new BarCarrier());
        var bag = new Dictionary<string, string> { ["test:foo"] = "f", ["test:bar"] = "b" };

        using (registry.Restore(bag))
        {
            EntityContext.GetSlice<FooSlice>()!.V.Should().Be("f");
            EntityContext.GetSlice<BarSlice>()!.V.Should().Be("b");
        }
        EntityContext.GetSlice<FooSlice>().Should().BeNull();
        EntityContext.GetSlice<BarSlice>().Should().BeNull();
    }

    [Fact]
    public void Restore_unwinds_already_acquired_scopes_when_a_later_carrier_throws()
    {
        // Deterministic registration-order restore: Foo (succeeds) before the throwing carrier — so when the
        // throw fires, Foo's scope must already be unwound (no leaked ambient).
        var registry = Registry(new FooCarrier(), new ThrowingCarrier());
        var bag = new Dictionary<string, string> { ["test:foo"] = "f", ["test:throw"] = "x" };

        var act = () => registry.Restore(bag);
        act.Should().Throw<InvalidOperationException>();
        EntityContext.GetSlice<FooSlice>().Should().BeNull();          // partial restore unwound
    }

    [Fact]
    public void Duplicate_axis_keys_are_rejected_at_construction()
    {
        var act = () => new AmbientCarrierRegistry(new IAmbientSliceCarrier[] { new FooCarrier(), new FooCarrier() });
        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("test:foo");
    }

    [Fact]
    public void Restore_fails_closed_before_pushing_when_a_registered_axis_accompanies_an_unregistered_one()
    {
        // A real axis is present alongside an unregistered one. Fail-closed must fire BEFORE anything is pushed —
        // so no partial ambient is left in scope after the throw.
        var bag = new Dictionary<string, string> { ["test:foo"] = "f", ["test:unknown"] = "v" };
        var act = () => Registry(new FooCarrier()).Restore(bag);
        act.Should().Throw<AmbientCarrierException>();
        EntityContext.GetSlice<FooSlice>().Should().BeNull();   // nothing left pushed
    }

    [Fact]
    public async Task Restore_isolates_parallel_contexts()
    {
        var registry = Registry(new FooCarrier());

        async Task<string?> Scope(string v)
        {
            using (registry.Restore(new Dictionary<string, string> { ["test:foo"] = v }))
            {
                await Task.Yield();
                return EntityContext.GetSlice<FooSlice>()?.V;
            }
        }

        var results = await Task.WhenAll(Scope("a"), Scope("b"), Scope("c"));
        results.OrderBy(x => x).Should().Equal("a", "b", "c");   // each parallel flow saw only its own restored slice
    }
}
