using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Web.AdapterSurface.TestKit.Containers;

/// <summary>
/// Bootstraps a Mongo backend for adapter-surface tests. Honours, in order:
///   1. Koan_TESTS_MONGO env var
///   2. mongodb://localhost:27017 if reachable
///   3. Testcontainers-provisioned mongo:7
/// </summary>
public sealed class MongoContainerHelper : IAsyncDisposable
{
    private const int MongoPort = 27017;
    private TestcontainersContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string? ConnectionString { get; private set; }
    public string Database { get; } = $"koan_surface_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");

        var explicitConn = Environment.GetEnvironmentVariable("Koan_TESTS_MONGO")
                          ?? Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(explicitConn) && await CanPing(explicitConn).ConfigureAwait(false))
        {
            ConnectionString = EnsureDatabase(explicitConn, Database);
            IsAvailable = true;
            return;
        }

        if (await CanPing("mongodb://localhost:27017").ConfigureAwait(false))
        {
            ConnectionString = EnsureDatabase("mongodb://localhost:27017", Database);
            IsAvailable = true;
            return;
        }

        try
        {
            _container = new TestcontainersBuilder<TestcontainersContainer>()
                .WithImage("mongo:7")
                .WithCleanUp(true)
                .WithPortBinding(MongoPort, assignRandomHostPort: true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(MongoPort))
                .Build();

            await _container.StartAsync().ConfigureAwait(false);
            var mappedPort = _container.GetMappedPublicPort(MongoPort);
            var connection = EnsureDatabase($"mongodb://localhost:{mappedPort}", Database);
            if (!await CanPing(connection).ConfigureAwait(false))
            {
                UnavailableReason = "Mongo container started but did not respond to ping.";
                return;
            }
            ConnectionString = connection;
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start Mongo container: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task ResetAsync()
    {
        if (ConnectionString is null) return;
        var client = new MongoClient(ConnectionString);
        await client.DropDatabaseAsync(Database).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (ConnectionString is not null)
            {
                var client = new MongoClient(ConnectionString);
                await client.DropDatabaseAsync(Database).ConfigureAwait(false);
            }
        }
        catch { /* best effort */ }

        if (_container is not null)
        {
            try { await _container.StopAsync().ConfigureAwait(false); } catch { }
            try { await _container.DisposeAsync().ConfigureAwait(false); } catch { }
        }
    }

    private static string EnsureDatabase(string connectionString, string database)
    {
        var builder = new MongoUrlBuilder(connectionString) { DatabaseName = database };
        return builder.ToString();
    }

    private static async Task<bool> CanPing(string connectionString)
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
            var result = await database.RunCommandAsync(
                (Command<BsonDocument>)new BsonDocument("ping", 1)).ConfigureAwait(false);
            return result is not null;
        }
        catch
        {
            return false;
        }
    }
}
