using Koan.Redis.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Koan.Redis.Connections;

internal sealed class RedisConnectionProvider : IRedisConnectionProvider, IDisposable
{
    private const int MaximumEndpoints = 128;
    private readonly Dictionary<string, Lazy<IConnectionMultiplexer>> _connections = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly ILogger<RedisConnectionProvider>? _logger;
    private int _defaultOwnedByContainer;
    private bool _disposed;

    public RedisConnectionProvider(IOptions<RedisOptions> options, ILogger<RedisConnectionProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        DefaultConnectionString = options.Value.ConnectionString?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(DefaultConnectionString) ||
            string.Equals(DefaultConnectionString, "auto", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The Redis backend did not resolve an endpoint. Configure ConnectionStrings:Redis or enable Redis discovery.");
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
        Lazy<IConnectionMultiplexer> connection;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_connections.TryGetValue(normalized, out connection!))
            {
                if (_connections.Count >= MaximumEndpoints)
                    throw new InvalidOperationException(
                        $"The Redis host already owns {MaximumEndpoints} distinct endpoints. " +
                        "Consolidate source routes or start a separate Koan host; endpoint pooling is intentionally bounded.");
                connection = new Lazy<IConnectionMultiplexer>(
                    () => RedisConnectionFactory.Connect(normalized, _logger),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                _connections.Add(normalized, connection);
            }
        }
        return connection.Value;
    }

    public void Dispose()
    {
        KeyValuePair<string, Lazy<IConnectionMultiplexer>>[] connections;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            connections = _connections.ToArray();
            _connections.Clear();
        }
        foreach (var entry in connections)
        {
            if (Volatile.Read(ref _defaultOwnedByContainer) == 1 &&
                string.Equals(entry.Key, DefaultConnectionString, StringComparison.Ordinal))
                continue;
            if (entry.Value.IsValueCreated) entry.Value.Value.Dispose();
        }
    }
}
