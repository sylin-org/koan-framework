using System.Collections.Concurrent;
using Koan.Redis.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Koan.Redis.Connections;

internal sealed class RedisConnectionProvider : IRedisConnectionProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<IConnectionMultiplexer>> _connections =
        new(StringComparer.Ordinal);
    private readonly ILogger<RedisConnectionProvider>? _logger;
    private int _defaultOwnedByContainer;

    public RedisConnectionProvider(IOptions<RedisOptions> options, ILogger<RedisConnectionProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        DefaultConnectionString = options.Value.ConnectionString?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(DefaultConnectionString) ||
            string.Equals(DefaultConnectionString, "auto", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The Redis backend did not resolve an endpoint. Configure ConnectionStrings:Redis or enable Redis discovery.");
        }

        _logger = logger;
    }

    public string DefaultConnectionString { get; }

    public IConnectionMultiplexer GetDefault() => GetConnection(DefaultConnectionString);

    internal IConnectionMultiplexer GetDefaultForContainer()
    {
        var connection = GetDefault();
        Volatile.Write(ref _defaultOwnedByContainer, 1);
        return connection;
    }

    public IConnectionMultiplexer GetConnection(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var normalized = connectionString.Trim();
        return _connections.GetOrAdd(
            normalized,
            key => new Lazy<IConnectionMultiplexer>(
                () => RedisConnectionFactory.Connect(key, _logger),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public void Dispose()
    {
        foreach (var entry in _connections)
        {
            if (Volatile.Read(ref _defaultOwnedByContainer) == 1 &&
                string.Equals(entry.Key, DefaultConnectionString, StringComparison.Ordinal))
            {
                continue;
            }

            if (entry.Value.IsValueCreated)
                entry.Value.Value.Dispose();
        }

        _connections.Clear();
    }
}
