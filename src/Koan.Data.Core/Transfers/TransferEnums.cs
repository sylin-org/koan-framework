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
    /// <summary>
    /// Use the entity with the greater value of its one <c>[Timestamp(OnSave = true)]</c> property, or of its only
    /// <c>[Timestamp]</c> property when it has no OnSave one. A set-once creation stamp never decides over an OnSave
    /// stamp; several deciding candidates report every overlap as a conflict.
    /// </summary>
    Latest,

    /// <summary>The entity selected by <c>From(...)</c> wins.</summary>
    Source,

    /// <summary>The entity selected by <c>To(...)</c> wins.</summary>
    Destination,

    /// <summary>Report the overlap without changing either entity.</summary>
    Report
}

/// <summary>
/// How a transfer writes the rows it reads (DATA-0149). <see cref="Exact"/> is the default.
/// </summary>
public enum TransferFidelity
{
    /// <summary>
    /// Each row arrives as it is stored: <c>[Timestamp]</c> values and every other stored value are kept, and Load,
    /// Upsert and Remove lifecycle and registered write stamps do not run for the transfer's reads and writes. A row
    /// without an identity still gets one. Tenancy, source policy, isolation, field transforms, the Move proof and the
    /// receipts apply as for any Entity operation.
    /// </summary>
    Exact,

    /// <summary>
    /// Each row is read and written as ordinary Entity work: Load lifecycle runs on every row read, Upsert lifecycle and
    /// every write stamp on every destination write (so <c>[Timestamp(OnSave = true)]</c> takes the transfer time), and a
    /// Move or Mirror runs Remove lifecycle for the rows it deletes.
    /// </summary>
    Semantic
}
