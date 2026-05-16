namespace Koan.ZenGarden.Koi;

/// <summary>
/// A topology change event emitted by the Koi handler.
/// </summary>
public sealed record KoiTopologyEvent
{
    public required KoiTopologyEventKind Kind { get; init; }

    /// <summary>The Stone involved (for <c>StoneOnline</c>, <c>StoneOffline</c>, <c>StoneChanged</c>).</summary>
    public DiscoveredStone? Stone { get; init; }

    /// <summary>Previous state of the Stone (for <c>StoneChanged</c>).</summary>
    public DiscoveredStone? Previous { get; init; }

    /// <summary>The Lantern involved (for <c>LanternFound</c>, <c>LanternLost</c>).</summary>
    public DiscoveredLantern? Lantern { get; init; }

    /// <summary>Full topology snapshot at the time of this event.</summary>
    public required KoiTopologySnapshot Snapshot { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
