namespace Koan.ZenGarden.Models;

/// <summary>
/// Wrapper for Moss API responses.
/// </summary>
/// <typeparam name="T">The data type.</typeparam>
internal sealed record ApiResponse<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; init; }
    
    [JsonPropertyName("suggestions")]
    public string[]? Suggestions { get; init; }
}

/// <summary>
/// Response from GET /api/v1/services endpoint.
/// </summary>
internal sealed record ServicesListResponse
{
    [JsonPropertyName("data")]
    public ServicesListData? Data { get; init; }
}

internal sealed record ServicesListData
{
    [JsonPropertyName("found")]
    public bool Found { get; init; }
    
    [JsonPropertyName("services")]
    public ServiceInfo[]? Services { get; init; }
    
    [JsonPropertyName("source")]
    public string? Source { get; init; }
    
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }
}

/// <summary>
/// Health check response from GET /health.
/// </summary>
public sealed record HealthResponse
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }
    
    [JsonPropertyName("version")]
    public string? Version { get; init; }
    
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }
    
    [JsonIgnore]
    public bool IsHealthy => Status.Equals("healthy", StringComparison.OrdinalIgnoreCase);
}
