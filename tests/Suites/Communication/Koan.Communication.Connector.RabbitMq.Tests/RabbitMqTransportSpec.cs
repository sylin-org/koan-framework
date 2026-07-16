using System.Collections.Concurrent;
using Koan.Communication.Connector.RabbitMq.Orchestration;
using Koan.Communication.Signals;
using Koan.Core.Composition;
using Koan.Core.Diagnostics;
using Koan.Core.Observability.Health;
using Koan.Core.Orchestration;
using Koan.Tenancy;
using Microsoft.Extensions.Configuration;

namespace Koan.Communication.Connector.RabbitMq.Tests;

public sealed class RabbitMqTransportSpec(RabbitMqFixture rabbit) : IClassFixture<RabbitMqFixture>
{
    [Fact]
    public async Task Direct_connector_intent_elects_confirmed_transport_and_fans_out_group_copies()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new RabbitState(expected: 2);
        await using var host = await Start(state, rabbit.ConnectionString, ct);
        using var scope = AppHost.PushScope(host.Services);

        var acceptance = await new FanoutOrder { Name = "original" }.Transport.Send(ct);
        await state.Completed.Task.WaitAsync(ct);

        acceptance.Adapter.Should().Be("rabbitmq");
        acceptance.Assurance.Should().Be("durably-acknowledged");
        acceptance.ReceiverGroups.Should().BeNull();
        acceptance.SettlementObservable.Should().BeFalse();
        state.Observations.Should().BeEquivalentTo(["A:original", "B:original"]);

        var facts = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts;
        facts.Should().Contain(fact =>
            fact.Code == "koan.communication.transport.selected"
            && fact.Subject == "communication:transport:default"
            && fact.ReasonCode == "direct-reference-intent"
            && fact.Summary.Contains("'rabbitmq'", StringComparison.Ordinal));
        facts.Should().Contain(fact =>
            fact.Code == "koan.communication.events.selected"
            && fact.Subject == "communication:events:default"
            && fact.ReasonCode == "built-in-floor"
            && fact.Summary.Contains("'in-process'", StringComparison.Ordinal));
        var bounds = facts.Single(fact => fact.Code == "koan.communication.transport.bounds");
        bounds.Summary.Should().Contain("durably-acknowledged");
        bounds.Summary.Should().Contain("not observable");

        var health = host.Services.GetServices<IHealthContributor>()
            .Single(contributor => contributor.Name == "communication.rabbitmq");
        health.IsCritical.Should().BeTrue();
        var report = await health.Check(ct);
        report.State.Should().Be(HealthState.Healthy);
        report.Data!["active"].Should().Be(true);
        report.Data["ready"].Should().Be(true);

        var wait = () => acceptance.WaitForSettlement(ct);
        (await wait.Should().ThrowAsync<TransportException>()).Which.Failure
            .Should().Be(TransportException.FailureKind.SettlementUnavailable);
    }

    [Fact]
    public async Task Named_channel_can_elect_RabbitMQ_while_default_transport_stays_local()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new RabbitState(expected: 2);
        await using var host = await Start(
            state,
            rabbit.ConnectionString,
            ct,
            services => services.Configure<CommunicationOptions>(options =>
            {
                options.TransportProvider = "in-process";
                options.FrameworkSignalsProvider = "in-process";
                options.FrameworkBroadcastsProvider = "in-process";
                options.Channels["priority"] = new CommunicationChannelOptions
                {
                    TransportProvider = "rabbitmq"
                };
            }));
        using var scope = AppHost.PushScope(host.Services);

        var acceptance = await new FanoutOrder { Name = "named" }
            .Transport.Send(ct, channel: "priority");
        await state.Completed.Task.WaitAsync(ct);

        acceptance.Channel.Should().Be("priority");
        acceptance.Adapter.Should().Be("rabbitmq");
        acceptance.Assurance.Should().Be("durably-acknowledged");
        state.Observations.Should().BeEquivalentTo(["A:named", "B:named"]);

        var facts = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts;
        facts.Should().Contain(fact =>
            fact.Code == "koan.communication.transport.selected"
            && fact.Subject == "communication:transport:default"
            && fact.Summary.Contains("'in-process'", StringComparison.Ordinal));
        facts.Should().Contain(fact =>
            fact.Code == "koan.communication.transport.selected"
            && fact.Subject == "communication:transport:priority"
            && fact.Summary.Contains("'rabbitmq'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Authenticated_mesh_restores_tenant_context_only_inside_the_receiver()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new RabbitState(expected: 1);
        await using var host = await Start(state, rabbit.ConnectionString, ct);
        using var scope = AppHost.PushScope(host.Services);

        using (Tenant.Use("tenant-rabbit"))
        {
            _ = await new TenantOrder().Transport.Send(ct);
        }

        await state.Completed.Task.WaitAsync(ct);
        state.Observations.Should().Equal("tenant-rabbit");
        Tenant.Current.Should().BeNull();
    }

    [Fact]
    public async Task Direct_connector_carries_internal_framework_signals_without_an_application_bus()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new RabbitState(expected: 1);
        await using var host = await Start(
            state,
            rabbit.ConnectionString,
            ct,
            services =>
            {
                services.AddSingleton<ProbeSignalHandler>();
                services.AddFrameworkSignal<ProbeSignal, ProbeSignalHandler>();
            });

        var publisher = host.Services.GetRequiredService<IFrameworkSignalPublisher>();
        publisher.TryPublish(new ProbeSignal()).Should().BeTrue();
        await state.Completed.Task.WaitAsync(ct);

        publisher.ProviderId.Should().Be("rabbitmq");
        state.Observations.Should().Equal("framework-signal");
        host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts.Should().Contain(fact =>
            fact.Code == "koan.communication.framework-signals.selected"
            && fact.Subject == "communication:framework-signals:default"
            && fact.ReasonCode == "direct-reference-intent"
            && fact.Summary.Contains("'rabbitmq'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Framework_broadcast_reaches_every_active_node_in_the_mesh()
    {
        var ct = TestContext.Current.CancellationToken;
        var mesh = "rabbit-broadcast-" + Guid.NewGuid().ToString("N");
        var stateA = new RabbitState(expected: 1);
        var stateB = new RabbitState(expected: 1);
        await using var hostA = await Start(stateA, rabbit.ConnectionString, ct, mesh, services =>
        {
            services.AddSingleton<ProbeBroadcastHandler>();
            services.AddFrameworkBroadcast<ProbeBroadcast, ProbeBroadcastHandler>();
        });
        await using var hostB = await Start(stateB, rabbit.ConnectionString, ct, mesh, services =>
        {
            services.AddSingleton<ProbeBroadcastHandler>();
            services.AddFrameworkBroadcast<ProbeBroadcast, ProbeBroadcastHandler>();
        });

        var publisher = hostA.Services.GetRequiredService<IFrameworkSignalPublisher>();
        publisher.BroadcastProviderId.Should().Be("rabbitmq");
        publisher.TryBroadcast(new ProbeBroadcast("every-node")).Should().BeTrue();

        await Task.WhenAll(stateA.Completed.Task, stateB.Completed.Task).WaitAsync(ct);
        stateA.Observations.Should().Equal("every-node");
        stateB.Observations.Should().Equal("every-node");
    }

    [Fact]
    public async Task Mandatory_confirm_reports_no_receiver_without_local_fallback()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new RabbitState(expected: 1);
        await using var host = await Start(state, rabbit.ConnectionString, ct);
        using var scope = AppHost.PushScope(host.Services);

        var send = () => new NoReceiverOrder().Transport.Send(ct);
        var failure = (await send.Should().ThrowAsync<TransportException>()).Which;

        failure.Failure.Should().Be(TransportException.FailureKind.NoReceivers);
        failure.Acceptance.Adapter.Should().Be("rabbitmq");
        failure.Acceptance.Enumerated.Should().Be(1);
        failure.Acceptance.Accepted.Should().Be(0);
        failure.Acceptance.Rejected.Should().Be(1);
        state.Observations.Should().BeEmpty();
    }

    [Fact]
    public async Task Direct_connector_intent_enables_zero_configuration_orchestration()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new RabbitState(expected: 1);
        await using var host = await Start(state, rabbit.ConnectionString, ct);
        var references = host.Services.GetRequiredService<KoanApplicationReferenceManifest>();
        var evaluator = new RabbitMqOrchestrationEvaluator(references);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Orchestration:Global"] = "Always"
            })
            .Build();

        var decision = await evaluator.Evaluate(configuration, new OrchestrationContext
        {
            Mode = OrchestrationMode.Standalone,
            SessionId = "rabbit-spec",
            AppId = "rabbit-spec",
            AppInstance = "one",
            EnvironmentVariables = []
        });

        decision.Action.Should().Be(OrchestrationAction.ProvisionContainer);
        decision.DependencyDescriptor!.Image.Should().Be("rabbitmq:3.13-management");
        decision.DependencyDescriptor.Environment["RABBITMQ_DEFAULT_USER"].Should().Be("koan");
        decision.DependencyDescriptor.Environment["RABBITMQ_DEFAULT_PASS"].Should().Be("koan");
    }

    [Fact]
    public async Task Unelected_connector_is_healthy_but_not_critical()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new RabbitState(expected: 1);
        await using var host = await KoanIntegrationHost.Configure()
            .WithEnvironment("Test")
            .WithSetting("Koan:Application:Code", "rabbit-spec-" + Guid.NewGuid().ToString("N"))
            .WithSetting("Koan:Communication:RabbitMq:ConnectionString", rabbit.ConnectionString)
            .WithSetting("Koan:Communication:TransportProvider", "in-process")
            .WithSetting("Koan:Communication:FrameworkSignalsProvider", "in-process")
            .WithSetting("Koan:Communication:FrameworkBroadcastsProvider", "in-process")
            .ConfigureServices(services =>
            {
                services.AddSingleton(state);
                services.AddKoan();
            })
            .StartAsync(ct);

        var health = host.Services.GetServices<IHealthContributor>()
            .Single(contributor => contributor.Name == "communication.rabbitmq");
        health.IsCritical.Should().BeFalse();
        var report = await health.Check(ct);
        report.State.Should().Be(HealthState.Healthy);
        report.Data!["active"].Should().Be(false);
    }

    private static Task<IntegrationHost> Start(
        RabbitState state,
        string connectionString,
        CancellationToken ct,
        Action<IServiceCollection>? configure = null)
        => Start(
            state,
            connectionString,
            ct,
            "rabbit-spec-" + Guid.NewGuid().ToString("N"),
            configure);

    private static Task<IntegrationHost> Start(
        RabbitState state,
        string connectionString,
        CancellationToken ct,
        string mesh,
        Action<IServiceCollection>? configure)
        => KoanIntegrationHost.Configure()
            .WithEnvironment("Test")
            .WithSetting("Koan:Application:Code", mesh)
            .WithSetting("Koan:Communication:RabbitMq:ConnectionString", connectionString)
            .ConfigureServices(services =>
            {
                services.AddSingleton(state);
                services.AddKoan();
                configure?.Invoke(services);
            })
            .StartAsync(ct);

    public sealed class FanoutOrder : Entity<FanoutOrder>
    {
        public string Name { get; set; } = "";
    }

    public sealed class FanoutA(RabbitState state) : IReceiveEntity<FanoutOrder>
    {
        public Task Receive(FanoutOrder entity, CancellationToken ct)
        {
            state.Record("A:" + entity.Name);
            entity.Name = "changed-by-a";
            return Task.CompletedTask;
        }
    }

    public sealed class FanoutB(RabbitState state) : IReceiveEntity<FanoutOrder>
    {
        public Task Receive(FanoutOrder entity, CancellationToken ct)
        {
            state.Record("B:" + entity.Name);
            return Task.CompletedTask;
        }
    }

    public sealed class TenantOrder : Entity<TenantOrder>;

    public sealed class TenantReceiver(RabbitState state) : IReceiveEntity<TenantOrder>
    {
        public Task Receive(TenantOrder entity, CancellationToken ct)
        {
            state.Record(Tenant.Current?.Id ?? "missing");
            return Task.CompletedTask;
        }
    }

    public sealed class NoReceiverOrder : Entity<NoReceiverOrder>;

    internal readonly record struct ProbeSignal : IFrameworkSignal<ProbeSignal>
    {
        public static string ContractId => "koan.tests.framework-probe@1";
        public static string GroupId => "koan.tests.framework-probe-handler@1";
    }

    internal sealed class ProbeSignalHandler(RabbitState state) : IHandleFrameworkSignal<ProbeSignal>
    {
        public ValueTask Handle(ProbeSignal signal, CancellationToken ct)
        {
            state.Record("framework-signal");
            return ValueTask.CompletedTask;
        }
    }

    internal readonly record struct ProbeBroadcast(string Value) : IFrameworkBroadcast<ProbeBroadcast>
    {
        public static string ContractId => "koan.tests.framework-broadcast@1";
    }

    internal sealed class ProbeBroadcastHandler(RabbitState state) : IHandleFrameworkBroadcast<ProbeBroadcast>
    {
        public ValueTask Handle(ProbeBroadcast signal, CancellationToken ct)
        {
            state.Record(signal.Value);
            return ValueTask.CompletedTask;
        }
    }

    public sealed class RabbitState(int expected)
    {
        private int _count;
        public ConcurrentQueue<string> Observations { get; } = new();
        public TaskCompletionSource<bool> Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Record(string value)
        {
            Observations.Enqueue(value);
            if (Interlocked.Increment(ref _count) == expected) Completed.TrySetResult(true);
        }
    }
}
