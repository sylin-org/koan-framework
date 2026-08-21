namespace Koan.Data.Relational.Orchestration;

/// <summary>
/// A table as the store actually holds it, read in one pass.
///
/// <para>One description rather than a probe per column: every adapter already reads its whole catalogue in a
/// single statement — <c>PRAGMA table_info</c>, <c>information_schema.columns</c>, <c>sys.columns</c> — and
/// asking column by column would have turned one round trip into one per mapped value on every readiness check.</para>
///
/// <para>A column mapped to <see langword="null"/> is one the store holds but cannot describe — SQLite answers
/// TEXT for a string, a date and a Guid alike, so any definition it invented would be a lie the framework then
/// compares against. Presence is the key and description is the value, so a store admits exactly what it does
/// not know, per column, rather than making one store-wide claim its own answers can contradict.</para>
///
/// <para><see cref="Identity"/> is the primary key in the order the store holds it, which is the order it
/// indexes on. An empty list means the store keeps no primary key for this table.</para>
///
/// <para><see cref="Incompatible"/> is how a store reports something the neutral column model cannot express and
/// the framework has no business knowing — MySQL's storage engine, for one. The framework surfaces these
/// verbatim alongside its own findings rather than translating them.</para>
/// </summary>
public sealed record RelationalTableShape(
    IReadOnlyDictionary<string, RelationalColumnState?> Columns,
    IReadOnlyList<string> Identity,
    IReadOnlyList<string> Incompatible)
{
    public RelationalTableShape(
        IReadOnlyDictionary<string, RelationalColumnState?> columns,
        IReadOnlyList<string> identity)
        : this(columns, identity, [])
    {
    }
}
