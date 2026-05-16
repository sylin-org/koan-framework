namespace Koan.ZenGarden.Koi;

/// <summary>
/// A Moss Stone discovered via Koi's mDNS-to-HTTP bridge.
/// Immutable value object — mapped from a Koi SSE <c>resolved</c> event.
/// </summary>
public sealed record DiscoveredStone
{
    /// <summary>mDNS instance name or TXT <c>stone_name</c>.</summary>
    public required string StoneName { get; init; }

    /// <summary>TXT <c>stone_id</c> (GUID), if advertised.</summary>
    public string? StoneId { get; init; }

    /// <summary>Constructed endpoint: <c>http://{ip}:{port}</c>.</summary>
    public required string Endpoint { get; init; }

    /// <summary>mDNS hostname endpoint, e.g. <c>http://moss-01.local:7185</c>.</summary>
    public string? LocalEndpoint { get; init; }

    /// <summary>TXT <c>version</c>.</summary>
    public string? MossVersion { get; init; }

    /// <summary>TXT <c>health</c>.</summary>
    public string? Health { get; init; }

    /// <summary>TXT <c>mac</c>.</summary>
    public string? Mac { get; init; }

    /// <summary>When this Stone was first reported by Koi in the current session.</summary>
    public DateTimeOffset DiscoveredAt { get; init; }

    /// <summary>Cache key for dedup: prefers <see cref="StoneId"/> over <see cref="StoneName"/>.</summary>
    internal string CacheKey => string.IsNullOrWhiteSpace(StoneId) ? StoneName : StoneId!;

    /// <summary>
    /// Whether two stones represent the same topology-significant state.
    /// Ignores <see cref="DiscoveredAt"/> which changes on every observation.
    /// </summary>
    internal bool TopologyEquals(DiscoveredStone? other) =>
        other is not null &&
        string.Equals(StoneName, other.StoneName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(StoneId, other.StoneId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Endpoint, other.Endpoint, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(MossVersion, other.MossVersion, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Health, other.Health, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Mac, other.Mac, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Converts to <see cref="CachedMossStone"/> for integration with the existing stone cache.
    /// </summary>
    internal CachedMossStone ToCachedMossStone() => new()
    {
        Endpoint = Endpoint,
        StoneId = StoneId,
        StoneName = StoneName,
        MossVersion = MossVersion,
        LastSeenUtc = DiscoveredAt
    };
}
