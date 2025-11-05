using FluentAssertions;
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
    public void EntityWithoutEmbeddingAttribute_ThrowsInvalidOperationException()
    {
        // Arrange & Act
        Action act = () => EmbeddingMetadata.Get<NonEmbeddableEntity>();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*has no [Embedding] attribute*")
            .WithMessage("*Add [Embedding] to enable*");
    }
}

/// <summary>
/// Test entity WITHOUT [Embedding] attribute to validate error handling
/// </summary>
public class NonEmbeddableEntity : Koan.Data.Core.Model.Entity<NonEmbeddableEntity>
{
    public string Name { get; set; } = "";
}
