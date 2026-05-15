using System.ComponentModel.DataAnnotations;

namespace Koan.Cache.Adapter.Redis.Options;

/// <summary>
/// Storage-side options for the Redis cache adapter. Coherence pub/sub settings live in
/// <see cref="RedisCoherenceChannelOptions"/> and are consumed by <c>RedisCoherenceChannel</c>.
/// </summary>
public sealed class RedisCacheAdapterOptions
{
    [Required]
    public string Configuration { get; set; } = "localhost:6379";

    public string? InstanceName { get; set; }

    public string KeyPrefix { get; set; } = "cache:";

    public string TagPrefix { get; set; } = "cache:tag:";

    public int Database { get; set; } = -1;

    [Range(0, int.MaxValue)]
    public int TagIndexCapacity { get; set; } = 8192;
}
