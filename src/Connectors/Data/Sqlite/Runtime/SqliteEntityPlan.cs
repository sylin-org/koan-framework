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
    /// A scalar written to a real column, in the order-preserving form DATA-0100 defines. The same function encodes a
    /// filter comparand on that column (<see cref="RelationalEntityPlan{TEntity,TKey,TDialect}.Filter"/>) and an
    /// identity key bound to it, so a query, a Get and a Delete compare like for like with what was written
    /// (DATA-0136). A document column is encoded by the codec's converters instead.
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
