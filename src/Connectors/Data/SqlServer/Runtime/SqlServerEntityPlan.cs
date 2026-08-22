using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Semantics;
using Koan.Data.Relational;

namespace Koan.Data.Connector.SqlServer.Runtime;

/// <summary>
/// SQL Server qualifies a table with a schema and reads it exactly as the shared plan does, so it declares the
/// qualifier and nothing else.
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
    where TKey : notnull;
