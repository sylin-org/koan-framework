using AwesomeAssertions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Abstractions.Capabilities;
using Xunit;

namespace Koan.Data.Filtering.Tests;

/// <summary>
/// Specs for the ARCH-0084 stage-(b1) resolver: a provider's capabilities come from its native
/// <see cref="IDescribesCapabilities"/> declaration when present, else from the legacy marker bridge.
/// </summary>
public class CapabilityResolverTests
{
    private sealed class NativeRepo : IDescribesCapabilities
    {
        public void Describe(ICapabilities caps) => caps.Add(DataCaps.Query.Linq).Add(DataCaps.Write.BulkUpsert);
    }

    private sealed class LegacyQueryWriteRepo : IQueryCapabilities, IWriteCapabilities
    {
        public QueryCapabilities Capabilities => QueryCapabilities.Linq | QueryCapabilities.String;
        public WriteCapabilities Writes => WriteCapabilities.BulkUpsert;
    }

    private sealed class LegacyVectorRepo : IVectorCapabilities
    {
        public VectorCapabilities Capabilities => VectorCapabilities.Knn | VectorCapabilities.Filters;
    }

    // Implements BOTH: the native declaration must win and the legacy markers must be ignored.
    private sealed class NativeAndLegacyRepo : IDescribesCapabilities, IQueryCapabilities
    {
        public void Describe(ICapabilities caps) => caps.Add(DataCaps.Query.Linq);
        public QueryCapabilities Capabilities => QueryCapabilities.String;
    }

    [Fact]
    public void DataCaps_Describe_uses_native_declaration_when_present()
    {
        var caps = DataCaps.Describe(new NativeRepo(), "data.native");
        caps.Has(DataCaps.Query.Linq).Should().BeTrue();
        caps.Has(DataCaps.Write.BulkUpsert).Should().BeTrue();
        caps.Has(DataCaps.Query.String).Should().BeFalse();
        caps.Owner.Should().Be("data.native");
    }

    [Fact]
    public void DataCaps_Describe_bridges_legacy_markers_when_not_native()
    {
        var caps = DataCaps.Describe(new LegacyQueryWriteRepo());
        caps.Has(DataCaps.Query.Linq).Should().BeTrue();
        caps.Has(DataCaps.Query.String).Should().BeTrue();
        caps.Has(DataCaps.Write.BulkUpsert).Should().BeTrue();
        caps.Has(DataCaps.Write.AtomicBatch).Should().BeFalse();
    }

    [Fact]
    public void VectorCaps_Describe_bridges_legacy_vector_marker()
    {
        var caps = VectorCaps.Describe(new LegacyVectorRepo());
        caps.Has(VectorCaps.Knn).Should().BeTrue();
        caps.Has(VectorCaps.Filters).Should().BeTrue();
        caps.Has(VectorCaps.Hybrid).Should().BeFalse();
    }

    [Fact]
    public void Native_declaration_short_circuits_the_bridge()
    {
        var caps = DataCaps.Describe(new NativeAndLegacyRepo());
        caps.Has(DataCaps.Query.Linq).Should().BeTrue();    // from Describe
        caps.Has(DataCaps.Query.String).Should().BeFalse(); // legacy String not bridged — native won
    }
}
