using AwesomeAssertions;
using Koan.Classification.Infrastructure;
using Koan.Core.Semantics.Segmentation;
using Xunit;

namespace Koan.Classification.Tests;

public sealed class ClassificationKeyScopeSpec
{
    [Fact]
    public void No_active_dimensions_use_the_host_scope()
        => ClassificationKeyScope.From([]).Should().Be("host");

    [Fact]
    public void Scope_is_deterministic_opaque_and_sensitive_to_dimension_values()
    {
        var tenantA = ClassificationKeyScope.From([new SegmentationBinding("tenant", "tenant-a")]);
        var tenantAAgain = ClassificationKeyScope.From([new SegmentationBinding("tenant", "tenant-a")]);
        var tenantB = ClassificationKeyScope.From([new SegmentationBinding("tenant", "tenant-b")]);

        tenantA.Should().Be(tenantAAgain).And.StartWith("seg:");
        tenantA.Should().NotBe(tenantB);
        tenantA.Should().NotContain("tenant-a");
    }

    [Fact]
    public void Length_prefixing_prevents_ambiguous_dimension_pairs()
    {
        var first = ClassificationKeyScope.From([
            new SegmentationBinding("a", "bc"),
            new SegmentationBinding("d", "e")]);
        var second = ClassificationKeyScope.From([
            new SegmentationBinding("ab", "c"),
            new SegmentationBinding("d", "e")]);

        first.Should().NotBe(second);
    }
}
