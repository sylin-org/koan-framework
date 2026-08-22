using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Semantics;
using Koan.Data.Relational;
using Microsoft.Data.Sqlite;

namespace Koan.Data.Connector.Sqlite.Runtime;

/// <summary>
/// SQLite has no qualifier above the table. It differs from its siblings in three places: reads address columns
/// through a named subquery, scalars are written in DATA-0100's order-preserving form, and the client hands
/// back a reader rather than a row.
/// </summary>
internal sealed class SqliteEntityPlan<TEntity, TKey> : RelationalEntityPlan<TEntity, TKey, SqliteDialect>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    internal SqliteEntityPlan(MappingPlan mapping, DataSegmentationPlan segmentation)
        : base(mapping, segmentation, new SqliteDialect(), qualifier: null)
    {
        if (mapping.Container.Namespace.Count > 1 ||
            mapping.Container.Namespace.Count == 1 &&
            !string.Equals(mapping.Container.Namespace[0], "main", StringComparison.OrdinalIgnoreCase))
            throw new MappingCompilationException(mapping.Source, mapping.EntityType,
                "SQLite mappings support the empty namespace or 'main'.");

        foreach (var identity in mapping.Identity.Parts)
        {
            var binding = mapping.Bindings.Single(candidate => candidate.Id == identity.Id);
            if (binding.PhysicalPath.IsNested || binding.Shape != MappingValueShape.Scalar)
                throw new MappingCompilationException(mapping.Source, mapping.EntityType,
                    "SQLite identity components require scalar physical names.");
        }
    }

    /// <summary>
    /// Every statement this adapter writes aliases the table <c>koan_row</c> and addresses columns through it,
    /// including the expressions <see cref="SqliteDialect.Read"/> produces. A SELECT list is the one place the
    /// alias is redundant — one table is in scope, so a bare column resolves either way, and the suite passes
    /// without it. It is spelled here anyway so that every column reference in the adapter reads alike.
    /// </summary>
    protected override string Project(string root) => $"koan_row.{SqliteDialect.Quote(root)}";

    /// <summary>
    /// A scalar written to a real column, in the order-preserving form DATA-0100 defines, so that a filter —
    /// whose comparand is encoded the same way by <see cref="ComparableScalarEncoding.EncodeComparand"/> —
    /// compares like for like.
    ///
    /// <para>Of the four relational adapters only this one encodes here; the other three write the value as it
    /// comes. Disabling this leaves the suite green, including the DATA-0100 oracle, which says the branch
    /// carries no governed type today — a document column is encoded by the codec's converters instead, and a
    /// projected column is computed by the store. Whether that makes this defensive or makes the other three
    /// wrong is an open question rather than something this seam settles.</para>
    /// </summary>
    protected override object? EncodeScalar(object? value) => ComparableScalarEncoding.EncodeComparand(value);

    /// <summary>The client exposes a reader rather than a row, so the columns are lifted out of it first.</summary>
    internal TEntity Hydrate(SqliteDataReader reader)
    {
        var roots = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var root in Roots)
        {
            var ordinal = reader.GetOrdinal(root);
            roots[root] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
        }
        return Hydrate(roots);
    }
}
