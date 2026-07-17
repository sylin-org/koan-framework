using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Diagnostics;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Services;
using Koan.ZenGarden.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Koan.ZenGarden.Tests;

public sealed class ZenGardenServiceDiscoveryContributionSpec
{
    private const string Endpoint = "mongodb://garden-mongo:27017";
    private const string LocalEndpoint = "mongodb://localhost:27017";

    [Fact]
    public async Task AddKoan_activates_one_alias_aware_ZenGarden_source_from_package_intent()
    {
        var source = new RecordingProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IZenGardenInitializationProvider>(source);
        services.AddSingleton<IServiceDiscoveryAdapter>(new MongoProbeAdapter());

        services.AddKoan();

        using var provider = services.BuildServiceProvider();
        var coordinator = provider.GetRequiredService<IServiceDiscoveryCoordinator>();
        var result = await coordinator.DiscoverService("mongo", new DiscoveryContext());

        result.IsSuccessful.Should().BeTrue();
        result.ServiceUrl.Should().Be(Endpoint);
        result.DiscoveryMethod.Should().Be(Constants.Composition.SourceId);
        source.Selectors.Should().Equal("mongo", "mongodb");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unavailable_or_throwing_automatic_source_falls_through_without_a_rejection(bool throwOnResolve)
    {
        var source = new RecordingProvider(resolveAlias: false, throwOnResolve);
        var facts = new CapturingFacts();
        var services = new ServiceCollection();
        services.AddSingleton<IZenGardenInitializationProvider>(source);
        services.AddSingleton<IServiceDiscoveryAdapter>(new MongoProbeAdapter());
        services.AddSingleton<IKoanRuntimeFactRecorder>(facts);

        services.AddKoan();

        using var provider = services.BuildServiceProvider();
        var coordinator = provider.GetRequiredService<IServiceDiscoveryCoordinator>();
        var result = await coordinator.DiscoverService("mongo", new DiscoveryContext());

        result.IsSuccessful.Should().BeTrue();
        result.ServiceUrl.Should().Be(LocalEndpoint);
        result.DiscoveryMethod.Should().Be("local");
        facts.Descriptors.Should().ContainSingle().Which.State.Should().Be(KoanFactState.Selected);
        facts.Descriptors.Should().NotContain(fact =>
            fact.State == KoanFactState.Rejected || fact.State == KoanFactState.Degraded);
    }

    [KoanService(
        ServiceKind.Database,
        shortCode: "mongo",
        name: "Mongo",
        Scheme = "mongodb",
        Host = "mongo",
        EndpointPort = 27017,
        LocalScheme = "mongodb",
        LocalHost = "localhost",
        LocalPort = 27017)]
    private sealed class MongoFactory { }

    private sealed class MongoProbeAdapter
        : ServiceDiscoveryAdapterBase
    {
        public MongoProbeAdapter()
            : base(new ConfigurationBuilder().Build(), NullLogger<MongoProbeAdapter>.Instance)
        {
        }

        public override string ServiceName => "mongo";

        public override string[] Aliases => ["mongodb"];

        protected override Type GetFactoryType() => typeof(MongoFactory);

        protected override Task<bool> ValidateServiceHealth(
            string serviceUrl,
            DiscoveryContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                string.Equals(serviceUrl, Endpoint, StringComparison.Ordinal)
                || string.Equals(serviceUrl, LocalEndpoint, StringComparison.Ordinal));
    }

    private sealed class RecordingProvider(
        bool resolveAlias = true,
        bool throwOnResolve = false) : IZenGardenInitializationProvider
    {
        public List<string> Selectors { get; } = [];

        public ValueTask<ZenGardenOfferingResolution?> Resolve(
            ZenGardenConnectionIntent intent,
            CancellationToken cancellationToken = default)
        {
            Selectors.Add(intent.Offering);
            if (throwOnResolve)
            {
                throw new InvalidOperationException("topology unavailable");
            }

            return ValueTask.FromResult<ZenGardenOfferingResolution?>(
                resolveAlias && string.Equals(intent.Offering, "mongodb", StringComparison.Ordinal)
                    ? new ZenGardenOfferingResolution
                    {
                        ToolFqid = "mongodb:default",
                        Offering = "mongodb",
                        Uris = [Endpoint],
                    }
                    : null);
        }

        public ValueTask<ZenGardenCapabilityWishReceipt?> WishCapabilities(
            ZenGardenConnectionIntent intent,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ZenGardenCapabilityWishReceipt?>(null);
    }

    private sealed class CapturingFacts : IKoanRuntimeFactRecorder
    {
        public List<KoanFactDescriptor> Descriptors { get; } = [];

        public void Record(KoanFactDescriptor descriptor) => Descriptors.Add(descriptor);
    }
}
