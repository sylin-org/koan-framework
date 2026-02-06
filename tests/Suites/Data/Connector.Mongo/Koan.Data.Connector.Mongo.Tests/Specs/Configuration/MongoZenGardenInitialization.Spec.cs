using Koan.Core.Adapters;
using Koan.Data.Connector.Mongo.Initialization;
using Koan.ZenGarden.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Configuration;

public sealed class MongoZenGardenInitializationSpec
{
    [Fact]
    public void Auto_connection_prefers_zen_garden_resolution()
    {
        using var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["Koan:Data:Mongo:Database"] = "appdb"
            },
            new StubZenGardenProvider(intent =>
            {
                intent.Offering.Should().Be("mongodb");
                return new ZenGardenOfferingResolution
                {
                    ToolFqid = "offering:mongodb",
                    Offering = "mongodb",
                    Hostname = "mongo-zen",
                    Port = 27019
                };
            }));

        var options = provider.GetRequiredService<IOptions<MongoOptions>>().Value;

        options.ConnectionString.Should().Be("mongodb://mongo-zen:27019/appdb");
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
                ToolFqid = "offering:mongodb:dev",
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
        IZenGardenInitializationProvider zenGardenProvider)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IOptions<AdaptersReadinessOptions>>(Options.Create(new AdaptersReadinessOptions()));
        services.AddSingleton(zenGardenProvider);

        new KoanAutoRegistrar().Initialize(services);

        return services.BuildServiceProvider();
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

        public ValueTask<ZenGardenOfferingResolution?> ResolveAsync(
            ZenGardenConnectionIntent intent,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_resolver(intent));
        }
    }
}
