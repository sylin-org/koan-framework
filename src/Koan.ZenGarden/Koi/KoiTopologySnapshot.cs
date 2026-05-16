namespace Koan.ZenGarden.Koi;

/// <summary>
/// Immutable point-in-time view of the topology as observed by the Koi handler.
/// Published atomically via reference swap; safe to read without locks.
/// </summary>
public sealed record KoiTopologySnapshot
{
    public static readonly KoiTopologySnapshot Empty = new()
    {
        State = KoiHandlerState.Initializing,
        Stones = [],
        Lanterns = []
    };

    public required KoiHandlerState State { get; init; }
    public required IReadOnlyList<DiscoveredStone> Stones { get; init; }
    public required IReadOnlyList<DiscoveredLantern> Lanterns { get; init; }
    public DateTimeOffset? LastUpdate { get; init; }
    public DateTimeOffset? KoiDetectedAt { get; init; }
    public string? KoiVersion { get; init; }
}
