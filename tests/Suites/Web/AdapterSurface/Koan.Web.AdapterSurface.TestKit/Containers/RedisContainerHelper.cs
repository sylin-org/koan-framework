using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using StackExchange.Redis;

namespace Koan.Web.AdapterSurface.TestKit.Containers;

public sealed class RedisContainerHelper : IAsyncDisposable
{
    private const int RedisPort = 6379;
    private TestcontainersContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string? ConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");

        var explicitConn = Environment.GetEnvironmentVariable("Koan_TESTS_REDIS")
                          ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(explicitConn) && await CanPing(explicitConn).ConfigureAwait(false))
        {
            ConnectionString = explicitConn;
            IsAvailable = true;
            return;
        }

        try
        {
            _container = new TestcontainersBuilder<TestcontainersContainer>()
                .WithImage("redis:7-alpine")
                .WithCleanUp(true)
                .WithPortBinding(RedisPort, assignRandomHostPort: true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(RedisPort))
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
            var mappedPort = _container.GetMappedPublicPort(RedisPort);
            var connection = $"localhost:{mappedPort}";

            for (var attempt = 0; attempt < 30; attempt++)
            {
                if (await CanPing(connection).ConfigureAwait(false)) { ConnectionString = connection; IsAvailable = true; return; }
                await Task.Delay(500).ConfigureAwait(false);
            }
            UnavailableReason = "Redis container did not respond.";
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start Redis: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task ResetAsync()
    {
        if (ConnectionString is null) return;
        try
        {
            var muxer = await ConnectionMultiplexer.ConnectAsync($"{ConnectionString},allowAdmin=true").ConfigureAwait(false);
            var endpoints = muxer.GetEndPoints();
            foreach (var ep in endpoints)
            {
                var server = muxer.GetServer(ep);
                await server.FlushDatabaseAsync().ConfigureAwait(false);
            }
            await muxer.DisposeAsync().ConfigureAwait(false);
        }
        catch { /* best effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            try { await _container.StopAsync().ConfigureAwait(false); } catch { }
            try { await _container.DisposeAsync().ConfigureAwait(false); } catch { }
        }
    }

    private static async Task<bool> CanPing(string connectionString)
    {
        try
        {
            var muxer = await ConnectionMultiplexer.ConnectAsync(connectionString).ConfigureAwait(false);
            var db = muxer.GetDatabase();
            await db.PingAsync().ConfigureAwait(false);
            await muxer.DisposeAsync().ConfigureAwait(false);
            return true;
        }
        catch { return false; }
    }
}
