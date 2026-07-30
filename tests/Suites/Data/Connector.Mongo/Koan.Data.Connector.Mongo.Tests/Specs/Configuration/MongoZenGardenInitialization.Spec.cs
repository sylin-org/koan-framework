using Koan.Core.Adapters;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Connector.Mongo.Initialization;
using Koan.Data.Connector.Mongo.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Configuration;

public sealed class MongoZenGardenInitializationSpec
{
    [Fact]
    public async Task Options_are_pure_and_first_use_resolves_auto_once()
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

        options.ConnectionString.Should().Be("auto");
        discovery.AutomaticCalls.Should().Be(0);

        var route = Route(options);
        var clients = provider.GetRequiredService<MongoClientManager>();
        var databases = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => clients.Database(route, CancellationToken.None)));

        discovery.AutomaticCalls.Should().Be(1);
        databases.Should().OnlyContain(database =>
            database.Client.Settings.Servers.Single().Host == "mongo-zen" &&
            database.Client.Settings.Servers.Single().Port == 27019);
        discovery.RequestedService.Should().Be("mongo");
        discovery.Context.Should().NotBeNull();
        discovery.Context!.RequireHealthValidation.Should().BeTrue();
        discovery.Context.Parameters.Should().ContainKey("database").WhoseValue.Should().Be("appdb");
    }

    [Fact]
    public async Task Explicit_zen_garden_uri_resolves_on_first_use()
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

        options.ConnectionString.Should().Be("zen-garden://mongodb:dev");
        discovery.RequiredCalls.Should().Be(0);

        var route = Route(options);
        var database = await provider.GetRequiredService<MongoClientManager>()
            .Database(route, CancellationToken.None);

        discovery.RequiredCalls.Should().Be(1);
        discovery.AutomaticCalls.Should().Be(0);
        discovery.RequestedIntent.Should().Be("zen-garden://mongodb:dev");
        database.Client.Settings.Servers.Should().ContainSingle(server =>
            server.Host == "mongo-dev" && server.Port == 27021);
    }

    [Fact]
    public async Task Unresolved_explicit_zen_garden_intent_fails_on_first_use_without_autonomous_fallback()
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

        var options = provider.GetRequiredService<IOptions<MongoOptions>>().Value;
        options.ConnectionString.Should().Be("zen-garden://mongodb");
        discovery.RequiredCalls.Should().Be(0);

        var route = Route(options);
        var resolve = () => provider.GetRequiredService<MongoClientManager>()
            .Database(route, CancellationToken.None);

        await resolve.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Mongo explicit Zen Garden intent*mongodb*could not be satisfied*")
            .WithMessage("*Koan.ZenGarden*ready 'mongodb' offering*'auto'*native MongoDB connection string*");
        discovery.RequiredCalls.Should().Be(1);
        discovery.AutomaticCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_cancelled_caller_does_not_poison_shared_route_resolution()
    {
        var completion = new TaskCompletionSource<AdapterDiscoveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var discovery = new CapturingDiscoveryCoordinator(automaticCompletion: completion);
        using var provider = BuildProvider(new Dictionary<string, string?>(), discovery);
        var route = Route(provider.GetRequiredService<IOptions<MongoOptions>>().Value);
        var clients = provider.GetRequiredService<MongoClientManager>();
        using var cancellation = new CancellationTokenSource();

        var abandoned = clients.Database(route, cancellation.Token);
        discovery.AutomaticCalls.Should().Be(1);
        cancellation.Cancel();
        Func<Task> waitForAbandonedCall = async () => await abandoned;
        await waitForAbandonedCall.Should().ThrowAsync<OperationCanceledException>();

        completion.SetResult(AdapterDiscoveryResult.Success(
            "mongo",
            "mongodb://mongo-zen:27019/appdb",
            "zengarden-offering"));
        var database = await clients.Database(route, CancellationToken.None);

        discovery.AutomaticCalls.Should().Be(1);
        database.Client.Settings.Servers.Should().ContainSingle(server =>
            server.Host == "mongo-zen" && server.Port == 27019);
    }

    private static MongoRoute Route(MongoOptions options) => new(
        "Default",
        options.ConnectionString,
        options.Database,
        StorageLifecycle.Managed,
        DataSourceAccess.ReadWrite);

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
        AdapterDiscoveryResult? required = null,
        TaskCompletionSource<AdapterDiscoveryResult>? automaticCompletion = null) : IServiceDiscoveryCoordinator
    {
        private int _automaticCalls;
        private int _requiredCalls;

        public int AutomaticCalls => Volatile.Read(ref _automaticCalls);
        public int RequiredCalls => Volatile.Read(ref _requiredCalls);
        public string? RequestedService { get; private set; }
        public string? RequestedIntent { get; private set; }
        public DiscoveryContext? Context { get; private set; }

        public Task<AdapterDiscoveryResult> DiscoverService(
            string serviceName,
            DiscoveryContext? context = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _automaticCalls);
            RequestedService = serviceName;
            Context = context;
            return automaticCompletion?.Task ?? Task.FromResult(
                automatic ?? AdapterDiscoveryResult.Failed(serviceName, "unexpected automatic discovery"));
        }

        public Task<AdapterDiscoveryResult> ResolveServiceIntent(
            string serviceName,
            string intent,
            DiscoveryContext? context = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _requiredCalls);
            RequestedService = serviceName;
            RequestedIntent = intent;
            Context = context;
            return Task.FromResult(required ?? AdapterDiscoveryResult.Failed(serviceName, "unexpected required discovery"));
        }

        public IServiceDiscoveryAdapter[] GetRegisteredAdapters() => [];
    }
}
