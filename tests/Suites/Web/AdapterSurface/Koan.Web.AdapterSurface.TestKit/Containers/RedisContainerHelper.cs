using StackExchange.Redis;
using Testcontainers.Redis;

namespace Koan.Web.AdapterSurface.TestKit.Containers;

public sealed class RedisContainerHelper : IAsyncDisposable
{
    private RedisContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string? ConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
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
            _container = new RedisBuilder("redis:8.8.0-alpine")
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
            var connection = _container.GetConnectionString();
            for (var attempt = 0; attempt < 30; attempt++)
            {
                if (await CanPing(connection).ConfigureAwait(false))
                {
                    ConnectionString = connection;
                    IsAvailable = true;
                    return;
                }
                await Task.Delay(500).ConfigureAwait(false);
            }
            UnavailableReason = "Redis container did not respond after 15s.";
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
