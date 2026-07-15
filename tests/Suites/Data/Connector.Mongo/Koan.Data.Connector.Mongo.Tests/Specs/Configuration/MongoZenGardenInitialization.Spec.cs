using Koan.Core.Adapters;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Connector.Mongo.Initialization;
using Koan.ZenGarden.Core;
using Koan.ZenGarden.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Configuration;

public sealed class MongoZenGardenInitializationSpec
{
    [Fact]
    public void Auto_connection_delegates_to_shared_discovery()
    {
        var discovery = new CapturingDiscoveryCoordinator(
            AdapterDiscoveryResult.Success(
                "mongo",
                "mongodb://mongo-zen:27019/appdb",
                "zengarden-offering"));
        using var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["Koan:Data:Mongo:Database"] = "appdb"
            },
            discoveryCoordinator: discovery);

        var options = provider.GetRequiredService<IOptions<MongoOptions>>().Value;

        options.ConnectionString.Should().Be("mongodb://mongo-zen:27019/appdb");
        discovery.RequestedService.Should().Be("mongo");
        discovery.Context.Should().NotBeNull();
        discovery.Context!.RequireHealthValidation.Should().BeTrue();
        discovery.Context.Parameters.Should().ContainKey("database").WhoseValue.Should().Be("appdb");
    }

    [Fact]
    public async Task Binding_metadata_is_inert_without_the_zen_garden_engine()
    {
        using var provider = BuildLayeredDiscoveryProvider(activateZenGarden: false);

        provider.GetServices<IZenGardenOfferingBinding>()
            .Should().ContainSingle(binding => binding.AdapterId == "mongo" && binding.Offering == "mongodb");
        provider.GetServices<IDiscoveryCandidateContributor>().Should().BeEmpty();

        var result = await provider.GetRequiredService<IServiceDiscoveryCoordinator>()
            .DiscoverService("mongo", new DiscoveryContext { RequireHealthValidation = false });

        result.ServiceUrl.Should().Be("mongodb://localhost:27017");
        result.DiscoveryMethod.Should().Be("local-default");
    }

    [Fact]
    public async Task Activating_zen_garden_layers_its_candidate_into_shared_discovery()
    {
        using var provider = BuildLayeredDiscoveryProvider(activateZenGarden: true);

        provider.GetServices<IDiscoveryCandidateContributor>().Should().ContainSingle();

        var result = await provider.GetRequiredService<IServiceDiscoveryCoordinator>()
            .DiscoverService("mongo", new DiscoveryContext { RequireHealthValidation = false });

        result.ServiceUrl.Should().Be("mongodb://mongo-zen:27019");
        result.DiscoveryMethod.Should().Be("zengarden-offering");
    }

    [Fact]
    public void Explicit_zen_garden_uri_is_interpreted_and_converted()
    {
        using var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["Koan:Data:Mongo:ConnectionString"] = "zen-garden://mongodb:dev",
                ["Koan:Data:Mongo:Database"] = "mydb"
            },
            new StubZenGardenProvider(_ => new ZenGardenOfferingResolution
            {
                ToolFqid = "mongodb:dev",
                Offering = "mongodb",
                Instance = "dev",
                Uris = new[] { "mongodb://mongo-dev:27021" }
            }));

        var options = provider.GetRequiredService<IOptions<MongoOptions>>().Value;

        options.ConnectionString.Should().Be("mongodb://mongo-dev:27021/mydb");
    }

    [Fact]
    public void Unresolved_zen_garden_uri_falls_back_to_autonomous_discovery_defaults()
    {
        using var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["Koan:Data:Mongo:ConnectionString"] = "zen-garden://mongodb",
                ["Koan:Data:Mongo:Database"] = "fallbackdb"
            },
            new StubZenGardenProvider(_ => null));

        var options = provider.GetRequiredService<IOptions<MongoOptions>>().Value;

        options.ConnectionString.Should().Be("mongodb://localhost:27017/fallbackdb");
    }

    private static ServiceProvider BuildProvider(
        IDictionary<string, string?> settings,
        IZenGardenInitializationProvider? zenGardenProvider = null,
        IServiceDiscoveryCoordinator? discoveryCoordinator = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IOptions<AdaptersReadinessOptions>>(Options.Create(new AdaptersReadinessOptions()));
        if (zenGardenProvider is not null)
        {
            services.AddSingleton(zenGardenProvider);
        }
        if (discoveryCoordinator is not null)
        {
            services.AddSingleton(discoveryCoordinator);
        }

        new MongoModule().Register(services);

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildLayeredDiscoveryProvider(bool activateZenGarden)
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IOptions<AdaptersReadinessOptions>>(Options.Create(new AdaptersReadinessOptions()));

        new MongoModule().Register(services);
        if (activateZenGarden)
        {
            services.AddSingleton<IZenGardenInitializationProvider>(new StubZenGardenProvider(_ =>
                new ZenGardenOfferingResolution
                {
                    ToolFqid = "mongodb",
                    Offering = "mongodb",
                    Hostname = "mongo-zen",
                    Port = 27019,
                    Uris = ["mongodb://mongo-zen:27019"]
                }));
            services.AddKoanZenGarden(configuration);
        }

        new ServiceDiscoveryAutoRegistrar().Initialize(services);
        services.AddSingleton<IServiceDiscoveryAdapter, LayerProbeDiscoveryAdapter>();

        return services.BuildServiceProvider();
    }

    private sealed class CapturingDiscoveryCoordinator(AdapterDiscoveryResult result)
        : IServiceDiscoveryCoordinator
    {
        public string? RequestedService { get; private set; }
        public DiscoveryContext? Context { get; private set; }

        public Task<AdapterDiscoveryResult> DiscoverService(
            string serviceName,
            DiscoveryContext? context = null,
            CancellationToken cancellationToken = default)
        {
            RequestedService = serviceName;
            Context = context;
            return Task.FromResult(result);
        }

        public IServiceDiscoveryAdapter[] GetRegisteredAdapters() => [];
    }

    private sealed class LayerProbeDiscoveryAdapter : IServiceDiscoveryAdapter
    {
        public string ServiceName => "mongo";
        public string[] Aliases => ["mongodb"];
        public int Priority => 100;

        public Task<AdapterDiscoveryResult> Discover(
            DiscoveryContext context,
            CancellationToken cancellationToken = default)
        {
            var candidate = context.ContributedCandidates?.FirstOrDefault();
            return Task.FromResult(candidate is null
                ? AdapterDiscoveryResult.Success(ServiceName, "mongodb://localhost:27017", "local-default")
                : AdapterDiscoveryResult.Success(ServiceName, candidate.Url, candidate.Method));
        }
    }

    private sealed class StubZenGardenProvider : IZenGardenInitializationProvider
    {
        private readonly Func<ZenGardenConnectionIntent, ZenGardenOfferingResolution?> _resolver;

        public StubZenGardenProvider(Func<ZenGardenConnectionIntent, ZenGardenOfferingResolution?> resolver)
        {
            _resolver = resolver;
        }

        public bool TryGetDefaultOffering(string adapterId, out string offering)
        {
            offering = "mongodb";
            return string.Equals(adapterId, "mongo", StringComparison.OrdinalIgnoreCase)
                || string.Equals(adapterId, "mongodb", StringComparison.OrdinalIgnoreCase);
        }

        public ValueTask<ZenGardenOfferingResolution?> Resolve(
            ZenGardenConnectionIntent intent,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_resolver(intent));
        }

        public ValueTask<ZenGardenCapabilityWishReceipt?> WishCapabilities(
            ZenGardenConnectionIntent intent,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<ZenGardenCapabilityWishReceipt?>(null);
        }
    }
}
