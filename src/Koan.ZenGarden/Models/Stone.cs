namespace Koan.ZenGarden.Models;

/// <summary>
/// Represents a Zen Garden Stone (physical device running Moss).
/// </summary>
public sealed record Stone
{
    /// <summary>Immutable GUID v7 - use as cache key.</summary>
    [JsonPropertyName("stone_id")]
    public string? StoneId { get; init; }
    
    /// <summary>Human-readable hostname (may change).</summary>
    [JsonPropertyName("stone_name")]
    public required string StoneName { get; init; }
    
    /// <summary>Full HTTP endpoint URL (e.g., http://192.168.1.100:7185).</summary>
    [JsonPropertyName("stone_endpoint")]
    public required string StoneEndpoint { get; init; }
    
    /// <summary>Moss daemon version.</summary>
    [JsonPropertyName("moss_version")]
    public string? MossVersion { get; init; }
    
    /// <summary>Lantern registry URL (for cross-subnet discovery).</summary>
    [JsonPropertyName("lantern_endpoint")]
    public string? LanternEndpoint { get; init; }
    
    /// <summary>Extract host from endpoint.</summary>
    [JsonIgnore]
    public string Host => new Uri(StoneEndpoint).Host;
    
    /// <summary>Extract port from endpoint.</summary>
    [JsonIgnore]
    public int Port => new Uri(StoneEndpoint).Port;
    
    /// <summary>Cache key - prefers StoneId, falls back to StoneName.</summary>
    [JsonIgnore]
    public string CacheKey => StoneId ?? StoneName;
}

/// <summary>
/// Service running on a Stone (container managed by Moss).
/// </summary>
public sealed record ServiceInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }
    
    [JsonPropertyName("offering")]
    public required string Offering { get; init; }
    
    [JsonPropertyName("version")]
    public string? Version { get; init; }
    
    [JsonPropertyName("status")]
    public required string Status { get; init; }
    
    [JsonPropertyName("health")]
    public string? Health { get; init; }
    
    [JsonPropertyName("category")]
    public string? Category { get; init; }
    
    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }
    
    [JsonPropertyName("connection")]
    public ServiceConnection? Connection { get; init; }
    
    [JsonPropertyName("ports")]
    public ServicePorts? Ports { get; init; }
    
    /// <summary>Stone that hosts this service (from Garden-wide search).</summary>
    [JsonPropertyName("stone")]
    public ServiceStoneRef? Stone { get; init; }
    
    /// <summary>Check if service is running.</summary>
    [JsonIgnore]
    public bool IsRunning => Status.Equals("Running", StringComparison.OrdinalIgnoreCase);
    
    /// <summary>Check if service is healthy.</summary>
    [JsonIgnore]
    public bool IsHealthy => Health?.Equals("Healthy", StringComparison.OrdinalIgnoreCase) ?? false;
}

/// <summary>
/// Reference to a Stone in service search results.
/// </summary>
public sealed record ServiceStoneRef
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }
    
    [JsonPropertyName("name")]
    public string? Name { get; init; }
    
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }
}

/// <summary>
/// Connection information provided by Zen Garden.
/// </summary>
public sealed record ServiceConnection
{
    [JsonPropertyName("hostname")]
    public required string Hostname { get; init; }
    
    [JsonPropertyName("ip")]
    public required string Ip { get; init; }
    
    [JsonPropertyName("port")]
    public required int Port { get; init; }
    
    [JsonPropertyName("protocol")]
    public required string Protocol { get; init; }
    
    /// <summary>Pre-built connection URIs from Moss (tcp://host:port format).</summary>
    [JsonPropertyName("uris")]
    public required string[] Uris { get; init; }
    
    /// <summary>Primary URI (hostname-based).</summary>
    [JsonIgnore]
    public string PrimaryUri => Uris.FirstOrDefault() ?? $"{Protocol}://{Hostname}:{Port}";
    
    /// <summary>
    /// Get URI with a service-specific scheme.
    /// Replaces generic tcp:// with mongodb://, redis://, etc.
    /// </summary>
    public string GetUri(string scheme)
    {
        var uri = PrimaryUri;
        if (uri.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
            return $"{scheme}://{uri[6..]}";
        if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return $"{scheme}://{uri[7..]}";
        return uri;
    }
}

/// <summary>
/// Port information for a service.
/// </summary>
public sealed record ServicePorts
{
    [JsonPropertyName("native")]
    public int? Native { get; init; }
    
    [JsonPropertyName("agnostic")]
    public int? Agnostic { get; init; }
}

/// <summary>
/// Resolved service with connection string ready to use.
/// </summary>
public sealed record ResolvedService
{
    public required ServiceInfo Service { get; init; }
    public required Stone Stone { get; init; }
    public required string ConnectionString { get; init; }
    
    /// <summary>Timestamp when this was cached.</summary>
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;
}
