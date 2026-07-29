using System.Collections.Frozen;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Koan.Data.Relational.Linq;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteMappedQueryCompiler<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly SqliteMappedEntityPlan<TEntity, TKey> _entity;
    private readonly SqliteDialect _dialect = new();

    public SqliteMappedQueryCompiler(SqliteMappedEntityPlan<TEntity, TKey> entity) =>
        _entity = entity;

    public SqlitePredicatePlan CompilePredicate(Koan.Data.Abstractions.Filtering.Filter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var translated = new SqlFilterTranslator(_dialect, _entity.Mapping).Translate(filter);
        return new SqlitePredicatePlan(translated.whereSql, translated.parameters);
    }

    public SqliteQueryPlan Compile(QueryDefinition query, int? hardLimit = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        var parameters = new List<object?>();
        var where = "";
        if (query.Filter is not null)
        {
            var translated = new SqlFilterTranslator(_dialect, _entity.Mapping).Translate(query.Filter);
            where = " WHERE " + translated.whereSql;
            parameters.AddRange(translated.parameters);
        }

        var handledSort = CompileSort(query.Sort, out var order);
        var sortComplete = query.Sort.Count == handledSort.Count;
        var mayPage = sortComplete;
        var limit = hardLimit ?? (query.HasPagination && mayPage ? query.EffectivePageSize() : (int?)null);
        var offset = hardLimit is null && query.HasPagination && mayPage ? query.EffectiveOffset() : 0;
        var page = limit is null ? "" : $" LIMIT {limit.Value} OFFSET {offset}";
        var table = SqliteDialect.Quote(_entity.Table);
        var sql = $"SELECT {_entity.Select} FROM {table} AS koan_row{where}{order}{page}";
        var countSql = query.CountStrategy is null
            ? null
            : $"SELECT COUNT(*) FROM {table} AS koan_row{where}";
        return new SqliteQueryPlan(
            sql,
            countSql,
            parameters,
            FilterHandled: true,
            handledSort.ToFrozenSet(),
            query.HasPagination && mayPage,
            query.CountStrategy is null ? CountExecutionKind.None : CountExecutionKind.Exact);
    }

    private IReadOnlyList<SortSpec> CompileSort(IReadOnlyList<SortSpec> requested, out string sql)
    {
        if (requested.Count == 0)
        {
            sql = "";
            return [];
        }

        var expressions = new List<string>(requested.Count);
        var handled = new List<SortSpec>(requested.Count);
        foreach (var sort in requested)
        {
            if (sort.Path.TraversesCollection) break;
            try
            {
                var logical = MappingPath.Of(sort.Path.Members.Select(static member => member.Name).ToArray());
                var binding = _entity.Mapping.Use(logical, MappingConsumer.Order).Bindings.Single();
                expressions.Add(_dialect.Read(binding.PhysicalPath, binding.Shape, binding.PhysicalType) +
                    (sort.Desc ? " DESC" : " ASC"));
                handled.Add(sort);
            }
            catch (MappingValueException)
            {
                break;
            }
        }
        if (handled.Count != requested.Count)
        {
            sql = "";
            return [];
        }
        sql = " ORDER BY " + string.Join(", ", expressions);
        return handled;
    }
}
