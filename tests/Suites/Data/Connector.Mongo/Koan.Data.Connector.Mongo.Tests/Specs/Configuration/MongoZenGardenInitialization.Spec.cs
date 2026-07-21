using Koan.Core.Adapters;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Connector.Mongo.Initialization;
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
            automatic: AdapterDiscoveryResult.Success(
                "mongo",
                "mongodb://mongo-zen:27019/appdb",
                "zengarden-offering"));
        using var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["Koan:Data:Mongo:Database"] = "appdb"
            },
            discovery);

        var options = provider.GetRequiredService<IOptions<MongoOptions>>().Value;

        options.ConnectionString.Should().Be("mongodb://mongo-zen:27019/appdb");
        discovery.AutomaticCalls.Should().Be(1);
        discovery.RequestedService.Should().Be("mongo");
        discovery.Context.Should().NotBeNull();
        discovery.Context!.RequireHealthValidation.Should().BeTrue();
        discovery.Context.Parameters.Should().ContainKey("database").WhoseValue.Should().Be("appdb");
    }

    [Fact]
    public void Explicit_zen_garden_uri_uses_the_required_shared_pipeline()
    {
        var discovery = new CapturingDiscoveryCoordinator(
            required: AdapterDiscoveryResult.Success(
                "mongo",
                "mongodb://mongo-dev:27021/mydb",
                "zengarden-offering"));
        using var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["Koan:Data:Mongo:ConnectionString"] = "zen-garden://mongodb:dev",
                ["Koan:Data:Mongo:Database"] = "mydb"
            },
            discovery);

        var options = provider.GetRequiredService<IOptions<MongoOptions>>().Value;

        options.ConnectionString.Should().Be("mongodb://mongo-dev:27021/mydb");
        discovery.RequiredCalls.Should().Be(1);
        discovery.AutomaticCalls.Should().Be(0);
        discovery.RequestedIntent.Should().Be("zen-garden://mongodb:dev");
    }

    [Fact]
    public void Unresolved_explicit_zen_garden_intent_fails_correctively_without_autonomous_fallback()
    {
        var discovery = new CapturingDiscoveryCoordinator(
            required: AdapterDiscoveryResult.Failed(
                "mongo",
                "The active 'zen-garden' discovery source returned no candidates for the explicit intent."));
        using var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["Koan:Data:Mongo:ConnectionString"] = "zen-garden://mongodb",
                ["Koan:Data:Mongo:Database"] = "orders"
            },
            discovery);

        var resolve = () => provider.GetRequiredService<IOptions<MongoOptions>>().Value;

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage("*Mongo explicit Zen Garden intent*mongodb*could not be satisfied*")
            .WithMessage("*Koan.ZenGarden*ready 'mongodb' offering*'auto'*native MongoDB connection string*");
        discovery.RequiredCalls.Should().Be(1);
        discovery.AutomaticCalls.Should().Be(0);
    }

    private static ServiceProvider BuildProvider(
        IDictionary<string, string?> settings,
        IServiceDiscoveryCoordinator discoveryCoordinator)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IOptions<AdaptersReadinessOptions>>(
            Options.Create(new AdaptersReadinessOptions()));
        services.AddSingleton(discoveryCoordinator);
        new MongoModule().Register(services);
        return services.BuildServiceProvider();
    }

    private sealed class CapturingDiscoveryCoordinator(
        AdapterDiscoveryResult? automatic = null,
        AdapterDiscoveryResult? required = null) : IServiceDiscoveryCoordinator
    {
        public int AutomaticCalls { get; private set; }
        public int RequiredCalls { get; private set; }
        public string? RequestedService { get; private set; }
        public string? RequestedIntent { get; private set; }
        public DiscoveryContext? Context { get; private set; }

        public Task<AdapterDiscoveryResult> DiscoverService(
            string serviceName,
            DiscoveryContext? context = null,
            CancellationToken cancellationToken = default)
        {
            AutomaticCalls++;
            RequestedService = serviceName;
            Context = context;
            return Task.FromResult(automatic ?? AdapterDiscoveryResult.Failed(serviceName, "unexpected automatic discovery"));
        }

        public Task<AdapterDiscoveryResult> ResolveServiceIntent(
            string serviceName,
            string intent,
            DiscoveryContext? context = null,
            CancellationToken cancellationToken = default)
        {
            RequiredCalls++;
            RequestedService = serviceName;
            RequestedIntent = intent;
            Context = context;
            return Task.FromResult(required ?? AdapterDiscoveryResult.Failed(serviceName, "unexpected required discovery"));
        }

        public IServiceDiscoveryAdapter[] GetRegisteredAdapters() => [];
    }
}
