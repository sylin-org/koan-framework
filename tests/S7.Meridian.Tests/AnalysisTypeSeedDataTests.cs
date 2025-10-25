using System;
using System.Linq;
using FluentAssertions;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.SeedData;
using Xunit;

namespace S7.Meridian.Tests;

public sealed class AnalysisTypeSeedDataTests
{
    [Fact]
    public void SeededAnalysisTypes_ShouldIncludeTaxonomyDefinitions()
    {
        var types = AnalysisTypeSeedData.GetAnalysisTypes();

        types.Should().NotBeEmpty("seed data must include at least one analysis type");

        foreach (var type in types)
        {
            type.FactCategories.Should().NotBeEmpty($"{type.Id} should declare fact categories");
            type.FieldMappings.Should().NotBeEmpty($"{type.Id} should declare field mappings");

            foreach (var mapping in type.FieldMappings)
            {
                var category = type.FactCategories.FirstOrDefault(c => string.Equals(c.Id, mapping.CategoryId, StringComparison.OrdinalIgnoreCase));
                category.Should().NotBeNull($"Mapping {mapping.FieldPath} references category {mapping.CategoryId}");

                category!.Attributes.Should().NotBeEmpty();
                category.Attributes.Should().Contain(attr => string.Equals(attr.Id, mapping.AttributeId, StringComparison.OrdinalIgnoreCase));

                var canonical = FieldPathCanonicalizer.Canonicalize(mapping.FieldPath);
                canonical.Should().NotBeNullOrWhiteSpace();
            }
        }
    }
}
