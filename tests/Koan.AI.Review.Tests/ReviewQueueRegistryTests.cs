using FluentAssertions;
using Koan.AI.Review;
using Xunit;

namespace Koan.AI.Review.Tests;

public class ReviewQueueRegistryTests
{
    private static ReviewQueue<TestEntity> CreateTestQueue(string name) =>
        Review.Create<TestEntity>(
            name,
            where: t => t.ReviewStatus == ReviewStatus.Pending,
            display: t => new { t.Id, t.Name },
            actions: [Review.Approve<TestEntity>()]);

    [Fact]
    public void Register_StoresQueue()
    {
        var registry = new ReviewQueueRegistry();
        var queue = CreateTestQueue("test-queue");

        registry.Register(queue);

        registry.Names.Should().Contain("test-queue");
    }

    [Fact]
    public void Register_Duplicate_Throws()
    {
        var registry = new ReviewQueueRegistry();
        registry.Register(CreateTestQueue("duplicate-queue"));

        var act = () => registry.Register(CreateTestQueue("duplicate-queue"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Get_ReturnsCorrectType()
    {
        var registry = new ReviewQueueRegistry();
        var queue = CreateTestQueue("typed-queue");
        registry.Register(queue);

        var retrieved = registry.Get<TestEntity>("typed-queue");

        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("typed-queue");
        retrieved.EntityType.Should().Be(typeof(TestEntity));
    }

    [Fact]
    public void Names_ListsRegistered()
    {
        var registry = new ReviewQueueRegistry();
        registry.Register(CreateTestQueue("queue-a"));
        registry.Register(CreateTestQueue("queue-b"));

        registry.Names.Should().HaveCount(2);
        registry.Names.Should().Contain("queue-a");
        registry.Names.Should().Contain("queue-b");
    }
}
