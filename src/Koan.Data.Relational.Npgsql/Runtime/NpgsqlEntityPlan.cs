using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Semantics;

namespace Koan.Data.Relational.Npgsql.Runtime;

/// <summary>
/// PostgreSQL qualifies a table with a schema. It differs from its siblings in two places: a document column
/// comes back as <c>jsonb</c> and is cast on the way out, and a managed path is sometimes qualified by its
/// table.
/// </summary>
internal sealed class NpgsqlEntityPlan<TEntity, TKey>(
    MappingPlan mapping,
    NpgsqlRepositoryOptions options,
    DataSegmentationPlan segmentation)
    : RelationalEntityPlan<TEntity, TKey, NpgsqlDialect>(
        mapping,
        segmentation,
        new NpgsqlDialect(),
        mapping.Container.Namespace.LastOrDefault() ?? options.SearchPath)
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    /// A document column is <c>jsonb</c>, which the client would otherwise hand back as its own type. Casting
    /// it in the projection keeps the read on the same string path every other store uses.
    /// </summary>
    protected override string Project(string root) => IsStructuredRoot(root)
        ? $"{NpgsqlDialect.Quote(root)}::text AS {NpgsqlDialect.Quote(root)}"
        : NpgsqlDialect.Quote(root);

    /// <summary>
    /// The same managed path, qualified by its table for a statement that has more than one table in scope.
    /// </summary>
    public string ManagedPath(string storageName, Type type, bool qualify)
    {
        var expression = ManagedPath(storageName, type);
        if (!qualify) return expression;
        var root = NpgsqlDialect.Quote(ManagedRoot(storageName));
        return expression.Replace(root, $"{QualifiedTable}.{root}", StringComparison.Ordinal);
    }
}
