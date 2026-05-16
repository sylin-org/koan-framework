using Koan.Data.AI;
using Koan.Data.AI.Attributes;
using Koan.Data.Core.Model;
using FluentAssertions;

namespace Koan.Tests.AI.Unit.Specs.Embedding;

/// <summary>
/// Tests for ADR AI-0021 convention defaults: EmbeddingMetadata.Resolve() on undecorated entities.
/// Validates that on-demand operations work without [Embedding] attribute via convention inference.
/// </summary>
[Trait("ADR", "AI-0021")]
[Trait("Category", "Unit")]
public sealed class EmbeddingConventionDefaultsSpec
{
    // ========================================================================
    // Convention Inference
    // ========================================================================

    [Fact]
    public void Resolve_on_undecorated_entity_returns_convention_metadata()
    {
        var metadata = EmbeddingMetadata.Resolve<UndecoratedEntity>();

        metadata.Should().NotBeNull("Resolve() should never return null");
        metadata.Policy.Should().Be(EmbeddingPolicy.AllStrings);
        metadata.HasAttribute.Should().BeFalse();
        metadata.LifecycleEnabled.Should().BeFalse("no [Embedding] = no auto-embed-on-save");
    }

    [Fact]
    public void Resolve_on_decorated_entity_returns_attribute_metadata()
    {
        var metadata = EmbeddingMetadata.Resolve<DecoratedEntity>();

        metadata.Should().NotBeNull();
        metadata.HasAttribute.Should().BeTrue();
        metadata.LifecycleEnabled.Should().BeTrue("[Embedding] attribute enables lifecycle");
        metadata.Policy.Should().Be(EmbeddingPolicy.AllStrings);
    }

    [Fact]
    public void Convention_infers_string_properties()
    {
        var metadata = EmbeddingMetadata.Resolve<UndecoratedEntity>();

        metadata.Properties.Should().Contain("Title");
        metadata.Properties.Should().Contain("Description");
    }

    [Fact]
    public void Convention_excludes_non_string_properties()
    {
        var metadata = EmbeddingMetadata.Resolve<MixedPropertyEntity>();

        metadata.Properties.Should().Contain("Name");
        metadata.Properties.Should().NotContain("Age", "int properties excluded from AllStrings");
        metadata.Properties.Should().NotContain("IsActive", "bool properties excluded from AllStrings");
    }

    [Fact]
    public void Convention_includes_string_array_properties()
    {
        var metadata = EmbeddingMetadata.Resolve<StringArrayConventionEntity>();

        metadata.Properties.Should().Contain("Tags");
        metadata.Properties.Should().Contain("Title");
    }

    [Fact]
    public void Convention_respects_EmbeddingIgnore_without_attribute()
    {
        var metadata = EmbeddingMetadata.Resolve<IgnoreWithoutAttributeEntity>();

        metadata.Properties.Should().Contain("PublicData");
        metadata.Properties.Should().NotContain("SecretKey",
            "[EmbeddingIgnore] should be respected even without [Embedding]");
    }

    [Fact]
    public void Convention_on_entity_with_no_string_properties_returns_empty_properties()
    {
        var metadata = EmbeddingMetadata.Resolve<NoStringPropertiesEntity>();

        metadata.Should().NotBeNull("Resolve() should never return null");
        metadata.Properties.Should().BeEmpty();
        metadata.LifecycleEnabled.Should().BeFalse();
    }

    // ========================================================================
    // Convention BuildEmbeddingText
    // ========================================================================

    [Fact]
    public void Convention_BuildEmbeddingText_concatenates_string_properties()
    {
        var metadata = EmbeddingMetadata.Resolve<UndecoratedEntity>();

        var entity = new UndecoratedEntity
        {
            Title = "Convention Test",
            Description = "Works without attribute"
        };

        var text = metadata.BuildEmbeddingText(entity);

        text.Should().Contain("Convention Test");
        text.Should().Contain("Works without attribute");
    }

    [Fact]
    public void Convention_BuildEmbeddingText_on_empty_entity_returns_empty()
    {
        var metadata = EmbeddingMetadata.Resolve<UndecoratedEntity>();

        var entity = new UndecoratedEntity
        {
            Title = null!,
            Description = null!
        };

        var text = metadata.BuildEmbeddingText(entity);

        text.Should().BeEmpty();
    }

    // ========================================================================
    // LifecycleEnabled distinction
    // ========================================================================

    [Fact]
    public void LifecycleEnabled_true_only_with_attribute()
    {
        var decorated = EmbeddingMetadata.Resolve<DecoratedEntity>();
        var undecorated = EmbeddingMetadata.Resolve<UndecoratedEntity>();

        decorated.LifecycleEnabled.Should().BeTrue();
        undecorated.LifecycleEnabled.Should().BeFalse();
    }

    [Fact]
    public void HasAttribute_reflects_decoration_status()
    {
        var decorated = EmbeddingMetadata.Resolve<DecoratedEntity>();
        var undecorated = EmbeddingMetadata.Resolve<UndecoratedEntity>();

        decorated.HasAttribute.Should().BeTrue();
        undecorated.HasAttribute.Should().BeFalse();
    }

    // ========================================================================
    // Convention defaults are stable
    // ========================================================================

    [Fact]
    public void Convention_metadata_is_cached()
    {
        var first = EmbeddingMetadata.Resolve<UndecoratedEntity>();
        var second = EmbeddingMetadata.Resolve<UndecoratedEntity>();

        ReferenceEquals(first, second).Should().BeTrue("metadata should be cached");
    }

    [Fact]
    public void Convention_defaults_do_not_set_truncation()
    {
        var metadata = EmbeddingMetadata.Resolve<UndecoratedEntity>();

        metadata.MaxTokens.Should().Be(0, "convention defaults should not truncate");
        metadata.MaxDepth.Should().Be(0);
    }

    [Fact]
    public void Convention_defaults_version_is_zero()
    {
        var metadata = EmbeddingMetadata.Resolve<UndecoratedEntity>();

        metadata.Version.Should().Be(0);
    }

    // ========================================================================
    // Test Entities
    // ========================================================================

    public class UndecoratedEntity : Entity<UndecoratedEntity>
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
    }

    [Embedding(Policy = EmbeddingPolicy.AllStrings)]
    public class DecoratedEntity : Entity<DecoratedEntity>
    {
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
    }

    public class MixedPropertyEntity : Entity<MixedPropertyEntity>
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public bool IsActive { get; set; }
    }

    public class StringArrayConventionEntity : Entity<StringArrayConventionEntity>
    {
        public string Title { get; set; } = "";
        public string[] Tags { get; set; } = [];
    }

    public class IgnoreWithoutAttributeEntity : Entity<IgnoreWithoutAttributeEntity>
    {
        public string PublicData { get; set; } = "";

        [EmbeddingIgnore]
        public string SecretKey { get; set; } = "";
    }

    public class NoStringPropertiesEntity : Entity<NoStringPropertiesEntity>
    {
        public int ItemCount { get; set; }
        public double Score { get; set; }
        public bool Active { get; set; }
    }
}
