using System.Collections.Concurrent;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Diagnostics;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Orchestration.Composition;
using Koan.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Tests.Core.Unit.Orchestration;

public sealed class ServiceDiscoveryPlanSpec
{
    private const string SourceEndpoint = "tcp://source-host:5555";
    private const string LocalEndpoint = "tcp://localhost:5555";

    [KoanService(ServiceKind.Database, shortCode: "testsvc", name: "TestSvc",
        Scheme = "tcp", Host = "container-host", EndpointPort = 5555,
        LocalScheme = "tcp", LocalHost = "localhost", LocalPort = 5555)]
    private sealed class TestServiceFactory { }

    [Fact(DisplayName = "an empty host plan preserves autonomous adapter discovery")]
    public async Task Empty_plan_preserves_baseline()
    {
        var adapter = new TestAdapter([LocalEndpoint]);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var coordinator = Coordinator(adapter, new ServiceDiscoveryRuntime(provider, ServiceDiscoveryPlan.Empty));

        var result = await coordinator.DiscoverService("test-alias", Context());

        result.IsSuccessful.Should().BeTrue();
        result.ServiceUrl.Should().Be(LocalEndpoint);
        result.DiscoveryMethod.Should().Contain("local");
    }

    [Fact(DisplayName = "the plan preserves owner order and sorts source identities within each owner")]
    public void Plan_order_is_deterministic()
    {
        var builder = new ServiceDiscoveryPlanBuilder();
        var secondConstitutionOwner = builder.ForOwner("module.second");
        secondConstitutionOwner.AddSource<SourceB>("source.z");
        secondConstitutionOwner.AddSource<SourceA>("source.a");
        builder.ForOwner("module.first").AddSource<SourceC>("source.m");

        var plan = builder.Build();

        plan.Sources.Select(static source => $"{source.Owner}/{source.Id}").Should().Equal(
            "module.second/source.a",
            "module.second/source.z",
            "module.first/source.m");
    }

    [Fact(DisplayName = "duplicate stable source identities reject the plan before publication")]
    public void Duplicate_source_ids_are_rejected()
    {
        var builder = new ServiceDiscoveryPlanBuilder();
        builder.ForOwner("module.one").AddSource<SourceA>("source.shared");
        builder.ForOwner("module.two").AddSource<SourceB>("source.shared");

        var act = builder.Build;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*source.shared*more than once*");
    }

    [Fact(DisplayName = "two sources cannot claim the same explicit intent scheme")]
    public void Duplicate_intent_schemes_are_rejected()
    {
        var builder = new ServiceDiscoveryPlanBuilder();
        builder.ForOwner("module.one").AddSource<SourceA>("source.one", "zen-garden");
        builder.ForOwner("module.two").AddSource<SourceB>("source.two", "ZEN-GARDEN");

        var act = builder.Build;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*zen-garden*claimed by both*");
    }

    [Fact(DisplayName = "one source instance serves repeated live queries without recompiling the plan")]
    public async Task Source_is_resolved_once_and_queried_each_operation()
    {
        var source = new RecordingSource((_, _) => Candidates(SourceEndpoint, priority: -100));
        var builder = new ServiceDiscoveryPlanBuilder();
        var target = builder.ForOwner("module.source");
        target.AddSource<RecordingSource>("source.live", "source");
        var plan = builder.Build();
        builder.Build().Should().BeSameAs(plan);

        var resolutions = 0;
        var services = new ServiceCollection();
        services.AddTransient<RecordingSource>(_ =>
        {
            resolutions++;
            return source;
        });
        using var provider = services.BuildServiceProvider();
        var runtime = new ServiceDiscoveryRuntime(provider, plan);
        var coordinator = Coordinator(new TestAdapter([SourceEndpoint, LocalEndpoint]), runtime);

        await coordinator.DiscoverService("testsvc", Context());
        await coordinator.DiscoverService("testsvc", Context());

        resolutions.Should().Be(1);
        source.QueryCount.Should().Be(2);
        source.LastRequest!.ServiceName.Should().Be("testsvc");
        source.LastRequest.ServiceSelectors.Should().Equal("testsvc", "test-alias", "mongodb");
        source.LastRequest.Intent.Should().BeNull();
    }

    [Fact(DisplayName = "a healthy automatic source wins ahead of autonomous topology")]
    public async Task Healthy_automatic_source_wins()
    {
        var source = new RecordingSource((_, _) => Candidates(SourceEndpoint));
        using var fixture = RuntimeWith(source);
        var coordinator = Coordinator(new TestAdapter([SourceEndpoint, LocalEndpoint]), fixture.Runtime);

        var result = await coordinator.DiscoverService("testsvc", Context());

        result.ServiceUrl.Should().Be(SourceEndpoint);
        result.DiscoveryMethod.Should().Be("optional-source");
    }

    [Fact(DisplayName = "an unhealthy automatic source falls through to autonomous topology")]
    public async Task Unhealthy_automatic_source_falls_through()
    {
        var source = new RecordingSource((_, _) => Candidates(SourceEndpoint));
        using var fixture = RuntimeWith(source);
        var adapter = new TestAdapter([LocalEndpoint]);
        var coordinator = Coordinator(adapter, fixture.Runtime);

        var result = await coordinator.DiscoverService("testsvc", Context());

        result.ServiceUrl.Should().Be(LocalEndpoint);
        adapter.Attempts.Should().ContainInOrder(SourceEndpoint, LocalEndpoint);
    }

    [Fact(DisplayName = "a throwing automatic source is reported safely and does not block fallback")]
    public async Task Throwing_automatic_source_is_safe_fallback()
    {
        const string sensitiveFailure = "failed at tcp://private.example:5555?token=secret";
        var source = new RecordingSource((_, _) => throw new InvalidOperationException(sensitiveFailure));
        using var fixture = RuntimeWith(source);
        var capture = new LogCapture();
        var coordinator = Coordinator(
            new TestAdapter([LocalEndpoint]),
            fixture.Runtime,
            new CapturingLogger<ServiceDiscoveryCoordinator>(capture));

        var result = await coordinator.DiscoverService("testsvc", Context());

        result.ServiceUrl.Should().Be(LocalEndpoint);
        capture.Messages.Should().Contain(message => message.Contains("source.live", StringComparison.Ordinal));
        capture.Messages.Should().Contain(message => message.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
        capture.Messages.Should().NotContain(message => message.Contains("private.example", StringComparison.Ordinal));
        capture.Messages.Should().NotContain(message => message.Contains("secret", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "caller cancellation propagates through a live source query")]
    public async Task Source_query_preserves_cancellation()
    {
        var source = new RecordingSource((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Candidates(SourceEndpoint);
        });
        using var fixture = RuntimeWith(source);
        var coordinator = Coordinator(new TestAdapter([LocalEndpoint]), fixture.Runtime);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => coordinator.DiscoverService("testsvc", Context(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact(DisplayName = "concrete application configuration outranks every automatic source candidate")]
    public async Task Explicit_configuration_outranks_automatic_source()
    {
        const string explicitEndpoint = "tcp://explicit:5555";
        var source = new RecordingSource((_, _) => Candidates(SourceEndpoint, priority: -100));
        using var fixture = RuntimeWith(source);
        var adapter = new TestAdapter(
            [explicitEndpoint, SourceEndpoint],
            explicitConfiguration: explicitEndpoint);
        var coordinator = Coordinator(adapter, fixture.Runtime);

        var result = await coordinator.DiscoverService("testsvc", Context());

        result.ServiceUrl.Should().Be(explicitEndpoint);
        result.DiscoveryMethod.Should().Be("explicit-config");
    }

    [Fact(DisplayName = "Aspire remains first within the automatic discovery slot")]
    public async Task Aspire_outranks_optional_source_within_automatic_slot()
    {
        const string aspireEndpoint = "tcp://aspire:5555";
        var source = new RecordingSource((_, _) => Candidates(SourceEndpoint));
        using var fixture = RuntimeWith(source);
        var adapter = new TestAdapter(
            [aspireEndpoint, SourceEndpoint],
            aspireEndpoint: aspireEndpoint);
        var coordinator = Coordinator(adapter, fixture.Runtime);

        var result = await coordinator.DiscoverService(
            "testsvc",
            Context(OrchestrationMode.AspireAppHost));

        result.ServiceUrl.Should().Be(aspireEndpoint);
        result.DiscoveryMethod.Should().Be("aspire-discovery");
    }

    [Fact(DisplayName = "an unknown explicit source scheme fails without autonomous fallback")]
    public async Task Unknown_required_scheme_never_falls_back()
    {
        var source = new RecordingSource((_, _) => Candidates(SourceEndpoint));
        using var fixture = RuntimeWith(source);
        var adapter = new TestAdapter([LocalEndpoint]);
        var coordinator = Coordinator(adapter, fixture.Runtime);

        var result = await coordinator.ResolveServiceIntent(
            "testsvc",
            "unknown://offering",
            Context());

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No active discovery source");
        adapter.Attempts.Should().BeEmpty();
        source.QueryCount.Should().Be(0);
    }

    [Fact(DisplayName = "a failed explicit source query fails without autonomous fallback")]
    public async Task Failed_required_source_never_falls_back()
    {
        var source = new RecordingSource((_, _) => throw new InvalidOperationException("provider unavailable"));
        using var fixture = RuntimeWith(source);
        var adapter = new TestAdapter([LocalEndpoint]);
        var coordinator = Coordinator(adapter, fixture.Runtime);

        var result = await coordinator.ResolveServiceIntent(
            "test-alias",
            "source://offering",
            Context());

        result.IsSuccessful.Should().BeFalse();
        adapter.Attempts.Should().BeEmpty();
        source.LastRequest!.ServiceName.Should().Be("testsvc");
        source.LastRequest.ServiceSelectors.Should().Equal("testsvc", "test-alias", "mongodb");
        source.LastRequest.Intent.Should().Be("source://offering");
    }

    [Fact(DisplayName = "an unhealthy explicit source candidate cannot weaken into local discovery")]
    public async Task Unhealthy_required_candidate_never_falls_back()
    {
        var source = new RecordingSource((_, _) => Candidates(SourceEndpoint));
        using var fixture = RuntimeWith(source);
        var adapter = new TestAdapter([LocalEndpoint]);
        var coordinator = Coordinator(adapter, fixture.Runtime);

        var result = await coordinator.ResolveServiceIntent(
            "testsvc",
            "source://offering",
            Context());

        result.IsSuccessful.Should().BeFalse();
        adapter.Attempts.Should().Equal(SourceEndpoint);
    }

    [Fact(DisplayName = "the elected method is factual while the endpoint stays out of runtime facts")]
    public async Task Election_fact_is_safe()
    {
        var recorder = new CapturingFactRecorder();
        var source = new RecordingSource((_, _) => Candidates("tcp://agent:secret@private.example:5555"));
        using var fixture = RuntimeWith(source);
        var coordinator = Coordinator(
            new TestAdapter(["tcp://agent:secret@private.example:5555"]),
            fixture.Runtime,
            facts: recorder);

        await coordinator.DiscoverService("testsvc", Context());

        var fact = recorder.Facts.Should().ContainSingle().Subject;
        fact.Code.Should().Be("koan.discovery.service");
        fact.Kind.Should().Be(KoanFactKind.Discovery);
        fact.State.Should().Be(KoanFactState.Selected);
        fact.Subject.Should().Be("service:testsvc");
        fact.Summary.Should().Contain("optional-source");
        fact.Summary.Should().NotContain("private.example");
        fact.Summary.Should().NotContain("secret");
    }

    private static ServiceDiscoveryCoordinator Coordinator(
        IServiceDiscoveryAdapter adapter,
        ServiceDiscoveryRuntime runtime,
        ILogger<ServiceDiscoveryCoordinator>? logger = null,
        IKoanRuntimeFactRecorder? facts = null) =>
        new([adapter], runtime, logger ?? NullLogger<ServiceDiscoveryCoordinator>.Instance, facts);

    private static RuntimeFixture RuntimeWith(RecordingSource source)
    {
        var builder = new ServiceDiscoveryPlanBuilder();
        builder.ForOwner("module.optional").AddSource<RecordingSource>("source.live", "source");
        var services = new ServiceCollection();
        services.AddSingleton(source);
        var provider = services.BuildServiceProvider();
        return new RuntimeFixture(provider, new ServiceDiscoveryRuntime(provider, builder.Build()));
    }

    private static DiscoveryContext Context(
        OrchestrationMode mode = OrchestrationMode.Standalone) => new()
    {
        OrchestrationMode = mode,
        RequireHealthValidation = true,
        HealthCheckTimeout = TimeSpan.FromSeconds(1)
    };

    private static Task<IReadOnlyList<DiscoveryCandidate>> Candidates(
        string endpoint,
        int priority = DiscoveryCandidatePriority.Automatic) =>
        Task.FromResult<IReadOnlyList<DiscoveryCandidate>>(
            [new DiscoveryCandidate(endpoint, "optional-source", priority)]);

    private sealed class TestAdapter(
        IEnumerable<string> reachable,
        string? explicitConfiguration = null,
        string? aspireEndpoint = null)
        : ServiceDiscoveryAdapterBase(new ConfigurationBuilder().Build(), NullLogger<TestAdapter>.Instance)
    {
        private readonly HashSet<string> _reachable = new(reachable, StringComparer.OrdinalIgnoreCase);

        public List<string> Attempts { get; } = [];
        public override string ServiceName => "testsvc";
        public override string[] Aliases => ["test-alias", "mongodb"];
        protected override Type GetFactoryType() => typeof(TestServiceFactory);
        protected override string? ReadExplicitConfiguration() => explicitConfiguration;
        protected override string? ReadAspireServiceDiscovery() => aspireEndpoint;

        protected override Task<bool> ValidateServiceHealth(
            string serviceUrl,
            DiscoveryContext context,
            CancellationToken cancellationToken)
        {
            Attempts.Add(serviceUrl);
            return Task.FromResult(_reachable.Contains(serviceUrl));
        }
    }

    private sealed class RecordingSource(
        Func<DiscoveryCandidateRequest, CancellationToken, Task<IReadOnlyList<DiscoveryCandidate>>> query)
        : IDiscoveryCandidateSource
    {
        public int QueryCount { get; private set; }
        public DiscoveryCandidateRequest? LastRequest { get; private set; }

        public Task<IReadOnlyList<DiscoveryCandidate>> GetCandidates(
            DiscoveryCandidateRequest request,
            CancellationToken cancellationToken = default)
        {
            QueryCount++;
            LastRequest = request;
            return query(request, cancellationToken);
        }
    }

    private sealed class SourceA : EmptySource { }
    private sealed class SourceB : EmptySource { }
    private sealed class SourceC : EmptySource { }

    private abstract class EmptySource : IDiscoveryCandidateSource
    {
        public Task<IReadOnlyList<DiscoveryCandidate>> GetCandidates(
            DiscoveryCandidateRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DiscoveryCandidate>>([]);
    }

    private sealed record RuntimeFixture(ServiceProvider Provider, ServiceDiscoveryRuntime Runtime) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private sealed class CapturingFactRecorder : IKoanRuntimeFactRecorder
    {
        public List<KoanFactDescriptor> Facts { get; } = [];
        public void Record(KoanFactDescriptor descriptor) => Facts.Add(descriptor);
    }

    private sealed class LogCapture
    {
        public ConcurrentQueue<string> Messages { get; } = new();
    }

    private sealed class CapturingLogger<T>(LogCapture capture) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            capture.Messages.Enqueue(formatter(state, exception));
    }
}
