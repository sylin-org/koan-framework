namespace Koan.ZenGarden.Koi;

/// <summary>
/// A Lantern endpoint discovered via Koi's mDNS-to-HTTP bridge.
/// Lanterns provide cross-subnet garden topology.
/// </summary>
internal sealed record DiscoveredLantern
{
    /// <summary>mDNS instance name.</summary>
    public required string Name { get; init; }

    /// <summary>Constructed endpoint: <c>http://{ip}:{port}</c>.</summary>
    public required string Endpoint { get; init; }

    /// <summary>mDNS hostname endpoint, e.g. <c>http://lantern.local:7186</c>.</summary>
    public string? LocalEndpoint { get; init; }

    /// <summary>When this Lantern was first reported by Koi.</summary>
    public DateTimeOffset DiscoveredAt { get; init; }
}
