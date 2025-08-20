using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Data.Core;
using Sora.Messaging;
using Sora.Testing;
using System.Text.Json;
using Xunit;

public class BasicRabbitMqTests : IAsyncLifetime
{
    private TestcontainersContainer? _rabbit;
    private int _hostPort = 5674; // avoid default 5672 to reduce conflicts
    private bool _available;
    private string? _dockerEndpoint;

    public async Task InitializeAsync()
    {
        // Standardized Docker probing and Ryuk disable
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
        var probe = await DockerEnvironment.ProbeAsync();
        if (!probe.Available)
        {
            _available = false;
            return;
        }
        _dockerEndpoint = probe.Endpoint;
        try
        {
            _rabbit = new TestcontainersBuilder<TestcontainersContainer>()
                .WithDockerEndpoint(_dockerEndpoint)
                .WithImage("rabbitmq:3.13-management")
                .WithPortBinding(_hostPort, 5672)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5672))
                .Build();
            await _rabbit.StartAsync();
            _available = true;
        }
        catch
        {
            _available = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_rabbit != null)
        {
            try { await _rabbit.StopAsync(); } catch { }
            try { await _rabbit.DisposeAsync(); } catch { }
        }
    }

    [Fact]
    public async Task Send_and_handle_message_via_DI_handler()
    {
        if (!_available) return; // Skip when Docker/Testcontainers not available.
        var conn = $"amqp://guest:guest@localhost:{_hostPort}";

        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sora:Messaging:DefaultBus"] = "rabbit",
                ["Sora:Messaging:Buses:rabbit:Provider"] = "RabbitMq",
                ["Sora:Messaging:Buses:rabbit:ConnectionString"] = conn,
                ["Sora:Messaging:Buses:rabbit:RabbitMq:Exchange"] = "sora-test",
                ["Sora:Messaging:Buses:rabbit:ProvisionOnStart"] = "true",
                ["Sora:Messaging:Buses:rabbit:Subscriptions:0:Name"] = "workers",
                ["Sora:Messaging:Buses:rabbit:Subscriptions:0:RoutingKeys:0"] = "hello"
            })
            .Build();
        services.AddSingleton<IConfiguration>(cfg);

        services.AddSora();
        services.AddSingleton<IMessageHandler<Hello>, HelloHandler>();

        var sp = services.BuildServiceProvider();
        sp.UseSora();

        await new Hello { Name = "Sora" }.Send();

        // Give consumer a moment
        await Task.Delay(500);

        var handler = sp.GetRequiredService<IMessageHandler<Hello>>() as HelloHandler;
        handler!.Last?.Name.Should().Be("Sora");
    }

    [Fact]
    public async Task Partition_key_affects_routing_suffix()
    {
        if (!_available) return; // Skip when Docker/Testcontainers not available.
        var conn = $"amqp://guest:guest@localhost:{_hostPort}";

        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sora:Messaging:DefaultBus"] = "rabbit",
                ["Sora:Messaging:Buses:rabbit:Provider"] = "RabbitMq",
                ["Sora:Messaging:Buses:rabbit:ConnectionString"] = conn,
                ["Sora:Messaging:Buses:rabbit:RabbitMq:Exchange"] = "sora-test",
                ["Sora:Messaging:Buses:rabbit:ProvisionOnStart"] = "true",
                ["Sora:Messaging:Buses:rabbit:Subscriptions:0:Name"] = "workers",
                // Bind to the alias prefix so .pN suffix still matches
                ["Sora:Messaging:Buses:rabbit:Subscriptions:0:RoutingKeys:0"] = "My.Partitioned.Event.#"
            })
            .Build();
        services.AddSingleton<IConfiguration>(cfg);

        services.AddSora();
        services.AddSingleton<IMessageHandler<MyPartitionedEvent>, PartitionedHandler>();

        var sp = services.BuildServiceProvider();
        sp.UseSora();

        await new MyPartitionedEvent { UserId = "user-42", Value = 7 }.Send();
        await Task.Delay(500);

        var handler = sp.GetRequiredService<IMessageHandler<MyPartitionedEvent>>() as PartitionedHandler;
        handler!.Last?.UserId.Should().Be("user-42");
    }

    [Message(Alias = "My.Partitioned.Event")]
    public sealed class MyPartitionedEvent
    {
        [PartitionKey] public string UserId { get; init; } = string.Empty;
        public int Value { get; init; }
    }

    public sealed class PartitionedHandler : IMessageHandler<MyPartitionedEvent>
    {
        public MyPartitionedEvent? Last { get; private set; }
        public Task HandleAsync(MessageEnvelope envelope, MyPartitionedEvent message, CancellationToken ct)
        {
            Last = message;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Publisher_confirms_enabled_does_not_block()
    {
        if (!_available) return;
        var conn = $"amqp://guest:guest@localhost:{_hostPort}";

        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sora:Messaging:DefaultBus"] = "rabbit",
                ["Sora:Messaging:Buses:rabbit:Provider"] = "RabbitMq",
                ["Sora:Messaging:Buses:rabbit:ConnectionString"] = conn,
                ["Sora:Messaging:Buses:rabbit:RabbitMq:Exchange"] = "sora-test",
                ["Sora:Messaging:Buses:rabbit:ProvisionOnStart"] = "true",
                ["Sora:Messaging:Buses:rabbit:RabbitMq:PublisherConfirms"] = "true",
                ["Sora:Messaging:Buses:rabbit:Subscriptions:0:Name"] = "workers",
                ["Sora:Messaging:Buses:rabbit:Subscriptions:0:RoutingKeys:0"] = "pub.confirm"
            })
            .Build();
        services.AddSingleton<IConfiguration>(cfg);

        services.AddSora();
        services.AddSingleton<IMessageHandler<Confirmable>, ConfirmableHandler>();

        var sp = services.BuildServiceProvider();
        sp.UseSora();

        await new Confirmable { Name = "ok" }.Send();
        await Task.Delay(300);

        var handler = sp.GetRequiredService<IMessageHandler<Confirmable>>() as ConfirmableHandler;
        handler!.Last?.Name.Should().Be("ok");
    }

    [Message(Alias = "pub.confirm")]
    public sealed class Confirmable { public string Name { get; init; } = string.Empty; }

    public sealed class ConfirmableHandler : IMessageHandler<Confirmable>
    {
        public Confirmable? Last { get; private set; }
        public Task HandleAsync(MessageEnvelope envelope, Confirmable message, CancellationToken ct)
        {
            Last = message;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Batch_alias_dispatches_to_OnBatch_handler()
    {
        if (!_available) return;
        var conn = $"amqp://guest:guest@localhost:{_hostPort}";

        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sora:Messaging:DefaultBus"] = "rabbit",
                ["Sora:Messaging:Buses:rabbit:Provider"] = "RabbitMq",
                ["Sora:Messaging:Buses:rabbit:ConnectionString"] = conn,
                ["Sora:Messaging:Buses:rabbit:RabbitMq:Exchange"] = "sora-test",
                ["Sora:Messaging:Buses:rabbit:ProvisionOnStart"] = "true",
                ["Sora:Messaging:Buses:rabbit:Subscriptions:0:Name"] = "workers",
                ["Sora:Messaging:Buses:rabbit:Subscriptions:0:RoutingKeys:0"] = "batch:BatchTests.User"
            })
            .Build();
        services.AddSingleton<IConfiguration>(cfg);

        var batchHandler = new UserBatchHandler();
        services.AddSora();
        services.AddSingleton<IMessageHandler<Batch<User>>>(batchHandler);

        var sp = services.BuildServiceProvider();
        sp.UseSora();

        var users = new[] { new User { Id = "u1" }, new User { Id = "u2" } };
        await users.SendAsBatch();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await batchHandler.Done.Task.WaitAsync(cts.Token);
        batchHandler.Count.Should().Be(2);
    }

    [Message(Alias = "BatchTests.User")]
    public sealed class User { public string Id { get; init; } = string.Empty; }

    public sealed class UserBatchHandler : IMessageHandler<Batch<User>>
    {
        public int Count { get; private set; }
        public TaskCompletionSource<bool> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task HandleAsync(MessageEnvelope envelope, Batch<User> message, CancellationToken ct)
        {
            Count = message.Items.Count;
            Done.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Auto_subscribe_default_group_when_no_subscriptions_configured()
    {
        if (!_available) return;
        var conn = $"amqp://guest:guest@localhost:{_hostPort}";

        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sora:Messaging:DefaultBus"] = "rabbit",
                ["Sora:Messaging:DefaultGroup"] = "workers",
                ["Sora:Messaging:Buses:rabbit:Provider"] = "RabbitMq",
                ["Sora:Messaging:Buses:rabbit:ConnectionString"] = conn,
                ["Sora:Messaging:Buses:rabbit:RabbitMq:Exchange"] = "sora-test",
                ["Sora:Messaging:Buses:rabbit:ProvisionOnStart"] = "true"
            })
            .Build();
        services.AddSingleton<IConfiguration>(cfg);

        var handler = new HelloHandler();
        services.AddSora();
        services.AddSingleton<IMessageHandler<Hello>>(handler);

        var sp = services.BuildServiceProvider();
        sp.UseSora();

        await new Hello { Name = "auto" }.Send();
        await Task.Delay(300);
        handler.Last?.Name.Should().Be("auto");
    }

    [Fact]
    public async Task Redelivery_sets_envelope_attempt_from_1_to_2()
    {
        if (!_available) return;
        var conn = $"amqp://guest:guest@localhost:{_hostPort}";

        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sora:Messaging:DefaultBus"] = "rabbit",
                ["Sora:Messaging:Buses:rabbit:Provider"] = "RabbitMq",
                ["Sora:Messaging:Buses:rabbit:ConnectionString"] = conn,
                ["Sora:Messaging:Buses:rabbit:RabbitMq:Exchange"] = "sora-test",
                ["Sora:Messaging:Buses:rabbit:ProvisionOnStart"] = "true",
                // DLQ enabled is fine; we succeed on second delivery
                ["Sora:Messaging:Buses:rabbit:Dlq:Enabled"] = "true",
                ["Sora:Messaging:Buses:rabbit:Subscriptions:0:Name"] = "workers",
                ["Sora:Messaging:Buses:rabbit:Subscriptions:0:RoutingKeys:0"] = "retry.case"
            })
            .Build();
        services.AddSingleton<IConfiguration>(cfg);

        var handler = new RetryingHandler();
        services.AddSora();
        services.AddSingleton<IMessageHandler<RetryMessage>>(handler);

        var sp = services.BuildServiceProvider();
        sp.UseSora();

        await new RetryMessage { Key = "k-1" }.Send();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await handler.Done.Task.WaitAsync(cts.Token);

        handler.Attempts.Should().ContainInOrder(1, 2);
    }

    [Message(Alias = "retry.case")]
    public sealed class RetryMessage { public string Key { get; init; } = string.Empty; }

    public sealed class RetryingHandler : IMessageHandler<RetryMessage>
    {
        private int _count = 0;
        public List<int> Attempts { get; } = new();
        public TaskCompletionSource<bool> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task HandleAsync(MessageEnvelope envelope, RetryMessage message, CancellationToken ct)
        {
            _count++;
            Attempts.Add(envelope.Attempt);
            if (_count == 1)
            {
                throw new InvalidOperationException("fail first");
            }
            Done.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    public sealed class Hello { public string Name { get; init; } = string.Empty; }

    public sealed class HelloHandler : IMessageHandler<Hello>
    {
        public Hello? Last { get; private set; }
        public Task HandleAsync(MessageEnvelope envelope, Hello message, CancellationToken ct)
        {
            Last = message;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Inbox_deduplicates_by_IdempotencyKey()
    {
        if (!_available) return; // Skip when Docker/Testcontainers not available.
        var conn = $"amqp://guest:guest@localhost:{_hostPort}";

        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sora:Messaging:DefaultBus"] = "rabbit",
                ["Sora:Messaging:Buses:rabbit:Provider"] = "RabbitMq",
                ["Sora:Messaging:Buses:rabbit:ConnectionString"] = conn,
                ["Sora:Messaging:Buses:rabbit:RabbitMq:Exchange"] = "sora-test",
                ["Sora:Messaging:Buses:rabbit:ProvisionOnStart"] = "true",
                ["Sora:Messaging:Buses:rabbit:Subscriptions:0:Name"] = "workers",
                ["Sora:Messaging:Buses:rabbit:Subscriptions:0:RoutingKeys:0"] = "inbox.sample"
            })
            .Build();
        services.AddSingleton<IConfiguration>(cfg);

        var handler = new InboxedHandler();
        services.AddSora();
        services.AddSingleton<IMessageHandler<InboxSample>>(handler);

        var sp = services.BuildServiceProvider();
        sp.UseSora();

        var msg1 = new InboxSample { OrderId = "o-1", Key = "same" };
        var msg2 = new InboxSample { OrderId = "o-1", Key = "same" };
        await msg1.Send();
        await msg2.Send();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await Task.Delay(400, cts.Token);

        handler.Count.Should().Be(1);
    }

    [Message(Alias = "inbox.sample")]
    public sealed class InboxSample
    {
        public string OrderId { get; init; } = string.Empty;
        [IdempotencyKey] public string Key { get; init; } = string.Empty;
    }

    public sealed class InboxedHandler : IMessageHandler<InboxSample>
    {
        public int Count { get; private set; }
        public Task HandleAsync(MessageEnvelope envelope, InboxSample message, CancellationToken ct)
        {
            Count++;
            return Task.CompletedTask;
        }
    }
}
