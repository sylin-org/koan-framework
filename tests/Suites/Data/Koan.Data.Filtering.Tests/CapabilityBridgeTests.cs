using AwesomeAssertions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Xunit;

namespace Koan.Data.Filtering.Tests;

/// <summary>
/// Conformance specs for the ARCH-0084 enum↔token bridge and <see cref="FilterSupport"/>. These
/// pin the migration scaffolding so the legacy flag enums and the unified model stay in lockstep
/// until the legacy enums are retired in the delete stage.
/// </summary>
public class CapabilityBridgeTests
{
    [Fact]
    public void Query_enum_round_trips_through_tokens()
    {
        var flags = QueryCapabilities.Linq | QueryCapabilities.String;
        var caps = CapabilitySet.Build("data.test", c => { foreach (var t in DataCaps.From(flags)) c.Add(t); });

        caps.Has(DataCaps.Query.Linq).Should().BeTrue();
        caps.Has(DataCaps.Query.String).Should().BeTrue();
        caps.Has(DataCaps.Query.FastCount).Should().BeFalse();
        DataCaps.ToQueryCapabilities(caps).Should().Be(flags);
    }

    [Fact]
    public void Write_enum_round_trips_through_tokens()
    {
        var flags = WriteCapabilities.BulkUpsert | WriteCapabilities.AtomicBatch | WriteCapabilities.FastRemove;
        var caps = CapabilitySet.Build(null, c => { foreach (var t in DataCaps.From(flags)) c.Add(t); });

        DataCaps.ToWriteCapabilities(caps).Should().Be(flags);
    }

    [Fact]
    public void Vector_enum_round_trips_through_tokens()
    {
        var flags = VectorCapabilities.Knn | VectorCapabilities.Filters
                    | VectorCapabilities.BulkUpsert | VectorCapabilities.DynamicCollections;
        var caps = CapabilitySet.Build(null, c => { foreach (var t in VectorCaps.From(flags)) c.Add(t); });

        caps.All.Should().HaveCount(4);
        VectorCaps.ToVectorCapabilities(caps).Should().Be(flags);
    }

    [Fact]
    public void FilterSupport_from_entity_record_preserves_scalar_collection_split()
    {
        var fc = new FilterCapabilities(
            ScalarOperators: new HashSet<FilterOperator> { FilterOperator.Eq, FilterOperator.In },
            CollectionOperators: new HashSet<FilterOperator> { FilterOperator.Has },
            NestedPaths: true, IgnoreCase: false);

        var fs = FilterSupport.From(fc);
        fs.CanPush(FilterOperator.Eq, collectionField: false).Should().BeTrue();
        fs.CanPush(FilterOperator.Eq, collectionField: true).Should().BeFalse();   // split preserved
        fs.CanPush(FilterOperator.Has, collectionField: true).Should().BeTrue();
    }

    [Fact]
    public void FilterSupport_from_vector_record_uses_one_set_for_both_axes()
    {
        var vfc = VectorFilterCapabilities.Of(nestedPaths: true, ignoreCase: false, FilterOperator.Eq, FilterOperator.In);

        var fs = FilterSupport.From(vfc);
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
