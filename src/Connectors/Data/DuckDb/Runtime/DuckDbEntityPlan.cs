using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Semantics;
using Koan.Data.Relational;
using System.Data.Common;
using DuckDB.NET.Data;

namespace Koan.Data.Connector.DuckDb.Runtime;

/// <summary>
/// DuckDB has one qualifier, the <c>main</c> schema. Like its SQLite sibling it reads columns through a
/// named subquery, writes scalars in DATA-0100's order-preserving form, and the client hands back a
/// reader rather than a row — with one engine-specific wrinkle: BLOB columns surface as streams, which
/// hydration drains into the byte[] the mapping declares.
/// </summary>
internal sealed class DuckDbEntityPlan<TEntity, TKey> : RelationalEntityPlan<TEntity, TKey, DuckDbDialect>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    internal DuckDbEntityPlan(MappingPlan mapping, DataSegmentationPlan segmentation)
        : base(mapping, segmentation, new DuckDbDialect(), qualifier: null)
    {
        if (mapping.Container.Namespace.Count > 1 ||
            mapping.Container.Namespace.Count == 1 &&
            !string.Equals(mapping.Container.Namespace[0], "main", StringComparison.OrdinalIgnoreCase))
            throw new MappingCompilationException(mapping.Source, mapping.EntityType,
                "DuckDB mappings support the empty namespace or 'main'.");

        foreach (var identity in mapping.Identity.Parts)
        {
            var binding = mapping.Bindings.Single(candidate => candidate.Id == identity.Id);
            if (binding.PhysicalPath.IsNested || binding.Shape != MappingValueShape.Scalar)
                throw new MappingCompilationException(mapping.Source, mapping.EntityType,
                    "DuckDB identity components require scalar physical names.");
        }
    }

    /// <summary>
    /// Every statement this adapter writes aliases the table <c>koan_row</c> and addresses columns through
    /// it, including the expressions <see cref="DuckDbDialect.Read"/> produces.
    /// </summary>
    protected override string Project(string root) => $"koan_row.{DuckDbDialect.Quote(root)}";

    /// <summary>
    /// A scalar written to a real column, in the order-preserving form DATA-0100 defines, so that a filter —
    /// whose comparand is encoded the same way by <see cref="ComparableScalarEncoding.EncodeComparand"/> —
    /// compares like for like.
    /// </summary>
    protected override object? EncodeScalar(object? value) => ComparableScalarEncoding.EncodeComparand(value);

    /// <summary>The client exposes a reader rather than a row, so the columns are lifted out of it first.</summary>
    internal TEntity Hydrate(DbDataReader reader)
    {
        var roots = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var root in Roots)
        {
            var ordinal = reader.GetOrdinal(root);
            roots[root] = reader.IsDBNull(ordinal) ? null : Materialize(reader.GetValue(ordinal));
        }
        return Hydrate(roots);
    }

    /// <summary>DuckDB hands BLOB columns back as an unmanaged stream; the mapping expects the bytes.</summary>
    private static object? Materialize(object? value)
    {
        if (value is UnmanagedMemoryStream stream)
        {
            using var _ = stream;
            if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        return value;
    }
}
