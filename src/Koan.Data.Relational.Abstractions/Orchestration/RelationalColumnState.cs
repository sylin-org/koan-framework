namespace Koan.Data.Relational.Orchestration;

/// <summary>
/// One column as a store actually holds it.
///
/// <para>Deliberately not the same type as <see cref="RelationalColumnDefinition"/>. What the mapping asks for
/// and what a catalogue reports are different things, and one record for both forces every store to fill fields
/// it cannot know: a described column has no CLR type unless the store can name one, and no projection source
/// unless someone parses a generation expression. Sharing the type would mean filling those with plausible
/// defaults the framework then compares against - drift invented out of a placeholder.</para>
///
/// <para><see cref="NativeType"/> is the store's own spelling, and the store is what compares it, through
/// <see cref="IRelationalDdlExecutor.ColumnMatches"/>. <see cref="ClrType"/> is for a store whose catalogue maps
/// cleanly onto CLR types and which is content with the default comparison.</para>
/// </summary>
/// <param name="Name">The column name as the store spells it.</param>
/// <param name="Nullable">Whether the store accepts an absent value here.</param>
/// <param name="IsGenerated">Whether the store supplies the value on insert - an auto-increment or sequence.</param>
/// <param name="IsProjected">Whether the store computes the value on every write.</param>
/// <param name="NativeType">The store's own type spelling, when it can produce one.</param>
/// <param name="ClrType">The CLR meaning, when the store's catalogue maps onto one.</param>
/// <param name="ProjectionStamp">
/// What the store recorded about the recipe a projected column was built from, where it can carry one.
///
/// <para>A projected column is computed by an expression the store keeps, and that expression can go stale
/// without the column's type changing at all: the framework changes how it reads a value, new tables get the
/// new expression, and an existing table keeps the old one. Nothing about the column's shape says so. Reading
/// the stored expression back and comparing it is unreliable, because a store returns its own canonical
/// rendering rather than the text it was given — so a store that wants this checked records a marker it can
/// compare against instead, and one that does not leaves this null.</para>
/// </param>
public sealed record RelationalColumnState(
    string Name,
    bool Nullable,
    bool IsGenerated = false,
    bool IsProjected = false,
    string? NativeType = null,
    Type? ClrType = null,
    string? ProjectionStamp = null)
{
    /// <summary>How a mismatch names what was found.</summary>
    public override string ToString() =>
        (NativeType ?? ClrType?.Name ?? "unknown") +
        $"/nullable={Nullable}/generated={IsGenerated}/projected={IsProjected}" +
        (ProjectionStamp is null ? string.Empty : $"/recipe={ProjectionStamp}");
}
