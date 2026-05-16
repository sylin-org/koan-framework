namespace Koan.ZenGarden;

/// <summary>
/// The type of content change detected in storage.
/// </summary>
public enum StorageChangeOp
{
    /// <summary>A new file was created.</summary>
    Create = 0,
    /// <summary>An existing file was modified.</summary>
    Modify = 1,
    /// <summary>A file was deleted.</summary>
    Delete = 2,
    /// <summary>A file was renamed or moved.</summary>
    Rename = 3
}

/// <summary>
/// A typed record representing a single content change in garden storage.
/// Produced by the CDC subscription (<see cref="ZenGardenStorageSurface.OnContentChange"/>).
/// </summary>
public sealed record StorageContentChange
{
    /// <summary>
    /// The operation type (create, modify, delete, rename).
    /// </summary>
    public required StorageChangeOp Op { get; init; }

    /// <summary>
    /// Relative path within the storage bank (e.g., "snap-vault-photos/image.jpg").
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Previous path for rename operations. Null for create/modify/delete.
    /// </summary>
    public string? OldPath { get; init; }

    /// <summary>
    /// File size in bytes. Null for delete operations.
    /// </summary>
    public long? Size { get; init; }

    /// <summary>
    /// Timestamp when the change occurred on the stone.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Change sequence number from the storage changelog. Used for cursor tracking.
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>
    /// The seed-bank (replica set) name where the change occurred.
    /// </summary>
    public string? BankName { get; init; }
}
