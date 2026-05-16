namespace Koan.Storage.Replication;

/// <summary>
/// Determines how a storage profile routes operations between providers.
/// </summary>
public enum StorageMode
{
    /// <summary>Local provider only — standalone store, no replication.</summary>
    Local,

    /// <summary>Remote provider only — nothing on local disk.</summary>
    Remote,

    /// <summary>Local cache with async push to durable remote. Pull-through on cache miss.</summary>
    Replicated
}
