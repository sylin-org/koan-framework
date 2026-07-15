using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Diagnostics;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Orchestration;
using Koan.Orchestration.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Koan.Tests.Core.Unit.Orchestration;

/// <summary>
/// Guards the discovery-candidate contributor seam (the "ZenGarden contributes, never short-circuits" fix): an
/// EXTERNAL discovery source contributes candidates into the adapter's HEALTH-CHECKED probe. An unreachable
/// contributed answer — e.g. a same-host offering advertised at the docker bridge gateway 172.19.0.1 for a
/// loopback-bound host service — must fall through to the standard candidates instead of stranding the app.
/// </summary>
public class DiscoveryCandidateContributorTests
{
    // A factory carrying the KoanServiceAttribute the base reads for its standard (compose/host/local) candidates.
    [KoanService(ServiceKind.Database, shortCode: "testsvc", name: "TestSvc",
        Scheme = "tcp", Host = "container-host", EndpointPort = 5555,
        LocalScheme = "tcp", LocalHost = "localhost", LocalPort = 5555)]
    private sealed class TestServiceFactory { }

    // A test adapter whose health check passes only for URLs in the reachable set.
    private sealed class TestAdapter(
        IEnumerable<string> reachable,
        string? explicitConfiguration = null,
        string? aspireEndpoint = null,
        IReadOnlyList<DiscoveryCandidate>? runtimeCandidates = null,
        IReadOnlyList<DiscoveryCandidate>? environmentCandidates = null)
        : ServiceDiscoveryAdapterBase(new ConfigurationBuilder().Build(), NullLogger<TestAdapter>.Instance)
    {
        private readonly HashSet<string> _reachable = new(reachable, StringComparer.OrdinalIgnoreCase);
        public override string ServiceName => "testsvc";
        protected override Type GetFactoryType() => typeof(TestServiceFactory);
        protected override string? ReadExplicitConfiguration() => explicitConfiguration;
        protected override string? ReadAspireServiceDiscovery() => aspireEndpoint;
        protected override IEnumerable<DiscoveryCandidate> GetEnvironmentCandidates() => environmentCandidates ?? [];
        protected override IEnumerable<DiscoveryCandidate> BuildRuntimeCandidates(KoanServiceAttribute attribute) =>
            runtimeCandidates ?? base.BuildRuntimeCandidates(attribute);
        protected override Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken ct)
            => Task.FromResult(_reachable.Contains(serviceUrl));
    }

    private static DiscoveryContext Ctx(
        IReadOnlyList<DiscoveryCandidate>? contributed = null,
        OrchestrationMode orchestrationMode = OrchestrationMode.Standalone,
        bool requireHealthValidation = true) => new()
    {
        OrchestrationMode = orchestrationMode,
        RequireHealthValidation = requireHealthValidation,
        HealthCheckTimeout = TimeSpan.FromSeconds(1),
        ContributedCandidates = contributed
    };

    [Fact(DisplayName = "an unreachable contributed candidate falls through to a reachable standard candidate")]
    public async Task Unreachable_contributed_candidate_falls_through()
    {
        // ZenGarden contributed an unreachable address (the 172.19.0.1 scenario); only localhost is reachable.
        var adapter = new TestAdapter(reachable: ["tcp://localhost:5555"]);
        var zg = new DiscoveryCandidate(
            "tcp://172.19.0.1:5555",
            "zengarden-offering",
            DiscoveryCandidatePriority.Automatic);

        var result = await adapter.Discover(Ctx([zg]));

        result.IsSuccessful.Should().BeTrue();
        result.ServiceUrl.Should().Be("tcp://localhost:5555");
        result.DiscoveryMethod.Should().NotBe("zengarden-offering", "an unreachable ZG answer must not strand discovery");
    }

    [Fact(DisplayName = "a reachable contributed candidate wins and is tried ahead of the local guess")]
    public async Task Reachable_contributed_candidate_wins()
    {
        // Both the ZG answer and localhost are reachable; the contributed candidate is tried first.
        var adapter = new TestAdapter(reachable: ["tcp://zg-host:5555", "tcp://localhost:5555"]);
        var zg = new DiscoveryCandidate(
            "tcp://zg-host:5555",
            "zengarden-offering",
            DiscoveryCandidatePriority.Automatic);

        var result = await adapter.Discover(Ctx([zg]));

        result.ServiceUrl.Should().Be("tcp://zg-host:5555");
        result.DiscoveryMethod.Should().Be("zengarden-offering");
    }

    [Fact(DisplayName = "explicit configuration remains authoritative when Aspire also contributes an endpoint")]
    public async Task Explicit_configuration_wins_over_aspire()
    {
        var explicitEndpoint = "tcp://explicit:5555";
        var aspireEndpoint = "tcp://aspire:5555";
        var adapter = new TestAdapter(
            reachable: [explicitEndpoint, aspireEndpoint],
            explicitConfiguration: explicitEndpoint,
            aspireEndpoint: aspireEndpoint);

        var result = await adapter.Discover(Ctx(orchestrationMode: OrchestrationMode.AspireAppHost));

        result.ServiceUrl.Should().Be(explicitEndpoint);
        result.DiscoveryMethod.Should().Be("explicit-config");
    }

    [Fact(DisplayName = "concrete application configuration wins over legacy environment hints")]
    public async Task Explicit_configuration_wins_over_environment_hint()
    {
        var explicitEndpoint = "tcp://explicit:5555";
        var environmentEndpoint = "tcp://environment:5555";
        var adapter = new TestAdapter(
            reachable: [explicitEndpoint, environmentEndpoint],
            explicitConfiguration: explicitEndpoint,
            environmentCandidates:
            [
                new DiscoveryCandidate(environmentEndpoint, "environment", DiscoveryCandidatePriority.Environment)
            ]);

        var result = await adapter.Discover(Ctx());

        result.ServiceUrl.Should().Be(explicitEndpoint);
        result.DiscoveryMethod.Should().Be("explicit-config");
    }

    [Fact(DisplayName = "the auto declaration delegates even when health validation is disabled")]
    public async Task Auto_configuration_is_never_an_endpoint()
    {
        var environmentEndpoint = "tcp://environment:5555";
        var adapter = new TestAdapter(
            reachable: [],
            explicitConfiguration: " auto ",
            environmentCandidates:
            [
                new DiscoveryCandidate(environmentEndpoint, "environment", DiscoveryCandidatePriority.Environment)
            ]);

        var result = await adapter.Discover(Ctx(requireHealthValidation: false));

        result.ServiceUrl.Should().Be(environmentEndpoint);
        result.DiscoveryMethod.Should().Be("environment");
    }

    [Fact(DisplayName = "Aspire wins the automatic slot ahead of contributed and local candidates")]
    public async Task Aspire_wins_within_automatic_candidates()
    {
        var aspireEndpoint = "tcp://aspire:5555";
        var contributedEndpoint = "tcp://zg-host:5555";
        var adapter = new TestAdapter(
            reachable: [aspireEndpoint, contributedEndpoint, "tcp://localhost:5555"],
            aspireEndpoint: aspireEndpoint);
        var contributed = new DiscoveryCandidate(
            contributedEndpoint,
            "zengarden-offering",
            DiscoveryCandidatePriority.Automatic);

        var result = await adapter.Discover(Ctx([contributed], OrchestrationMode.AspireAppHost));

        result.ServiceUrl.Should().Be(aspireEndpoint);
        result.DiscoveryMethod.Should().Be("aspire-discovery");
    }

    [Fact(DisplayName = "adapter-specific topology cannot replace the shared activated-contributor pipeline")]
    public async Task Runtime_topology_hook_preserves_layered_contributors()
    {
        var contributedEndpoint = "tcp://zg-host:5555";
        var runtimeEndpoint = "tcp://adapter-runtime:5555";
        var adapter = new TestAdapter(
            reachable: [contributedEndpoint, runtimeEndpoint],
            runtimeCandidates:
            [
                new DiscoveryCandidate(
                    runtimeEndpoint,
                    "adapter-runtime",
                    DiscoveryCandidatePriority.Automatic)
            ]);
        var contributed = new DiscoveryCandidate(
            contributedEndpoint,
            "zengarden-offering",
            DiscoveryCandidatePriority.Automatic);

        var result = await adapter.Discover(Ctx([contributed]));

        result.ServiceUrl.Should().Be(contributedEndpoint);
        result.DiscoveryMethod.Should().Be("zengarden-offering");
    }

    [Fact(DisplayName = "layered and runtime candidates cannot assign themselves above explicit application intent")]
    public async Task Automatic_candidates_are_normalized_below_explicit_configuration()
    {
        var explicitEndpoint = "tcp://explicit:5555";
        var contributedEndpoint = "tcp://zg-host:5555";
        var runtimeEndpoint = "tcp://adapter-runtime:5555";
        var adapter = new TestAdapter(
            reachable: [explicitEndpoint, contributedEndpoint, runtimeEndpoint],
            explicitConfiguration: explicitEndpoint,
            runtimeCandidates:
            [
                new DiscoveryCandidate(
                    runtimeEndpoint,
                    "adapter-runtime",
                    DiscoveryCandidatePriority.Environment)
            ]);
        var contributed = new DiscoveryCandidate(
            contributedEndpoint,
            "zengarden-offering",
            DiscoveryCandidatePriority.Environment);

        var result = await adapter.Discover(Ctx([contributed]));

        result.ServiceUrl.Should().Be(explicitEndpoint);
        result.DiscoveryMethod.Should().Be("explicit-config");
    }

    private sealed class RecordingAdapter(Action<DiscoveryContext> onDiscover) : IServiceDiscoveryAdapter
    {
        public string ServiceName => "testsvc";
        public string[] Aliases => [];
        public int Priority => 10;
        public Task<AdapterDiscoveryResult> Discover(DiscoveryContext context, CancellationToken ct = default)
        {
            onDiscover(context);
            return Task.FromResult(AdapterDiscoveryResult.Success(ServiceName, "tcp://x:1", "test"));
        }
    }

    private sealed class FakeContributor(Func<string, DiscoveryContext, IReadOnlyList<DiscoveryCandidate>> fn)
        : IDiscoveryCandidateContributor
    {
        public Task<IReadOnlyList<DiscoveryCandidate>> ContributeCandidates(
            string serviceName, DiscoveryContext context, CancellationToken ct = default)
            => Task.FromResult(fn(serviceName, context));
    }

    private sealed class CapturingFactRecorder : IKoanRuntimeFactRecorder
    {
        public List<KoanFactDescriptor> Facts { get; } = [];
        public void Record(KoanFactDescriptor descriptor) => Facts.Add(descriptor);
    }

    [Fact(DisplayName = "the coordinator folds contributor candidates into the context and swallows a throwing contributor")]
    public async Task Coordinator_folds_and_swallows()
    {
        DiscoveryContext? seen = null;
        var recording = new RecordingAdapter(ctx => seen = ctx);
        var good = new FakeContributor((svc, _) =>
            [new DiscoveryCandidate(
                $"tcp://zg-{svc}:1",
                "zg",
                DiscoveryCandidatePriority.Automatic)]);
        var bad = new FakeContributor((_, _) => throw new InvalidOperationException("boom"));

        var coordinator = new ServiceDiscoveryCoordinator(
            [recording],
            [bad, good],   // the throwing contributor must not break discovery
            NullLogger<ServiceDiscoveryCoordinator>.Instance);

        await coordinator.DiscoverService("testsvc", Ctx());

        seen.Should().NotBeNull();
        seen!.ContributedCandidates.Should().ContainSingle().Which.Url.Should().Be("tcp://zg-testsvc:1");
    }

    [Fact(DisplayName = "the coordinator reports the elected method without exposing the endpoint")]
    public async Task Coordinator_records_safe_election_fact()
    {
        var recorder = new CapturingFactRecorder();
        var recording = new RecordingAdapter(_ => { });
        var coordinator = new ServiceDiscoveryCoordinator(
            [recording],
            [],
            NullLogger<ServiceDiscoveryCoordinator>.Instance,
            recorder);

        await coordinator.DiscoverService("testsvc", Ctx());

        var fact = recorder.Facts.Should().ContainSingle().Subject;
        fact.Code.Should().Be("koan.discovery.service");
        fact.Kind.Should().Be(KoanFactKind.Discovery);
        fact.State.Should().Be(KoanFactState.Selected);
        fact.Subject.Should().Be("service:testsvc");
        fact.Summary.Should().Contain("'test'");
        fact.Summary.Should().NotContain("tcp://x:1");
    }
}
