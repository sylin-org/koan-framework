using FluentAssertions;
using Koan.Data.AI;
using Koan.Data.AI.Attributes;
using Xunit;

namespace Koan.Data.AI.Tests;

/// <summary>
/// AI-0036 §9 D1: the embedding write path auto-stamps an entity's filterable facets as metadata
/// keyed by CLR property name, so a metadata filter (incl. the Chain lambda DX) is sound by
/// construction. These pin which properties become facets and which are excluded.
/// </summary>
public sealed class VectorFilterableMetadataSpecs
{
    private sealed class Doc
    {
        public string Id { get; set; } = "doc-1";
        public string Category { get; set; } = "legal";
        public int Year { get; set; } = 2024;
        public bool Active { get; set; } = true;
        public string[] Tags { get; set; } = { "a", "b" };
        public string Content { get; set; } = new string('x', 1000); // long -> content, not a facet
        [EmbeddingIgnore] public string Secret { get; set; } = "nope";
        public Doc? Parent { get; set; } // complex -> not a facet
    }

    [Fact]
    public void Stamps_short_scalars_strings_and_arrays_under_clr_names()
    {
        var m = VectorFilterableMetadata.Extract(new Doc())!;
        m["Category"].Should().Be("legal");
        m["Year"].Should().Be(2024);
        m["Active"].Should().Be(true);
        m["Tags"].Should().BeAssignableTo<System.Collections.IEnumerable>();
    }

    [Fact]
    public void Excludes_id_long_strings_embedding_ignore_and_complex()
    {
        var m = VectorFilterableMetadata.Extract(new Doc())!;
        m.Should().NotContainKey("Id", "Id is the vector key");
        m.Should().NotContainKey("Content", "long strings are content, not facets");
        m.Should().NotContainKey("Secret", "[EmbeddingIgnore] is excluded");
        m.Should().NotContainKey("Parent", "complex/nested objects are not facets");
    }

    [Fact]
    public void Null_entity_yields_null()
        => VectorFilterableMetadata.Extract(null).Should().BeNull();

    [Fact]
    public void Long_string_at_threshold_boundary_is_excluded()
    {
        var d = new Doc { Category = new string('y', VectorFilterableMetadata.MaxFacetLength + 1) };
        VectorFilterableMetadata.Extract(d)!.Should().NotContainKey("Category");
    }
}
