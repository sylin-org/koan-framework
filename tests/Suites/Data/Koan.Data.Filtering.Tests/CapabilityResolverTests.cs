using AwesomeAssertions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Capabilities;
using Xunit;

namespace Koan.Data.Filtering.Tests;

/// <summary>
/// Specs for the ARCH-0084 resolver: a provider's capabilities come from its
/// <see cref="IDescribesCapabilities"/> declaration; a source that declares none resolves to empty.
/// </summary>
public class CapabilityResolverTests
{
    private sealed class NativeRepo : IDescribesCapabilities
    {
        public void Describe(ICapabilities caps) => caps.Add(DataCaps.Query.Linq).Add(DataCaps.Write.BulkUpsert);
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
    public void DataCaps_Describe_returns_empty_when_source_declares_nothing()
    {
        var caps = DataCaps.Describe(new object(), "data.none");
        caps.All.Should().BeEmpty();
        caps.Owner.Should().Be("data.none");
    }
}
