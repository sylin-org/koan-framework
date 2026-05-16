using System.Text.Json.Serialization;

namespace Koan.ZenGarden.Persistence;

/// <summary>
/// Deserialization model for Moss's TopologyEntry (Rust schema, snake_case).
/// Used by both HTTP active hydration and file-based topology seeding.
/// Single model, two consumers — same source struct, same schema evolution.
/// </summary>
internal sealed record MossTopologyEntry
{
    [JsonPropertyName("stone_id")]
    public string? StoneId { get; init; }

    [JsonPropertyName("stone_name")]
    public string? StoneName { get; init; }

    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    [JsonPropertyName("moss_version")]
    public string? MossVersion { get; init; }

    [JsonPropertyName("last_seen")]
    public DateTimeOffset? LastSeen { get; init; }

    [JsonPropertyName("health")]
    public string? Health { get; init; }
}
