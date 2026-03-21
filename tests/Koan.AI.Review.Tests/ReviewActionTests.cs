using FluentAssertions;
using Koan.AI.Review;
using Xunit;

namespace Koan.AI.Review.Tests;

public class ReviewActionTests
{
    [Fact]
    public void Approve_CreatesApproveAction()
    {
        var action = Review.Approve<TestEntity>();

        action.Should().BeOfType<ApproveAction<TestEntity>>();
    }

    [Fact]
    public void Reject_WithReason_SetsFlag()
    {
        var action = Review.Reject<TestEntity>(requireReason: true);

        action.Should().BeOfType<RejectAction<TestEntity>>();
        action.RequireReason.Should().BeTrue();
    }

    [Fact]
    public void Edit_ExtractsFieldName()
    {
        var action = Review.Edit<TestEntity>(t => t.Name);

        action.Should().BeOfType<EditAction<TestEntity>>();
        action.FieldName.Should().Be("Name");
    }

    [Fact]
    public void Label_ExtractsFieldAndOptions()
    {
        var action = Review.Label<TestEntity>(t => t.Rating, 1, 2, 3);

        action.Should().BeOfType<LabelAction<TestEntity>>();
        action.FieldName.Should().Be("Rating");
        action.Options.Should().BeEquivalentTo(new object[] { 1, 2, 3 });
    }

    [Fact]
    public void Flag_SetsFlagTypes()
    {
        var action = Review.Flag<TestEntity>("escalate", "bias");

        action.Should().BeOfType<FlagAction<TestEntity>>();
        action.FlagTypes.Should().BeEquivalentTo(new[] { "escalate", "bias" });
    }
}
