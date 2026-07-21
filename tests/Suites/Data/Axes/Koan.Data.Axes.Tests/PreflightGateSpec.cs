using System;
using System.Linq;
using AwesomeAssertions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Axes.Tests.Support;
using Koan.Data.Core.Axes;
using Koan.Data.Core.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Axes.Tests;

/// <summary>
/// ARCH-0101 §8 (E3) — the boot pre-flight's off-gate. <see cref="DataAxisPreflight.DetectLeaks"/> is a byte-identical
/// no-op (returns empty without scanning) unless an always-on PREDICATE read contributor is registered: an
/// ambient-gated equality axis (tenancy) yields no boot fold, and an app with no axis at all is untouched. The
/// environment-aware warn/refuse behavior is proven end-to-end against a real boot in the tenancy suite.
/// </summary>
public sealed class PreflightGateSpec : IDisposable
{
    public PreflightGateSpec() => AxisRegistries.ResetAll();
    public void Dispose() => AxisRegistries.ResetAll();

    [Fact]
    public void No_read_contributor_at_all_is_a_no_op()
    {
        using var sp = new ServiceCollection().BuildServiceProvider();
        DataAxisPreflight.DetectLeaks(sp).Should().BeEmpty();
    }

    // (The "only the built-in equality contributor ⇒ no-op" case is the ambient-gated tenancy shape — proven at the
    //  integration level: all 84 tenancy boots carry the built-in equality contributor and the pre-flight never refuses.)

    [Fact]
    public void A_predicate_axis_without_a_data_service_is_a_no_op()
    {
        // The gate opens (a predicate contributor is present) but there is no IDataService to resolve adapters ⇒ empty.
        var services = new ServiceCollection();
        DataAxisExpander.ExpandAxes(new[]
        {
            new Axis().Named("archived").AppliesTo(_ => true).Reads(_ => Filter.Eq("__x", "v")),
        }, services);
        using var sp = services.BuildServiceProvider();
        DataAxisPreflight.DetectLeaks(sp).Should().BeEmpty();
    }
}
