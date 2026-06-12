using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core.Sorting;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Sorting;

/// <summary>
/// Unit tests for the structured sort contract (DATA-0092). These exercise the pure utility classes
/// directly — no AppHost, no repository, no AddKoan required.
/// </summary>
public sealed class SortContractUnitSpecs
{
    public sealed class Order
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
        public List<Sighting> Sightings { get; set; } = new();
    }

    public sealed class Sighting
    {
        public string Location { get; set; } = "";
        public DateTimeOffset LastChangedAt { get; set; }
        public int Index { get; set; }
    }

    // === MemberPathResolver ===

    [Fact]
    public void ResolveStrict_resolves_scalar_property()
    {
        var path = MemberPathResolver.ResolveStrict<Order>("Title");
        path.Members.Should().HaveCount(1);
        path.Members[0].Name.Should().Be("Title");
        path.TraversesCollection.Should().BeFalse();
        path.ValueType.Should().Be(typeof(string));
        path.DotPath.Should().Be("Title");
    }

    [Fact]
    public void ResolveStrict_resolves_dot_path_with_case_insensitivity()
    {
        var path = MemberPathResolver.ResolveStrict<Order>("createdat");
        path.DotPath.Should().Be("CreatedAt");
        path.ValueType.Should().Be(typeof(DateTimeOffset));
    }

    [Fact]
    public void ResolveStrict_resolves_collection_traversal()
    {
        var path = MemberPathResolver.ResolveStrict<Order>("Sightings.LastChangedAt");
        path.TraversesCollection.Should().BeTrue();
        path.CollectionSegmentIndex.Should().Be(1);
        path.ValueType.Should().Be(typeof(DateTimeOffset));
        path.DotPath.Should().Be("Sightings.LastChangedAt");
    }

    [Fact]
    public void ResolveStrict_throws_on_unresolvable_field()
    {
        var act = () => MemberPathResolver.ResolveStrict<Order>("Nonexistent");
        act.Should().Throw<InvalidSortFieldException>()
            .Where(ex => ex.Field == "Nonexistent" && ex.FailedSegment == "Nonexistent");
    }

    [Fact]
    public void ResolveStrict_throws_on_unresolvable_collection_segment()
    {
        var act = () => MemberPathResolver.ResolveStrict<Order>("Sightings.NonexistentField");
        act.Should().Throw<InvalidSortFieldException>()
            .Where(ex => ex.FailedSegment == "NonexistentField");
    }

    // === SortSpecParser ===

    [Fact]
    public void Parse_handles_minus_prefix_for_desc()
    {
        var specs = SortSpecParser.ParseStrict<Order>("-CreatedAt");
        specs.Should().HaveCount(1);
        specs[0].Desc.Should().BeTrue();
        specs[0].Path.DotPath.Should().Be("CreatedAt");
    }

    [Fact]
    public void Parse_handles_plus_prefix_for_asc()
    {
        var specs = SortSpecParser.ParseStrict<Order>("+Title");
        specs.Should().HaveCount(1);
        specs[0].Desc.Should().BeFalse();
        specs[0].Path.DotPath.Should().Be("Title");
    }

    [Fact]
    public void Parse_handles_no_prefix_as_asc()
    {
        var specs = SortSpecParser.ParseStrict<Order>("Title");
        specs.Should().HaveCount(1);
        specs[0].Desc.Should().BeFalse();
    }

    [Fact]
    public void Parse_handles_comma_separated_multi_field()
    {
        var specs = SortSpecParser.ParseStrict<Order>("-CreatedAt,+Title,Id");
        specs.Should().HaveCount(3);
        specs[0].Desc.Should().BeTrue();
        specs[0].Path.DotPath.Should().Be("CreatedAt");
        specs[1].Desc.Should().BeFalse();
        specs[1].Path.DotPath.Should().Be("Title");
        specs[2].Desc.Should().BeFalse();
        specs[2].Path.DotPath.Should().Be("Id");
    }

    [Fact]
    public void Parse_handles_dot_path_for_collection_with_default_aggregation()
    {
        var ascSpecs = SortSpecParser.ParseStrict<Order>("Sightings.LastChangedAt");
        ascSpecs[0].Aggregation.Should().Be(SortAggregation.Min);  // ASC → Min

        var descSpecs = SortSpecParser.ParseStrict<Order>("-Sightings.LastChangedAt");
        descSpecs[0].Aggregation.Should().Be(SortAggregation.Max);  // DESC → Max
    }

    [Fact]
    public void ParseStrict_throws_on_unresolvable_field()
    {
        var act = () => SortSpecParser.ParseStrict<Order>("NonexistentField");
        act.Should().Throw<InvalidSortFieldException>();
    }

    [Fact]
    public void ParseLenient_collects_unresolvable_fields_instead_of_throwing()
    {
        var result = SortSpecParser.ParseLenient<Order>("CreatedAt,NonexistentField,-Title");
        result.Specs.Should().HaveCount(2);
        result.Specs[0].Path.DotPath.Should().Be("CreatedAt");
        result.Specs[1].Path.DotPath.Should().Be("Title");
        result.SkippedFields.Should().ContainSingle().Which.Should().Be("NonexistentField");
    }

    [Fact]
    public void Parse_empty_or_whitespace_returns_empty_specs()
    {
        SortSpecParser.ParseStrict<Order>(null).Should().BeEmpty();
        SortSpecParser.ParseStrict<Order>("").Should().BeEmpty();
        SortSpecParser.ParseStrict<Order>("   ").Should().BeEmpty();
    }

    // === SortBuilder (LINQ surface) ===

    [Fact]
    public void SortBuilder_OrderBy_produces_asc_spec()
    {
        var specs = SortBuilder<Order>.Build(b => b.OrderBy(o => o.Title));
        specs.Should().HaveCount(1);
        specs[0].Desc.Should().BeFalse();
        specs[0].Path.DotPath.Should().Be("Title");
    }

    [Fact]
    public void SortBuilder_OrderByDescending_produces_desc_spec()
    {
        var specs = SortBuilder<Order>.Build(b => b.OrderByDescending(o => o.CreatedAt));
        specs.Should().HaveCount(1);
        specs[0].Desc.Should().BeTrue();
        specs[0].Path.DotPath.Should().Be("CreatedAt");
    }

    [Fact]
    public void SortBuilder_chained_then_produces_multi_specs_in_order()
    {
        var specs = SortBuilder<Order>.Build(b =>
            b.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Title));
        specs.Should().HaveCount(2);
        specs[0].Path.DotPath.Should().Be("CreatedAt");
        specs[0].Desc.Should().BeTrue();
        specs[1].Path.DotPath.Should().Be("Title");
        specs[1].Desc.Should().BeFalse();
    }

    // === InMemorySorter ===

    [Fact]
    public void InMemorySorter_sorts_by_scalar_asc()
    {
        var items = new[]
        {
            new Order { Title = "Charlie" },
            new Order { Title = "Alpha" },
            new Order { Title = "Bravo" }
        };
        var specs = SortSpecParser.ParseStrict<Order>("Title");
        var sorted = InMemorySorter.Apply(items, specs);
        sorted.Select(o => o.Title).Should().Equal("Alpha", "Bravo", "Charlie");
    }

    [Fact]
    public void InMemorySorter_sorts_by_scalar_desc()
    {
        var items = new[]
        {
            new Order { Title = "Alpha" },
            new Order { Title = "Charlie" },
            new Order { Title = "Bravo" }
        };
        var specs = SortSpecParser.ParseStrict<Order>("-Title");
        var sorted = InMemorySorter.Apply(items, specs);
        sorted.Select(o => o.Title).Should().Equal("Charlie", "Bravo", "Alpha");
    }

    [Fact]
    public void InMemorySorter_sorts_by_collection_path_desc_using_max_aggregation()
    {
        var items = new[]
        {
            new Order
            {
                Id = "A",
                Sightings = new List<Sighting>
                {
                    new() { LastChangedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                    new() { LastChangedAt = new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero) }
                }
            },
            new Order
            {
                Id = "B",
                Sightings = new List<Sighting>
                {
                    new() { LastChangedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) }
                }
            },
            new Order
            {
                Id = "C",
                Sightings = new List<Sighting>
                {
                    new() { LastChangedAt = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero) }
                }
            }
        };

        var specs = SortSpecParser.ParseStrict<Order>("-Sightings.LastChangedAt");
        var sorted = InMemorySorter.Apply(items, specs);
        sorted.Select(o => o.Id).Should().Equal("B", "C", "A");
    }

    [Fact]
    public void InMemorySorter_handles_multi_field_sort_stable()
    {
        var items = new[]
        {
            new Order { Id = "1", Title = "Alpha", CreatedAt = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            new Order { Id = "2", Title = "Alpha", CreatedAt = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            new Order { Id = "3", Title = "Bravo", CreatedAt = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero) }
        };
        var specs = SortSpecParser.ParseStrict<Order>("Title,-CreatedAt");
        var sorted = InMemorySorter.Apply(items, specs);
        sorted.Select(o => o.Id).Should().Equal("1", "2", "3");
    }

    [Fact]
    public void InMemorySorter_returns_source_when_specs_empty()
    {
        var items = new[] { new Order { Id = "1" }, new Order { Id = "2" } };
        var sorted = InMemorySorter.Apply(items, Array.Empty<SortSpec>());
        sorted.Select(o => o.Id).Should().Equal("1", "2");
    }
}
