using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Semantics;
using Koan.Data.Relational;

namespace Koan.Data.Connector.MySql.Runtime;

/// <summary>
/// MySQL qualifies a table with a database and reads it exactly as the shared plan does, so it declares the
/// qualifier and nothing else.
/// </summary>
internal sealed class MySqlEntityPlan<TEntity, TKey>(
    MappingPlan mapping,
    MySqlRepositoryOptions options,
    DataSegmentationPlan segmentation)
    : RelationalEntityPlan<TEntity, TKey, MySqlDialect>(
        mapping,
        segmentation,
        new MySqlDialect(),
        mapping.Container.Namespace.LastOrDefault() ?? options.Database)
    where TEntity : class, IEntity<TKey>
    where TKey : notnull;
