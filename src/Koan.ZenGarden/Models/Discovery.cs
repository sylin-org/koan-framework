namespace Koan.ZenGarden.Models;

/// <summary>
/// UDP discovery request wrapped in announcement envelope.
/// </summary>
public sealed record DiscoveryRequest
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "discovery_request";
    
    [JsonPropertyName("data")]
    public required DiscoveryRequestData Data { get; init; }
}

public sealed record DiscoveryRequestData
{
    [JsonPropertyName("discover")]
    public string Discover { get; init; } = "moss";
    
    [JsonPropertyName("request_id")]
    public required string RequestId { get; init; }
    
    [JsonPropertyName("requester")]
    public string Requester { get; init; } = "koan-framework";
}

/// <summary>
/// UDP discovery response wrapped in announcement envelope.
/// </summary>
public sealed record DiscoveryResponse
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }
    
    [JsonPropertyName("data")]
    public DiscoveryResponseData? Data { get; init; }
}

public sealed record DiscoveryResponseData
{
    [JsonPropertyName("stone_id")]
    public string? StoneId { get; init; }
    
    [JsonPropertyName("stone_name")]
    public required string StoneName { get; init; }
    
    [JsonPropertyName("stone_endpoint")]
    public required string StoneEndpoint { get; init; }
    
    [JsonPropertyName("moss_version")]
    public string? MossVersion { get; init; }
    
    [JsonPropertyName("lantern_endpoint")]
    public string? LanternEndpoint { get; init; }
    
    /// <summary>Convert to Stone model.</summary>
    public Stone ToStone() => new()
    {
        StoneId = StoneId,
        StoneName = StoneName,
        StoneEndpoint = StoneEndpoint,
        MossVersion = MossVersion,
        LanternEndpoint = LanternEndpoint
    };
}
