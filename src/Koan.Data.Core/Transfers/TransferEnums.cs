namespace Koan.Data.Core.Transfers;

/// <summary>
/// Specifies the high-level intent of a data transfer operation between two adapters or providers.
/// </summary>
public enum TransferKind
{
    /// <summary>
    /// The selected entities are upserted into the destination. Both sides keep their rows.
    /// </summary>
    Copy,

    /// <summary>
    /// The selected entities are upserted into the destination, and then the copied rows are removed from the source.
    /// Only the selection is removed: rows outside the predicate, rows the caller cannot see (another tenant's,
    /// soft-deleted ones outside <c>WithDeleted()</c>) and rows another writer changed after they were copied stay in the
    /// source. The copy completes before the first delete, so a Move is not atomic: a process that stops in between
    /// leaves the rows in both places, and running the Move again finishes it (DATA-0155).
    /// </summary>
    Move,

    /// <summary>
    /// One or both sides are brought into line with the other, as <see cref="MirrorMode"/> says. A one-way mirror
    /// upserts the authoritative side's selection into the replica and deletes the replica's rows that the
    /// authoritative side no longer selects; a bidirectional mirror only adds and resolves, and never deletes.
    /// </summary>
    Mirror
}

/// <summary>
/// Controls the directionality of a <see cref="TransferKind.Mirror"/> operation.
/// </summary>
public enum MirrorMode
{
    /// <summary>
    /// <c>From()</c> is authoritative. Its selection is upserted into <c>To()</c>, and the rows of <c>To()</c> that
    /// <c>From()</c> no longer selects are deleted there, within the predicate.
    /// </summary>
    Push,

    /// <summary>
    /// <c>To()</c> is authoritative. Its selection is upserted into <c>From()</c>, and the rows of <c>From()</c> that
    /// <c>To()</c> no longer selects are deleted there, within the predicate.
    /// </summary>
    Pull,

    /// <summary>
    /// Each side receives the identities only the other side holds, and an identity both sides hold is resolved by the
    /// <see cref="MirrorConflict"/> policy. Nothing is deleted, so a row removed on one side comes back from the other.
    /// The default <see cref="MirrorConflict.Latest"/> resolves an overlap only through one comparable
    /// <c>[Timestamp]</c> (an <c>OnSave</c> one first); without it every overlap is left on both sides and listed in
    /// <c>Conflicts</c>, up to the <c>Batch()</c> size, with a warning giving the total.
    /// </summary>
    Bidirectional
}

/// <summary>Chooses the winner when both sides of a bidirectional mirror contain the same identity.</summary>
public enum MirrorConflict
{
    /// <summary>
    /// Use the entity with the greater value of its one <c>[Timestamp(OnSave = true)]</c> property, or of its only
    /// <c>[Timestamp]</c> property when it has no OnSave one. A set-once creation stamp never decides over an OnSave
    /// stamp. With no deciding candidate, or several, or values that cannot be compared, the overlap is left on both
    /// sides and reported as a conflict.
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

/// <summary>Which declared side of a transfer a batch of rows was written into (NK-252).</summary>
public enum TransferDirection
{
    /// <summary>Rows read from <c>From()</c> were written into <c>To()</c>.</summary>
    IntoDestination,

    /// <summary>Rows read from <c>To()</c> were written into <c>From()</c>: a Pull, or the reverse half of a bidirectional Mirror.</summary>
    IntoSource
}

/// <summary>How a transfer read its rows (DATA-0113).</summary>
public enum TransferReadStrategy
{
    /// <summary>Nothing was read: the run was a reported no-op.</summary>
    None,

    /// <summary>Every read streamed in provider-bounded pages.</summary>
    Streamed,

    /// <summary>At least one read was materialized once because the provider could not stream it; a warning says why.</summary>
    Materialized
}
