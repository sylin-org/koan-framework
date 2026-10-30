using Koan.Communication.Adapters;

namespace Koan.Communication.Connector.RabbitMq.Tests;

public sealed class RabbitMqTopologySpec
{
    [Fact]
    public void Logical_channel_participates_in_routes_and_queue_identity()
    {
        var defaultBinding = Binding("default");
        var priorityBinding = Binding("priority");

        var defaultRoute = RabbitMqCommunicationAdapter.Topology.Route(
            defaultBinding.Lane,
            defaultBinding.Channel,
            defaultBinding.ContractId);
        var priorityRoute = RabbitMqCommunicationAdapter.Topology.Route(
            priorityBinding.Lane,
            priorityBinding.Channel,
            priorityBinding.ContractId);

        defaultRoute.Should().NotBe(priorityRoute);
        RabbitMqCommunicationAdapter.Topology.Queue("app", defaultBinding)
            .Should().NotBe(RabbitMqCommunicationAdapter.Topology.Queue("app", priorityBinding));
    }

    [Fact]
    public void Every_durable_group_dead_letters_to_its_own_queue_after_a_bounded_number_of_failures()
    {
        var first = Binding("default");
        var second = first with { GroupId = "other-receiver@1" };
        var queue = RabbitMqCommunicationAdapter.Topology.Queue("app", first);
        var deadLetterExchange = RabbitMqCommunicationAdapter.Topology.DeadLetterExchange("app");

        var arguments = RabbitMqCommunicationAdapter.Topology.GroupQueueArguments(deadLetterExchange, queue);

        RabbitMqCommunicationAdapter.Topology.DeadLetterQueue("app", first)
            .Should().Be(queue + ".dead-letter")
            .And.NotBe(RabbitMqCommunicationAdapter.Topology.DeadLetterQueue("app", second));
        deadLetterExchange.Should().Be(RabbitMqCommunicationAdapter.Topology.Exchange("app") + ".dead-letter");
        arguments.Should().Contain("x-queue-type", "quorum")
            .And.Contain("x-delivery-limit", 5)
            .And.Contain("x-dead-letter-exchange", deadLetterExchange)
            .And.Contain("x-dead-letter-routing-key", queue)
            .And.Contain("x-dead-letter-strategy", "at-least-once")
            .And.Contain("x-overflow", "reject-publish");
    }

    [Fact]
    public async Task The_publication_after_the_outstanding_confirm_limit_waits_until_a_confirm_settles()
    {
        var ct = TestContext.Current.CancellationToken;
        var limit = Infrastructure.Constants.Broker.OutstandingConfirms;
        await using var window = new Publishing.PublisherConfirmWindow(limit);
        var confirms = Enumerable.Range(0, limit + 1)
            .Select(static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var entered = new System.Collections.Concurrent.ConcurrentQueue<int>();
        var outstanding = 0;
        var mostOutstanding = 0;

        // Each publication is outstanding from the moment it is written until its confirm settles.
        async ValueTask Publish(int index)
        {
            var now = Interlocked.Increment(ref outstanding);
            InterlockedMax(ref mostOutstanding, now);
            entered.Enqueue(index);
            await confirms[index].Task;
            Interlocked.Decrement(ref outstanding);
        }

        var publications = Enumerable.Range(0, limit + 1)
            .Select(index => window.Publish(() => Publish(index), ct))
            .ToArray();
        await WaitUntil(() => entered.Count >= limit, ct);
        await Task.Delay(TimeSpan.FromMilliseconds(200), ct);

        entered.Should().HaveCount(limit, "the publication after the limit waits for a confirm");
        entered.Should().NotContain(limit);
        publications[limit].IsCompleted.Should().BeFalse();

        confirms[0].SetResult();
        await WaitUntil(() => entered.Contains(limit), ct);
        foreach (var confirm in confirms) confirm.TrySetResult();
        await Task.WhenAll(publications).WaitAsync(TimeSpan.FromSeconds(10), ct);

        mostOutstanding.Should().Be(limit);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var seen = Interlocked.CompareExchange(ref target, value, current);
            if (seen == current) return;
            current = seen;
        }
    }

    private static async Task WaitUntil(Func<bool> condition, CancellationToken ct)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bound.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
            await Task.Delay(TimeSpan.FromMilliseconds(10), bound.Token);
    }

    private static CommunicationAdapterBinding Binding(string channel)
        => new(
            $"transport|{channel}|order|receiver",
            CommunicationLane.Transport,
            channel,
            "order@1",
            "receiver@1");
}
