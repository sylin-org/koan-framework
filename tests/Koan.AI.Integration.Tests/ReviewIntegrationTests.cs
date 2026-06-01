using AwesomeAssertions;
using Koan.AI.Integration.Tests.Fixtures;
using Koan.AI.Review;
using Xunit;

namespace Koan.AI.Integration.Tests;

public sealed class ReviewIntegrationTests
{
    private readonly IReviewActionHandler _handler = new ReviewActionHandler();

    [Fact]
    public async Task Approve_SetsStatusAndReviewer()
    {
        var entity = new TestReviewableEntity { Content = "AI-generated text" };

        await _handler.ApproveAsync(entity, "reviewer@test.com");

        entity.ReviewStatus.Should().Be(ReviewStatus.Approved);
        entity.ReviewedBy.Should().Be("reviewer@test.com");
        entity.ReviewedAt.Should().NotBeNull();
        entity.ReviewedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Reject_SetsStatusAndReason()
    {
        var entity = new TestReviewableEntity { Content = "Bad content" };

        await _handler.RejectAsync(entity, "reviewer", "incorrect");

        entity.ReviewStatus.Should().Be(ReviewStatus.Rejected);
        entity.ReviewedBy.Should().Be("reviewer");
        entity.RejectionReason.Should().Be("incorrect");
        entity.ReviewedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Edit_UpdatesFieldAndPreservesOriginal()
    {
        var entity = new TestReviewableEntity { Content = "original" };

        await _handler.EditAsync(entity, "Content", "corrected", "reviewer");

        entity.Content.Should().Be("corrected");
        entity.OriginalContent.Should().Be("original");
        entity.ReviewStatus.Should().Be(ReviewStatus.Edited);
        entity.ReviewedBy.Should().Be("reviewer");
    }

    [Fact]
    public async Task Label_SetsFieldWithoutChangingStatus()
    {
        var entity = new TestReviewableEntity
        {
            Content = "Good content",
            ReviewStatus = ReviewStatus.Approved
        };

        await _handler.LabelAsync(entity, "Rating", 4, "reviewer");

        entity.Rating.Should().Be(4);
        entity.ReviewStatus.Should().Be(ReviewStatus.Approved, "Label should not change existing status");
        entity.ReviewedBy.Should().Be("reviewer");
    }

    [Fact]
    public async Task Flag_SetsStatusAndAddsFlag()
    {
        var entity = new TestReviewableEntity { Content = "Suspicious content" };

        await _handler.FlagAsync(entity, "bias", "reviewer");

        entity.ReviewStatus.Should().Be(ReviewStatus.Flagged);
        entity.Flags.Should().Contain("bias");
        entity.ReviewedBy.Should().Be("reviewer");
    }
}
