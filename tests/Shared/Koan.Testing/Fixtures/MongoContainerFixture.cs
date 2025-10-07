using System.Linq;
using DotNet.Testcontainers;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Koan.Testing.Contracts;
using Koan.Testing.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Testing.Fixtures;

public sealed class MongoContainerFixture : IAsyncDisposable, IInitializableFixture
{
    private const string DefaultDatabase = "Koan";
    private const string DockerFixtureDefaultKey = "docker";
    private const string RyukVariable = "TESTCONTAINERS_RYUK_DISABLED";
    private const int MongoPort = 27017;

    private TestcontainersContainer? _container;

    public MongoContainerFixture(string dockerFixtureKey = DockerFixtureDefaultKey, string database = DefaultDatabase)
    {
        DockerFixtureKey = dockerFixtureKey;
        Database = database;
    }

    public string DockerFixtureKey { get; }

    public string Database { get; }

    public bool IsAvailable { get; private set; }

    public string? ConnectionString { get; private set; }

    public string? UnavailableReason { get; private set; }

    public async ValueTask InitializeAsync(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsAvailable)
        {
            return;
        }

        context.Diagnostics.Info("mongo.fixture.initialize", new { dockerKey = DockerFixtureKey, database = Database });

        if (TryGetExplicitConnectionString(out var explicitConnection) && await CanPingAsync(explicitConnection!, context.Cancellation).ConfigureAwait(false))
        {
            ConnectionString = EnsureDatabase(explicitConnection!, Database);
            IsAvailable = true;
            context.Diagnostics.Info("mongo.fixture.explicit", new { source = "env" });
            return;
        }

        var localhost = "mongodb://localhost:27017";
        if (await CanPingAsync(localhost, context.Cancellation).ConfigureAwait(false))
        {
            ConnectionString = EnsureDatabase(localhost, Database);
            IsAvailable = true;
            context.Diagnostics.Info("mongo.fixture.local", new { host = "localhost", port = MongoPort });
            return;
        }

        if (!context.TryGetItem(DockerFixtureKey, out DockerDaemonFixture? dockerFixture))
        {
            UnavailableReason = $"Docker fixture '{DockerFixtureKey}' is not registered. Call UsingDocker() before UsingMongoContainer().";
            context.Diagnostics.Warn("mongo.fixture.docker.missing", new { DockerFixtureKey, reason = UnavailableReason });
            return;
        }

        if (!dockerFixture!.IsAvailable)
        {
            UnavailableReason = dockerFixture.UnavailableReason;
            context.Diagnostics.Warn("mongo.fixture.docker.unavailable", new { DockerFixtureKey, reason = UnavailableReason });
            return;
        }

        Environment.SetEnvironmentVariable(RyukVariable, "true");
        context.Diagnostics.Debug("mongo.fixture.ryuk.disabled", new { variable = RyukVariable });

        var endpoint = dockerFixture.Endpoint;
        var builder = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage("mongo:7")
            .WithCleanUp(true)
            .WithPortBinding(MongoPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(MongoPort));

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            builder = builder.WithDockerEndpoint(endpoint);
        }

        _container = builder.Build();
        context.Diagnostics.Info("mongo.fixture.container.create", new { image = "mongo:7", endpoint });

        try
        {
            await _container.StartAsync(context.Cancellation).ConfigureAwait(false);
            var mappedPort = _container.GetMappedPublicPort(MongoPort);
            var connection = EnsureDatabase($"mongodb://localhost:{mappedPort}", Database);

            if (!await CanPingAsync(connection, context.Cancellation).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Unable to ping Mongo container.");
            }

            ConnectionString = connection;
            IsAvailable = true;
            UnavailableReason = null;
            context.Diagnostics.Info("mongo.fixture.container.started", new { host = "localhost", port = mappedPort });
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start Mongo container: {ex.GetType().Name}: {ex.Message}";
            context.Diagnostics.Error("mongo.fixture.container.failed", new { message = ex.Message }, ex);
            await DisposeContainerSilentlyAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeContainerSilentlyAsync().ConfigureAwait(false);
    }

    private static bool TryGetExplicitConnectionString(out string? connectionString)
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("Koan_TESTS_MONGO"),
            Environment.GetEnvironmentVariable("Koan_MONGO__CONNECTION_STRING"),
            Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING"),
            Environment.GetEnvironmentVariable("Mongo__ConnectionString")
        };

        connectionString = candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return !string.IsNullOrWhiteSpace(connectionString);
    }

    private static string EnsureDatabase(string connectionString, string database)
    {
        var builder = new MongoUrlBuilder(connectionString)
        {
            DatabaseName = string.IsNullOrWhiteSpace(database)
                ? DefaultDatabase
                : database
        };

        return builder.ToString();
    }

    private static async Task<bool> CanPingAsync(string connectionString, CancellationToken cancellation = default)
    {
        try
        {
            var url = new MongoUrl(connectionString);
            var settings = MongoClientSettings.FromUrl(url);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(3);
            settings.ConnectTimeout = TimeSpan.FromSeconds(3);
            settings.SocketTimeout = TimeSpan.FromSeconds(3);

            var client = new MongoClient(settings);
            var dbName = url.DatabaseName ?? "admin";
            var database = client.GetDatabase(dbName);
            var result = await database.RunCommandAsync((Command<BsonDocument>)new BsonDocument("ping", 1), cancellationToken: cancellation).ConfigureAwait(false);
            return result is not null;
        }
        catch
        {
            return false;
        }
    }

    private async ValueTask DisposeContainerSilentlyAsync()
    {
        if (_container is null)
        {
            return;
        }

        try
        {
            await _container.StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }

        try
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }

        _container = null;
    }
}
