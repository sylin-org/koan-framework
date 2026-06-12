using AwesomeAssertions;
using Xunit;

namespace Koan.Data.AI.Tests;

/// <summary>
/// Tests for Phase 2: Query Extensions
///
/// IMPORTANT: SemanticSearch and FindSimilar methods require actual AI/vector infrastructure
/// to test their behavior. These cannot be meaningfully unit tested without:
/// - AI embedding service (Ai.Embed)
/// - Vector database (Vector.Search)
/// - Entity repository (Data.GetAsync)
///
/// This unit test file validates ONLY the error handling and validation that can be tested
/// without infrastructure. See INTEGRATION_TEST_PLAN.md for full test coverage requirements.
/// </summary>
public class Phase2_QueryExtensionsTests
{
    [Fact]
    public void EntityWithoutEmbeddingAttribute_ReturnsConventionInferredMetadata()
    {
        // Arrange & Act — Resolve() never returns null (AI-0021 convention defaults)
        var metadata = EmbeddingMetadata.Resolve<NonEmbeddableEntity>();

        // Assert — convention-inferred: AllStrings policy, lifecycle disabled
        metadata.Should().NotBeNull();
        metadata.Policy.Should().Be(Koan.Data.AI.Attributes.EmbeddingPolicy.AllStrings);
        metadata.LifecycleEnabled.Should().BeFalse("no [Embedding] attribute = no auto-embed-on-save");
        metadata.HasAttribute.Should().BeFalse();
        metadata.Properties.Should().Contain("Name");
    }
}

/// <summary>
/// Test entity WITHOUT [Embedding] attribute — used to verify convention-inferred metadata
/// </summary>
public class NonEmbeddableEntity : Koan.Data.Core.Model.Entity<NonEmbeddableEntity>
{
    public string Name { get; set; } = "";
}
