using System.Text.Json.Serialization;

namespace Koan.ZenGarden;

/// <summary>
/// Represents a discovered or learned Moss Stone with connection metadata.
/// Used for in-memory caching and persisted topology roster.
/// </summary>
internal sealed record CachedMossStone
{
    public required string Endpoint { get; init; }
    public string? StoneId { get; init; }
    public required string StoneName { get; init; }
    public string? MossVersion { get; init; }
    public string? LanternEndpoint { get; init; }
    public DateTimeOffset LastSeenUtc { get; init; }

    [JsonIgnore]
    public string CacheKey => string.IsNullOrWhiteSpace(StoneId) ? StoneName : StoneId!;
}
