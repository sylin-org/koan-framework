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

/// <summary>Chooses the winner when both sides of a bidirectional mirror contain the same identity.</summary>
public enum MirrorConflict
{
    /// <summary>Use the entity with the greatest supported <c>[Timestamp]</c> value.</summary>
    Latest,

    /// <summary>The entity selected by <c>From(...)</c> wins.</summary>
    Source,

    /// <summary>The entity selected by <c>To(...)</c> wins.</summary>
    Destination,

    /// <summary>Report the overlap without changing either entity.</summary>
    Report
}
