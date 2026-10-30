using System.Collections.Concurrent;
using System.Text;
using Koan.Communication.Adapters;
using Koan.Communication.Runtime;
using RabbitMQ.Client;

namespace Koan.Communication.Connector.RabbitMq.Tests;

public sealed class RabbitMqDeliverySpec(RabbitMqFixture rabbit) : IClassFixture<RabbitMqFixture>
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_failing_receiver_is_redelivered_a_bounded_number_of_times_then_dead_lettered()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DeliveryState();
        await using var host = await Start(state, "rabbit-delivery-" + Guid.NewGuid().ToString("N"), ct);
        using var scope = AppHost.PushScope(host.Services);
        var router = host.Services.GetRequiredService<CommunicationRouter>();
        var mesh = router.MeshId;
        var binding = router.Bindings
            .Single(candidate => candidate.GroupId.Contains(nameof(AlwaysFailingReceiver), StringComparison.Ordinal));

        await new FailingOrder { Name = "poison" }.Transport.Send(ct);
        var deadLetter = await NextDeadLetter(DeadLetterQueue(mesh, binding), ct);

        state.Attempts.Should().Be(Infrastructure.Constants.Broker.DeliveryLimit + 1);
        Encoding.UTF8.GetString(deadLetter.Body.Span).Should().Contain("poison");
        var headers = deadLetter.BasicProperties.Headers!;
        Encoding.UTF8.GetString((byte[])headers["x-first-death-reason"]!).Should().Be("delivery_limit");
        Encoding.UTF8.GetString((byte[])headers["x-first-death-queue"]!)
            .Should().Be(RabbitMqCommunicationAdapter.Topology.Queue(mesh, binding));
    }

    [Fact]
    public async Task A_hung_receiver_group_does_not_delay_another_groups_deliveries()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DeliveryState();
        await using var host = await Start(state, "rabbit-delivery-" + Guid.NewGuid().ToString("N"), ct);
        using var scope = AppHost.PushScope(host.Services);
        try
        {
            await new StallRabbitOrder { Name = "stalled" }.Transport.Send(ct);
            await state.Stalled.Task.WaitAsync(Bound, ct);

            await new PromptRabbitOrder { Name = "prompt" }.Transport.Send(ct);
            await state.Prompted.Task.WaitAsync(Bound, ct);

            state.Observations.Should().Equal("prompt");
        }
        finally
        {
            state.Release.TrySetResult(true);
        }
    }

    [Fact]
    public async Task Two_hundred_concurrent_sends_are_confirmed_promptly_through_the_confirm_window_and_every_one_arrives()
    {
        const int sends = 200;
        var ct = TestContext.Current.CancellationToken;
        var state = new DeliveryState();
        await using var host = await Start(state, "rabbit-delivery-" + Guid.NewGuid().ToString("N"), ct);
        using var scope = AppHost.PushScope(host.Services);

        await SendConcurrently(0, sends, ct);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var accepted = await SendConcurrently(sends, sends, ct);
        clock.Stop();
        await WaitUntil(() => state.Counted.Count == 2 * sends, ct);

        accepted.Should().OnlyContain(acceptance => acceptance.Accepted == 1);
        clock.Elapsed.Should().BeLessThan(
            TimeSpan.FromMilliseconds(500),
            "{0} sends share the publisher and wait for confirms, never behind a throttle or one another's round trips",
            sends);
        state.Counted.Order().Should().Equal(Enumerable.Range(0, 2 * sends));
    }

    private static Task<TransportAcceptance[]> SendConcurrently(int start, int count, CancellationToken ct)
        => Task.WhenAll(Enumerable.Range(start, count)
                .Select(index => new CountedOrder { Value = index }.Transport.Send(ct)))
            .WaitAsync(Bound, ct);

    [Fact]
    public async Task A_publication_over_the_brokers_message_limit_is_rejected_before_it_reaches_RabbitMQ()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DeliveryState();
        await using var host = await Start(
            state,
            "rabbit-delivery-" + Guid.NewGuid().ToString("N"),
            ct,
            maxMessageBytes: 4096);
        using var scope = AppHost.PushScope(host.Services);

        var oversized = () => new PromptRabbitOrder { Name = new string('x', 8192) }.Transport.Send(ct);
        var error = (await oversized.Should().ThrowAsync<TransportException>()).Which;
        await new PromptRabbitOrder { Name = "fits" }.Transport.Send(ct);
        await state.Prompted.Task.WaitAsync(Bound, ct);

        error.Failure.Should().Be(TransportException.FailureKind.PayloadTooLarge);
        error.Message.Should().Contain("4096-byte transport limit").And.Contain("'rabbitmq'");
        error.Acceptance.Accepted.Should().Be(0);
        error.Acceptance.Rejected.Should().Be(1);
        state.Observations.Should().Equal("fits");
    }

    private async Task<BasicGetResult> NextDeadLetter(string queue, CancellationToken ct)
    {
        var factory = new ConnectionFactory { Uri = new Uri(rabbit.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bound.CancelAfter(Bound);
        while (true)
        {
            var message = await channel.BasicGetAsync(queue, autoAck: true, bound.Token);
            if (message is not null) return message;
            await Task.Delay(TimeSpan.FromMilliseconds(100), bound.Token);
        }
    }

    private static string DeadLetterQueue(string mesh, CommunicationAdapterBinding binding)
        => RabbitMqCommunicationAdapter.Topology.DeadLetterQueue(mesh, binding);

    private static async Task WaitUntil(Func<bool> condition, CancellationToken ct)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bound.CancelAfter(Bound);
        while (!condition())
            await Task.Delay(TimeSpan.FromMilliseconds(50), bound.Token);
    }

    private Task<IntegrationHost> Start(
        DeliveryState state,
        string mesh,
        CancellationToken ct,
        int? maxMessageBytes = null)
    {
        var builder = KoanIntegrationHost.Configure()
            .WithEnvironment("Test")
            .WithSetting("Koan:Application:Code", mesh)
            .WithSetting("ConnectionStrings:RabbitMq", rabbit.ConnectionString);
        if (maxMessageBytes is { } limit)
            builder = builder.WithSetting("Koan:Communication:RabbitMq:MaxMessageBytes", limit.ToString());
        return builder
            .ConfigureServices(services =>
            {
                services.AddSingleton(state);
                services.AddKoan();
            })
            .StartAsync(ct);
    }

    public sealed class FailingOrder : Entity<FailingOrder>, IAmbientExempt
    {
        public string Name { get; set; } = "";
    }

    public sealed class AlwaysFailingReceiver(DeliveryState state) : IReceiveEntity<FailingOrder>
    {
        public Task Receive(FailingOrder entity, CancellationToken ct)
        {
            Interlocked.Increment(ref state.Attempts);
            throw new InvalidOperationException("expected transient receiver failure");
        }
    }

    public sealed class StallRabbitOrder : Entity<StallRabbitOrder>, IAmbientExempt
    {
        public string Name { get; set; } = "";
    }

    public sealed class StallingRabbitReceiver(DeliveryState state) : IReceiveEntity<StallRabbitOrder>
    {
        public async Task Receive(StallRabbitOrder entity, CancellationToken ct)
        {
            state.Stalled.TrySetResult(true);
            await state.Release.Task.WaitAsync(ct);
        }
    }

    public sealed class PromptRabbitOrder : Entity<PromptRabbitOrder>, IAmbientExempt
    {
        public string Name { get; set; } = "";
    }

    public sealed class PromptRabbitReceiver(DeliveryState state) : IReceiveEntity<PromptRabbitOrder>
    {
        public Task Receive(PromptRabbitOrder entity, CancellationToken ct)
        {
            state.Observations.Enqueue(entity.Name);
            state.Prompted.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    public sealed class CountedOrder : Entity<CountedOrder>, IAmbientExempt
    {
        public int Value { get; set; }
    }

    public sealed class CountedReceiver(DeliveryState state) : IReceiveEntity<CountedOrder>
    {
        public Task Receive(CountedOrder entity, CancellationToken ct)
        {
            state.Counted.Enqueue(entity.Value);
            return Task.CompletedTask;
        }
    }

    public sealed class DeliveryState
    {
        public int Attempts;
        public ConcurrentQueue<string> Observations { get; } = new();
        public ConcurrentQueue<int> Counted { get; } = new();
        public TaskCompletionSource<bool> Stalled { get; } = Signal();
        public TaskCompletionSource<bool> Release { get; } = Signal();
        public TaskCompletionSource<bool> Prompted { get; } = Signal();

        private static TaskCompletionSource<bool> Signal()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
