using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using AwesomeAssertions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Metadata;

/// <summary>
/// DATA-0105 phase 0a: <see cref="ProjectionResolver.Get"/> is Type-plane memoized. The prior
/// implementation re-ran reflection (property scan + <see cref="IndexMetadata"/> lookup) per call; it is
/// invoked on every relational schema-ensure and several adapter write paths. This pins the projection
/// rules (behaviour-preserving) and the memoization (the observable change).
/// </summary>
public class ProjectionResolverSpec
{
    public enum Color { Red, Green }

    public class Sample : Entity<Sample>
    {
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
        public Color Hue { get; set; }

        [Index] public string Email { get; set; } = "";

        [Column("custom_col")] public string Aliased { get; set; } = "";

        [NotMapped] public string Ignored { get; set; } = "";

        public Sample? Nested { get; set; }
    }

    [Fact]
    public void Scalar_properties_are_projected_and_Id_is_excluded()
    {
        var cols = ProjectionResolver.Get(typeof(Sample));
        var names = cols.Select(c => c.Property.Name).ToList();
        names.Should().Contain(new[] { "Name", "Quantity", "Hue", "Email", "Aliased" });
        names.Should().NotContain("Id");
    }

    [Fact]
    public void NotMapped_and_non_scalar_properties_are_excluded()
    {
        var cols = ProjectionResolver.Get(typeof(Sample));
        var names = cols.Select(c => c.Property.Name).ToList();
        names.Should().NotContain("Ignored");  // [NotMapped]
        names.Should().NotContain("Nested");   // non-scalar
    }

    [Fact]
    public void Column_attribute_sets_the_column_name()
    {
        var cols = ProjectionResolver.Get(typeof(Sample));
        cols.Single(c => c.Property.Name == "Aliased").ColumnName.Should().Be("custom_col");
        cols.Single(c => c.Property.Name == "Name").ColumnName.Should().Be("Name");
    }

    [Fact]
    public void Enum_and_indexed_flags_are_set()
    {
        var cols = ProjectionResolver.Get(typeof(Sample));
        cols.Single(c => c.Property.Name == "Hue").IsEnum.Should().BeTrue();
        cols.Single(c => c.Property.Name == "Email").IsIndexed.Should().BeTrue();
        cols.Single(c => c.Property.Name == "Name").IsIndexed.Should().BeFalse();
    }

    [Fact]
    public void Get_is_memoized_per_type()
    {
        var a = ProjectionResolver.Get(typeof(Sample));
        var b = ProjectionResolver.Get(typeof(Sample));
        a.Should().BeSameAs(b);
    }

    // DATA-0105 phase 3a — the column-name + exclusion resolution converges onto ProjectionResolver: the
    // Koan-native property attributes [StorageName] (name) and [IgnoreStorage] (exclusion) — previously read
    // only by the dead RelationalModelBuilder — become live alongside the EF-standard [Column]/[NotMapped],
    // and the dead divergent resolver is deleted. No shipped entity uses the property-level Koan attributes,
    // so this is byte-identical for existing entities and a latent-capability fix for the Koan attributes.
    public class Converged : Entity<Converged>
    {
        public string Plain { get; set; } = "";
        [StorageName("sn_col")] public string ViaStorageName { get; set; } = "";
        [Column("col_attr")][StorageName("loses")] public string BothPresent { get; set; } = "";
        [IgnoreStorage] public string IgnoredViaStorage { get; set; } = "";
    }

    [Fact]
    public void StorageName_property_attribute_sets_the_column_name()
        => ProjectionResolver.Get(typeof(Converged))
            .Single(c => c.Property.Name == "ViaStorageName").ColumnName.Should().Be("sn_col");

    [Fact]
    public void Column_wins_over_StorageName_when_both_present()
        => ProjectionResolver.Get(typeof(Converged))
            .Single(c => c.Property.Name == "BothPresent").ColumnName.Should().Be("col_attr");

    [Fact]
    public void IgnoreStorage_excludes_the_property()
        => ProjectionResolver.Get(typeof(Converged))
            .Select(c => c.Property.Name).Should().NotContain("IgnoredViaStorage");

    [Fact]
    public void ColumnNameOf_and_IsExcluded_are_the_single_converged_resolver()
    {
        var via = typeof(Converged).GetProperty(nameof(Converged.ViaStorageName))!;
        var ignored = typeof(Converged).GetProperty(nameof(Converged.IgnoredViaStorage))!;
        ProjectionResolver.ColumnNameOf(via).Should().Be("sn_col");
        ProjectionResolver.IsExcludedFromStorage(ignored).Should().BeTrue();
        ProjectionResolver.IsExcludedFromStorage(via).Should().BeFalse();
    }
}
