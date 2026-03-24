namespace Koan.Data.Core.Transfers;

/// <summary>
/// Specifies the high-level intent of a data transfer operation between two adapters or providers.
/// </summary>
public enum TransferKind
{
    /// <summary>
    /// Entities are duplicated from source to destination. Both source and destination
    /// retain their data after the operation completes.
    /// </summary>
    Copy,

    /// <summary>
    /// Entities are copied from source to destination and then deleted from the source.
    /// Source is empty after a successful move.
    /// </summary>
    Move,

    /// <summary>
    /// Destination is brought into sync with the source. Entities absent in source
    /// are removed from destination; entities present in source are upserted.
    /// Direction of sync is controlled by <see cref="MirrorMode"/>.
    /// </summary>
    Mirror
}

/// <summary>
/// Controls when source entities are deleted during a <see cref="TransferKind.Move"/> operation.
/// </summary>
public enum DeleteStrategy
{
    /// <summary>
    /// All entities are copied first, then deleted from source in a second pass.
    /// Safest option — destination is fully populated before any source records are removed.
    /// </summary>
    AfterCopy,

    /// <summary>
    /// Source entities are deleted in batches interleaved with the copy.
    /// Reduces peak memory and storage, but partial failures leave the source in a mixed state.
    /// </summary>
    Batched,

    /// <summary>
    /// Each entity is deleted from source immediately after it is confirmed written to destination.
    /// Lowest storage overhead; highest sensitivity to write failures.
    /// </summary>
    Synced
}

/// <summary>
/// Controls the directionality of a <see cref="TransferKind.Mirror"/> operation.
/// </summary>
public enum MirrorMode
{
    /// <summary>
    /// Source is authoritative. Destination is updated to match source.
    /// Entities in destination that are absent in source are deleted.
    /// </summary>
    Push,

    /// <summary>
    /// Destination is authoritative. Source is updated to match destination.
    /// Entities in source that are absent in destination are deleted.
    /// </summary>
    Pull,

    /// <summary>
    /// Both source and destination are reconciled. Entities present in either
    /// are propagated to the other; conflicts are resolved by last-write-wins (by default).
    /// </summary>
    Bidirectional
}
