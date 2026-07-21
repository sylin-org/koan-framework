namespace Koan.Redis.Options;

/// <summary>Configures the shared Redis backend used by all Redis-backed Koan capabilities.</summary>
public sealed class RedisOptions
{
    /// <summary>Gets or sets the StackExchange.Redis connection string, or <c>auto</c> for discovery.</summary>
    public string ConnectionString { get; set; } = "auto";

    /// <summary>Gets or sets whether autonomous endpoint discovery is disabled.</summary>
    public bool DisableAutoDetection { get; set; }
}
