using System.ComponentModel.DataAnnotations;
using Koan.Cache.Adapter.Redis.Infrastructure;

namespace Koan.Cache.Adapter.Redis.Options;

/// <summary>
/// Storage-side options for the Redis cache adapter. Peer-broadcast settings live in
/// <see cref="RedisCacheBroadcastOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>No <c>Configuration</c> property:</b> per ARCH-0080, the connection string is owned by
/// <c>Koan.Data.Connector.Redis</c> (read from <c>Koan:Data:Redis:ConnectionString</c>). This
/// adapter consumes <c>IConnectionMultiplexer</c> via DI and only exposes cache-specific
/// knobs (key/tag prefixes, optional Redis database selector, tag-index capacity).
/// </para>
/// </remarks>
public sealed class RedisCacheAdapterOptions
{
    public string? InstanceName { get; set; }

    public string KeyPrefix { get; set; } = Constants.DefaultKeyPrefix;

    public string TagPrefix { get; set; } = Constants.DefaultTagPrefix;

    public int Database { get; set; } = -1;

    [Range(0, int.MaxValue)]
    public int TagIndexCapacity { get; set; } = 8192;
}
