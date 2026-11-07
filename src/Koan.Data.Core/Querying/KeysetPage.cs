namespace Koan.Data.Core.Querying;

/// <summary>
/// A page of a list in a <see cref="Keyset{TEntity}"/> order (DATA-0170): at most the rows asked for, in the order, and the key the next
/// page continues from, or null on the last page. Nothing is counted.
/// </summary>
public sealed class KeysetPage<TEntity>
{
    internal KeysetPage(IReadOnlyList<TEntity> items, KeysetKey? next, Koan.Data.Abstractions.IQueryReadEvidence? evidence)
    {
        Items = items;
        Next = next;
        ReadEvidence = evidence;
    }

    /// <summary>The rows, in the order.</summary>
    public IReadOnlyList<TEntity> Items { get; }

    /// <summary>Where the next page continues, or null when no row follows this page.</summary>
    public KeysetKey? Next { get; }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    [System.Text.Json.Serialization.JsonIgnore]
    [System.Runtime.Serialization.IgnoreDataMember]
    public Koan.Data.Abstractions.IQueryReadEvidence? ReadEvidence { get; }
}
