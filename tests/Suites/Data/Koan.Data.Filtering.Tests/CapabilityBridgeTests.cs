using AwesomeAssertions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Vector.Abstractions.Capabilities;
using Xunit;

namespace Koan.Data.Filtering.Tests;

/// <summary>
/// Conformance specs for <see cref="FilterSupport"/> (ARCH-0084) — the one structured capability
/// detail, generalizing the entity scalar/collection split and the vector single-set form, and
/// riding on a capability token as its attached detail.
/// </summary>
public class CapabilityBridgeTests
{
    [Fact]
    public void FilterSupport_Of_preserves_scalar_collection_split()
    {
        var fs = FilterSupport.Of(
            new[] { FilterOperator.Eq, FilterOperator.In },
            new[] { FilterOperator.Has },
            nestedPaths: true, ignoreCase: false);

        fs.CanPush(FilterOperator.Eq, collectionField: false).Should().BeTrue();
        fs.CanPush(FilterOperator.Eq, collectionField: true).Should().BeFalse();   // split preserved
        fs.CanPush(FilterOperator.Has, collectionField: true).Should().BeTrue();
    }

    [Fact]
    public void FilterSupport_Uniform_uses_one_set_for_both_axes()
    {
        var fs = FilterSupport.Uniform(nestedPaths: true, ignoreCase: false, FilterOperator.Eq, FilterOperator.In);

        fs.CanPush(FilterOperator.In, collectionField: false).Should().BeTrue();
        fs.CanPush(FilterOperator.In, collectionField: true).Should().BeTrue();    // schemaless: no split
    }

    [Fact]
    public void FilterSupport_rides_on_the_filter_token_as_detail()
    {
        var fs = FilterSupport.Uniform(nestedPaths: true, ignoreCase: false, FilterOperator.Eq);
        var caps = CapabilitySet.Build("vector.test", c => c.Add(VectorCaps.Filters, fs));

        caps.Detail<FilterSupport>(VectorCaps.Filters).Should().BeSameAs(fs);
        caps.Detail<FilterSupport>(DataCaps.Query.Filter).Should().BeNull();        // not declared here
    }
}
