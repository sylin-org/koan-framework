using System.Collections.Frozen;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Relational.Linq;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteQueryCompiler<TEntity>(string identityName)
{
    private readonly SqliteDialect _dialect = new();

    public SqlitePredicatePlan CompilePredicate(Filter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var translated = new SqlFilterTranslator(_dialect, typeof(TEntity), ResolveColumn).Translate(filter);
        return new SqlitePredicatePlan(translated.whereSql, translated.parameters);
    }

    public SqliteQueryPlan Compile(string table, QueryDefinition query, int? hardLimit = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        var parameters = new List<object?>();
        var where = "";
        var filterHandled = query.Filter is null;
        if (query.Filter is not null)
        {
            var translated = new SqlFilterTranslator(_dialect, typeof(TEntity), ResolveColumn).Translate(query.Filter);
            where = " WHERE " + translated.whereSql;
            parameters.AddRange(translated.parameters);
            filterHandled = true;
        }

        var handledSort = CompileSort(query.Sort, out var order);
        var sortComplete = query.Sort.Count == handledSort.Count;
        var mayPage = sortComplete;
        var limit = hardLimit ?? (query.HasPagination && mayPage ? query.EffectivePageSize() : (int?)null);
        var offset = hardLimit is null && query.HasPagination && mayPage ? query.EffectiveOffset() : 0;
        var page = limit is null ? "" : $" LIMIT {limit.Value} OFFSET {offset}";
        var sql = $"SELECT koan_row.\"Id\", koan_row.\"Json\" FROM {SqliteDialect.Quote(table)} AS koan_row{where}{order}{page}";
        var countSql = query.CountStrategy is null
            ? null
            : $"SELECT COUNT(*) FROM {SqliteDialect.Quote(table)} AS koan_row{where}";

        return new SqliteQueryPlan(
            sql,
            countSql,
            parameters,
            filterHandled,
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
            if (sort.Path.TraversesCollection || sort.Path.Members.Count != 1) break;
            var name = sort.Path.Members[0].Name;
            var expression = string.Equals(name, identityName, StringComparison.Ordinal)
                ? "koan_row.\"Id\""
                : JsonValue([name], sort.Path.ValueType);
            expressions.Add(expression + (sort.Desc ? " DESC" : " ASC"));
            handled.Add(sort);
        }

        if (handled.Count != requested.Count)
        {
            sql = "";
            return [];
        }

        sql = " ORDER BY " + string.Join(", ", expressions);
        return handled;
    }

    private string ResolveColumn(FieldPath path, ResolvedField field)
    {
        if (!field.IsManaged && path.Segments.Count == 1 &&
            string.Equals(path.Leaf, identityName, StringComparison.Ordinal))
            return "koan_row.\"Id\"";
        return JsonValue(path.Segments, field.LeafType);
    }

    private static string JsonValue(IReadOnlyList<string> segments, Type valueType)
    {
        var path = "$" + string.Concat(segments.Select(static segment =>
            ".\"" + segment.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""));
        var literal = path.Replace("'", "''", StringComparison.Ordinal);
        var extracted = $"json_extract(koan_row.\"Json\", '{literal}')";
        var type = Nullable.GetUnderlyingType(valueType) ?? valueType;
        return IsNumeric(type) || type == typeof(bool) || type == typeof(TimeSpan)
            ? $"CAST({extracted} AS NUMERIC)"
            : extracted;
    }

    private static bool IsNumeric(Type type) =>
        type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
        type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
        type == typeof(float) || type == typeof(double) || type == typeof(decimal);
}

internal sealed record SqliteQueryPlan(
    string Sql,
    string? CountSql,
    IReadOnlyList<object?> Parameters,
    bool FilterHandled,
    IReadOnlySet<SortSpec> SortHandled,
    bool PaginationHandled,
    CountExecutionKind CountExecution);

internal sealed record SqlitePredicatePlan(string Sql, IReadOnlyList<object?> Parameters);
