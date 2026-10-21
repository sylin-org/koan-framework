using System.Reflection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sorting;

namespace Koan.Data.Core.Querying;

/// <summary>
/// Continues a read ordered by the Entity identity from the last identity it returned (keyset, or seek, paging).
///
/// <para>A numbered page is a position, and a position moves when another writer inserts or deletes a row ahead
/// of it: page two then repeats a row page one returned, or skips one it never reached. Each page also makes the
/// store walk past every earlier row, so reading a large set costs quadratically, and the offset caps the read at
/// <see cref="int.MaxValue"/> rows. "Identity after the last one returned" names a row instead of a position, so
/// a row present for the whole read is returned exactly once, each page costs the same, and nothing is counted.</para>
///
/// <para>The seek compares in the store, through the same filter pushdown every query uses, and the page is
/// ordered by the same identity in the same store. Both therefore use the store's own comparison of that key:
/// a text key by its collation, a BSON key by BSON order. The identity is unique under that comparison, so the
/// order has no ties for the seek to cross. Koan never compares identities in memory here.</para>
/// </summary>
internal sealed class IdentitySeek
{
    private readonly FieldPath _identity;
    private readonly FilterOperator _after;

    private IdentitySeek(PropertyInfo identity, bool descending)
    {
        _identity = FieldPath.Of(identity.Name);
        _after = descending ? FilterOperator.Lt : FilterOperator.Gt;
    }

    /// <summary>
    /// The seek for a read whose order leads with the Entity identity, when the provider can apply it;
    /// otherwise <see langword="null"/>. Keys after a leading identity never decide anything, because the
    /// identity has no ties.
    /// </summary>
    public static IdentitySeek? For(Type entityType, IReadOnlyList<SortSpec> sort, Func<MemberPath, bool> isIdentity, FilterSupport support)
    {
        if (sort.Count == 0 || !isIdentity(sort[0].Path)) return null;
        if (AggregateMetadata.GetIdSpec(entityType)?.Prop is not { } identity) return null;

        var seek = new IdentitySeek(identity, sort[0].Desc);
        var probe = new FieldFilter(seek._identity, seek._after, FilterValue.Of(null));
        return FilterSplitter.Split(probe, support, entityType).Residual is null ? seek : null;
    }

    /// <summary>The rows strictly after <paramref name="cursor"/> in the read's order.</summary>
    public Filter After(object cursor) => new FieldFilter(_identity, _after, FilterValue.Of(cursor));

    /// <summary>The query for the page after <paramref name="cursor"/>: the read's own filter and the seek.</summary>
    public QueryDefinition Continue(QueryDefinition query, object cursor)
        => query.Where(Filter.And(query.Filter, After(cursor)));
}
