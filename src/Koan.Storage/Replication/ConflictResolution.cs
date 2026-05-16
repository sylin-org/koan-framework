namespace Koan.Storage.Replication;

/// <summary>
/// Policy applied when a sync push detects a divergent remote version.
/// </summary>
public enum ConflictResolutionPolicy
{
    /// <summary>Overwrite remote with local version. Simple, deterministic.</summary>
    LastWriterWins,

    /// <summary>Discard local, pull remote version. Remote is authoritative.</summary>
    KeepRemote,

    /// <summary>Emit ConflictDetected event and let the application decide.</summary>
    Callback
}
