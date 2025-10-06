namespace Koan.Service.Inbox.Connector.Redis.Options;

public sealed class RedisInboxOptions
{
    /// <summary>
    /// Connection string or redis:// URI used to reach the backing Redis instance. "auto" delegates to discovery.
    /// </summary>
    public string? ConnectionString { get; set; } = "auto";

    /// <summary>
    /// Key prefix applied to inbox entries for namespacing.
    /// </summary>
    public string KeyPrefix { get; set; } = Infrastructure.Constants.Defaults.KeyPrefix;

    /// <summary>
    /// Duration a processed key remains stored.
    /// </summary>
    public TimeSpan ProcessingTtl { get; set; } = Infrastructure.Constants.Defaults.ProcessingTtl;

    /// <summary>
    /// Whether to delegate to Koan service discovery when resolving Redis endpoints.
    /// </summary>
    public bool EnableDiscovery { get; set; } = true;
}