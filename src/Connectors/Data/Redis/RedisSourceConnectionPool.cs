using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Koan.Data.Connector.Redis;

/// <summary>Shares one Redis connection pool per distinct named-source endpoint for the host lifetime.</summary>
internal sealed class RedisSourceConnectionPool(ILogger<RedisSourceConnectionPool>? logger = null) : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<IConnectionMultiplexer>> _connections =
        new(StringComparer.Ordinal);

    public IConnectionMultiplexer Get(string connectionString) =>
        _connections.GetOrAdd(
            connectionString,
            key => new Lazy<IConnectionMultiplexer>(
                () => RedisConnectionFactory.Connect(key, logger),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    public void Dispose()
    {
        foreach (var connection in _connections.Values)
        {
            if (connection.IsValueCreated)
            {
                connection.Value.Dispose();
            }
        }

        _connections.Clear();
    }
}
