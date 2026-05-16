namespace Koan.Storage.Replication;

/// <summary>
/// Types of events emitted by <see cref="ReplicatedStorageProvider"/> during replication lifecycle.
/// </summary>
public enum StorageEventType
{
    /// <summary>File successfully synced from cache to durable.</summary>
    FilePushed,

    /// <summary>File fetched from durable on cache miss (pull-through).</summary>
    FilePulled,

    /// <summary>File removed from local cache after confirmed durable sync.</summary>
    FileEvicted,

    /// <summary>Divergent versions detected between cache and durable during sync.</summary>
    ConflictDetected,

    /// <summary>Profile storage mode changed (e.g., local → replicated).</summary>
    ModeChanged
}

/// <summary>
/// Immutable event record emitted by the replication subsystem.
/// </summary>
public sealed record StorageEvent(
    StorageEventType Type,
    string Container,
    string Key,
    string? Detail = null);
