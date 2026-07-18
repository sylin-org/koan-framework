using System.ComponentModel.DataAnnotations;
using Koan.Cache.Adapter.Redis.Infrastructure;

namespace Koan.Cache.Adapter.Redis.Options;

/// <summary>
/// Storage-side options for the Redis cache adapter. Peer-broadcast settings live in
/// <see cref="RedisCacheBroadcastOptions"/>.
/// </summary>
/// <remarks>The shared endpoint belongs to <c>Koan.Redis</c>; these options express only Cache semantics.</remarks>
public sealed class RedisCacheAdapterOptions
{
    public string? InstanceName { get; set; }

    public string KeyPrefix { get; set; } = Constants.DefaultKeyPrefix;

    public string TagPrefix { get; set; } = Constants.DefaultTagPrefix;

    public int Database { get; set; } = -1;

    [Range(0, int.MaxValue)]
    public int TagIndexCapacity { get; set; } = 8192;
}
