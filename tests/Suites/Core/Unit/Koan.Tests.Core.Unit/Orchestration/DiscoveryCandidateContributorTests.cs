using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
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
    private sealed class TestAdapter(IEnumerable<string> reachable)
        : ServiceDiscoveryAdapterBase(new ConfigurationBuilder().Build(), NullLogger<TestAdapter>.Instance)
    {
        private readonly HashSet<string> _reachable = new(reachable, StringComparer.OrdinalIgnoreCase);
        public override string ServiceName => "testsvc";
        protected override Type GetFactoryType() => typeof(TestServiceFactory);
        protected override Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken ct)
            => Task.FromResult(_reachable.Contains(serviceUrl));
    }

    private static DiscoveryContext Ctx(IReadOnlyList<DiscoveryCandidate>? contributed = null) => new()
    {
        RequireHealthValidation = true,
        HealthCheckTimeout = TimeSpan.FromSeconds(1),
        ContributedCandidates = contributed
    };

    [Fact(DisplayName = "an unreachable contributed candidate falls through to a reachable standard candidate")]
    public async Task Unreachable_contributed_candidate_falls_through()
    {
        // ZenGarden contributed an unreachable address (the 172.19.0.1 scenario); only localhost is reachable.
        var adapter = new TestAdapter(reachable: ["tcp://localhost:5555"]);
        var zg = new DiscoveryCandidate("tcp://172.19.0.1:5555", "zengarden-offering", 2);

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
        var zg = new DiscoveryCandidate("tcp://zg-host:5555", "zengarden-offering", 2);

        var result = await adapter.Discover(Ctx([zg]));

        result.ServiceUrl.Should().Be("tcp://zg-host:5555");
        result.DiscoveryMethod.Should().Be("zengarden-offering");
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

    [Fact(DisplayName = "the coordinator folds contributor candidates into the context and swallows a throwing contributor")]
    public async Task Coordinator_folds_and_swallows()
    {
        DiscoveryContext? seen = null;
        var recording = new RecordingAdapter(ctx => seen = ctx);
        var good = new FakeContributor((svc, _) => [new DiscoveryCandidate($"tcp://zg-{svc}:1", "zg", 2)]);
        var bad = new FakeContributor((_, _) => throw new InvalidOperationException("boom"));

        var coordinator = new ServiceDiscoveryCoordinator(
            [recording],
            [bad, good],   // the throwing contributor must not break discovery
            NullLogger<ServiceDiscoveryCoordinator>.Instance);

        await coordinator.DiscoverService("testsvc", Ctx());

        seen.Should().NotBeNull();
        seen!.ContributedCandidates.Should().ContainSingle().Which.Url.Should().Be("tcp://zg-testsvc:1");
    }
}
