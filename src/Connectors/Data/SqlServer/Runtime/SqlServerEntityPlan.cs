using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Semantics;
using Koan.Data.Relational;

namespace Koan.Data.Connector.SqlServer.Runtime;

/// <summary>
/// SQL Server qualifies a table with a schema and reads it as the shared plan does; a DateTime on its way to a column
/// binds to the tick (<see cref="SqlServerDateTime"/>).
/// </summary>
internal sealed class SqlServerEntityPlan<TEntity, TKey>(
    MappingPlan mapping,
    SqlServerRepositoryOptions options,
    DataSegmentationPlan segmentation)
    : RelationalEntityPlan<TEntity, TKey, SqlServerDialect>(
        mapping,
        segmentation,
        new SqlServerDialect(),
        mapping.Container.Namespace.LastOrDefault() ?? options.Schema)
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    protected override object? EncodeScalar(object? value) => SqlServerDateTime.Exact(value);
}
