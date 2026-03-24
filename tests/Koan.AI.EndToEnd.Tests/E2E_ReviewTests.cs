using FluentAssertions;
using Koan.AI.Review;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using ReviewFacade = Koan.AI.Review.Review;

namespace Koan.AI.EndToEnd.Tests;

/// <summary>
/// End-to-end tests verifying Review queue and action handler infrastructure
/// through the full DI-bootstrapped framework.
/// </summary>
[Trait("Category", "EndToEnd")]
[Collection("EndToEnd")]
public sealed class E2E_ReviewTests : IDisposable
{
    private readonly KoanTestFixture _fixture;

    public E2E_ReviewTests()
    {
        _fixture = new KoanTestFixture();
    }

    [Fact]
    public void ReviewQueueRegistry_IsResolvableFromDI()
    {
        var registry = _fixture.Services.GetService<ReviewQueueRegistry>();

        registry.Should().NotBeNull("ReviewQueueRegistry should be registered by Koan.AI.Review auto-registrar");
    }

    [Fact]
    public void ReviewQueueRegistry_RegisterAndGet_RoundTrips()
    {
        var registry = _fixture.Services.GetRequiredService<ReviewQueueRegistry>();

        var queue = ReviewFacade.Create<TestReviewEntity>(
            "test-review-queue",
            where: e => e.ReviewStatus == ReviewStatus.Pending,
            display: e => new { e.Message },
            actions: [ReviewFacade.Approve<TestReviewEntity>(), ReviewFacade.Reject<TestReviewEntity>(true)]);

        registry.Register(queue);

        var retrieved = registry.Get<TestReviewEntity>("test-review-queue");
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("test-review-queue");
        retrieved.Actions.Should().HaveCount(2);
    }

    [Fact]
    public void ReviewQueueRegistry_Names_ListsRegisteredQueues()
    {
        var registry = _fixture.Services.GetRequiredService<ReviewQueueRegistry>();

        var queue = ReviewFacade.Create<TestReviewEntity>(
            "named-queue",
            where: e => e.ReviewStatus == ReviewStatus.Pending,
            display: e => new { e.Message },
            actions: [ReviewFacade.Approve<TestReviewEntity>()]);

        registry.Register(queue);

        registry.Names.Should().Contain("named-queue");
    }

    [Fact]
    public void ReviewActionHandler_IsResolvableFromDI()
    {
        var handler = _fixture.Services.GetService<IReviewActionHandler>();

        handler.Should().NotBeNull("IReviewActionHandler should be registered by Koan.AI.Review auto-registrar");
    }

    [Fact]
    public async Task ReviewActionHandler_Approve_ChangesEntityState()
    {
        var handler = _fixture.Services.GetRequiredService<IReviewActionHandler>();
        var entity = new TestReviewEntity { Message = "Test reply" };

        entity.ReviewStatus.Should().Be(ReviewStatus.Pending);

        await handler.ApproveAsync(entity, "test-reviewer");

        entity.ReviewStatus.Should().Be(ReviewStatus.Approved);
        entity.ReviewedBy.Should().Be("test-reviewer");
        entity.ReviewedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ReviewActionHandler_Reject_SetsReasonAndStatus()
    {
        var handler = _fixture.Services.GetRequiredService<IReviewActionHandler>();
        var entity = new TestReviewEntity { Message = "Bad reply" };

        await handler.RejectAsync(entity, "reviewer", "Low quality");

        entity.ReviewStatus.Should().Be(ReviewStatus.Rejected);
        entity.ReviewedBy.Should().Be("reviewer");
        entity.RejectionReason.Should().Be("Low quality");
    }

    [Fact]
    public async Task ReviewActionHandler_Flag_SetsStatusAndAddsFlag()
    {
        var handler = _fixture.Services.GetRequiredService<IReviewActionHandler>();
        var entity = new TestReviewEntity { Message = "Suspicious reply" };

        await handler.FlagAsync(entity, "hallucination", "reviewer");

        entity.ReviewStatus.Should().Be(ReviewStatus.Flagged);
        entity.Flags.Should().Contain("hallucination");
    }

    public void Dispose() => _fixture.Dispose();
}

/// <summary>
/// Test entity implementing IReviewable for review action handler tests.
/// </summary>
internal sealed class TestReviewEntity : IReviewable
{
    public string Message { get; set; } = "";
    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Pending;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
    public List<string> Flags { get; set; } = [];
}
