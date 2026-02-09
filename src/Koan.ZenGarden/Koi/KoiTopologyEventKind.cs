namespace Koan.ZenGarden.Koi;

/// <summary>
/// Classifies a topology change detected by the Koi handler.
/// </summary>
public enum KoiTopologyEventKind
{
    /// <summary>A new Moss Stone appeared on the network (mDNS resolved).</summary>
    StoneOnline,

    /// <summary>A Moss Stone was removed from the network (mDNS goodbye or lease expiry).</summary>
    StoneOffline,

    /// <summary>A known Stone's metadata changed (TXT record update).</summary>
    StoneChanged,

    /// <summary>Handler reconnected after a gap; snapshot is the reconciled full topology.</summary>
    TopologyReset,

    /// <summary>A Lantern endpoint appeared on the network.</summary>
    LanternFound,

    /// <summary>A Lantern endpoint was removed from the network.</summary>
    LanternLost,

    /// <summary>Handler successfully connected to Koi.</summary>
    KoiAvailable,

    /// <summary>Handler lost its connection to Koi.</summary>
    KoiLost
}
