using FluentAssertions;
using Koan.AI.Review;
using Xunit;

namespace Koan.AI.Review.Tests;

public class ReviewQueueBuilderTests
{
    [Fact]
    public void Builder_RequiresWhere()
    {
        var builder = new ReviewQueueBuilder<TestEntity>("test");
        builder.Display(t => new { t.Name });
        builder.Approve();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Where()*");
    }

    [Fact]
    public void Builder_RequiresDisplay()
    {
        var builder = new ReviewQueueBuilder<TestEntity>("test");
        builder.Where(t => t.ReviewStatus == ReviewStatus.Pending);
        builder.Approve();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Display()*");
    }

    [Fact]
    public void Builder_WithAllActions_BuildsCorrectly()
    {
        var builder = new ReviewQueueBuilder<TestEntity>("full-queue");
        builder.Where(t => t.ReviewStatus == ReviewStatus.Pending);
        builder.Display(t => new { t.Id, t.Name, t.Content });
        builder.Approve();
        builder.Reject(requireReason: true);
        builder.Edit(t => t.Content!);
        builder.Label(t => t.Rating, 1, 2, 3, 4, 5);
        builder.Flag("escalate", "bias");

        var queue = builder.Build();

        queue.Name.Should().Be("full-queue");
        queue.EntityType.Should().Be(typeof(TestEntity));
        queue.Actions.Should().HaveCount(5);
        queue.Actions[0].Should().BeOfType<ApproveAction<TestEntity>>();
        queue.Actions[1].Should().BeOfType<RejectAction<TestEntity>>();
        queue.Actions[2].Should().BeOfType<EditAction<TestEntity>>();
        queue.Actions[3].Should().BeOfType<LabelAction<TestEntity>>();
        queue.Actions[4].Should().BeOfType<FlagAction<TestEntity>>();
    }
}
