using StackExchange.Redis;

namespace Koan.Redis;

/// <summary>
/// Provides host-owned Redis connections, sharing one multiplexer for each distinct connection string.
/// </summary>
/// <remarks>
/// Application code normally consumes <see cref="IConnectionMultiplexer"/> directly. This contract exists for
/// Koan modules, such as source-aware Data adapters, that can address more than the default Redis endpoint.
/// </remarks>
public interface IRedisConnectionProvider
{
    /// <summary>Gets the fully resolved connection string for the host's default Redis endpoint.</summary>
    /// <remarks>The value can contain credentials and must not be logged without redaction.</remarks>
    string DefaultConnectionString { get; }

    /// <summary>Gets the host-owned connection for the default Redis endpoint.</summary>
    IConnectionMultiplexer GetDefault();

    /// <summary>Gets the host-owned connection for an explicit endpoint.</summary>
    /// <param name="connectionString">A StackExchange.Redis connection string.</param>
    IConnectionMultiplexer GetConnection(string connectionString);
}
