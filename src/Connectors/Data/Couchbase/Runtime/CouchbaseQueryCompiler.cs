using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sorting;

namespace Koan.Data.Connector.Couchbase.Runtime;

internal sealed class CouchbaseQueryCompiler<TEntity, TKey>(CouchbaseDocumentPlan<TEntity, TKey> entity)
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    internal CouchbaseQueryPlan Compile(QueryDefinition query, CouchbaseContainer container, int? forcedLimit = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        var context = new ParameterContext();
        var where = query.Filter is null ? null : Filter(query.Filter, context);
        var order = Order(query.Sort);
        var limit = forcedLimit ?? (query.HasPagination ? query.EffectivePageSize() : null);
        var offset = query.HasPagination ? query.EffectiveOffset() : 0;
        return new CouchbaseQueryPlan(where, order.Sql, order.Handled, context.Parameters, limit, offset);
    }

    private string Filter(Filter filter, ParameterContext context) => filter switch
    {
        AllOf all when all.Operands.Count == 0 => "TRUE",
        AllOf all => Join("AND", all.Operands, context),
        AnyOf any when any.Operands.Count == 0 => "FALSE",
        AnyOf any => Join("OR", any.Operands, context),
        Not not => $"NOT ({Filter(not.Operand, context)})",
        FieldFilter field => Field(field, context),
        ClrFilter => throw new NotSupportedException("Couchbase received a CLR-only residual filter for native execution."),
        _ => throw new NotSupportedException($"Couchbase does not support filter node '{filter.GetType().Name}'.")
    };

    private string Join(
        string operation,
        IReadOnlyList<Filter> values,
        ParameterContext context)
    {
        var translated = new string[values.Count];
        for (var index = 0; index < values.Count; index++)
            translated[index] = Filter(values[index], context);
        return "(" + string.Join($" {operation} ", translated) + ")";
    }

    private string Field(FieldFilter filter, ParameterContext context)
    {
        if (filter.IgnoreCase)
            throw new NotSupportedException("Couchbase case-insensitive filters are not claimed.");
        var resolved = FieldPathResolver.Resolve(typeof(TEntity), filter.Field);
        var path = "doc." + entity.Field(filter.Field, resolved, MappingConsumer.Filter);
        var values = filter.Value switch
        {
            FilterValue.Scalar scalar when filter.Operator == FilterOperator.Size =>
                new object?[] { Convert.ToInt32(scalar.Value, System.Globalization.CultureInfo.InvariantCulture) },
            FilterValue.Scalar scalar when filter.Operator == FilterOperator.Exists =>
                new object?[] { Convert.ToBoolean(scalar.Value, System.Globalization.CultureInfo.InvariantCulture) },
            FilterValue.Scalar scalar => new[] { entity.FilterValue(filter.Field, resolved, scalar.Value) },
            FilterValue.Set set => set.Values.Select(value => entity.FilterValue(filter.Field, resolved, value)).ToArray(),
            FilterValue.None => [],
            _ => throw new NotSupportedException("Couchbase received an unknown filter value shape.")
        };
        return filter.Operator switch
        {
            FilterOperator.Eq when values[0] is null => $"{path} IS NULL",
            FilterOperator.Eq => $"{path} = {context.Add(values[0])}",
            FilterOperator.Ne when values[0] is null => $"{path} IS NOT NULL",
            FilterOperator.Ne => $"({path} IS NULL OR {path} != {context.Add(values[0])})",
            FilterOperator.Gt => $"{path} > {context.Add(values[0])}",
            FilterOperator.Gte => $"{path} >= {context.Add(values[0])}",
            FilterOperator.Lt => $"{path} < {context.Add(values[0])}",
            FilterOperator.Lte => $"{path} <= {context.Add(values[0])}",
            FilterOperator.In => $"{path} IN {context.Add(values)}",
            FilterOperator.Nin => $"({path} IS NULL OR {path} NOT IN {context.Add(values)})",
            FilterOperator.StartsWith => $"{path} LIKE {context.Add(Escape(values[0]) + "%")}",
            FilterOperator.EndsWith => $"{path} LIKE {context.Add("%" + Escape(values[0]))}",
            FilterOperator.Contains => $"{path} LIKE {context.Add("%" + Escape(values[0]) + "%")}",
            FilterOperator.Exists => Convert.ToBoolean(values[0]) ? $"{path} IS VALUED" : $"{path} IS NOT VALUED",
            FilterOperator.Has => $"{context.Add(values[0])} IN {path}",
            FilterOperator.HasAny => $"ANY elementValue IN {path} SATISFIES elementValue IN {context.Add(values)} END",
            FilterOperator.HasAll => $"EVERY elementValue IN {context.Add(values)} SATISFIES elementValue IN {path} END",
            FilterOperator.HasNone => $"EVERY elementValue IN {context.Add(values)} SATISFIES elementValue NOT IN {path} END",
            FilterOperator.Size => $"ARRAY_LENGTH({path}) = {context.Add(values[0])}",
            _ => throw new NotSupportedException($"Couchbase does not support operator '{filter.Operator}'.")
        };
    }

    private (string Sql, IReadOnlySet<SortSpec> Handled) Order(IReadOnlyList<SortSpec> sorts)
    {
        var values = new List<string>(sorts.Count + 1);
        foreach (var sort in sorts)
        {
            if (sort.Path.TraversesCollection || sort.Aggregation != SortAggregation.None)
                throw new NotSupportedException($"Couchbase cannot natively order collection path '{sort.Path}'.");
            var path = FieldPath.Of(sort.Path.Members.Select(static member => member.Name).ToArray());
            var resolved = FieldPathResolver.Resolve(typeof(TEntity), path);
            values.Add("doc." + entity.Field(path, resolved, MappingConsumer.Order) + (sort.Desc ? " DESC" : " ASC"));
        }
        values.Add("META(doc).id ASC");
        return (string.Join(", ", values), sorts.ToHashSet());
    }

    private static string Escape(object? value) => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed class ParameterContext
    {
        private int _ordinal;
        internal Dictionary<string, object?> Parameters { get; } = new();

        internal string Add(object? value)
        {
            var name = "p" + _ordinal++;
            Parameters.Add(name, value);
            return "$" + name;
        }
    }
}

internal sealed record CouchbaseQueryPlan(
    string? Where,
    string Order,
    IReadOnlySet<SortSpec> SortHandled,
    IReadOnlyDictionary<string, object?> Parameters,
    int? Limit,
    int Offset);

internal readonly record struct CouchbaseContainer(string Scope, string Collection)
{
    internal string Qualified(string bucket) =>
        $"`{Escape(bucket)}`.`{Escape(Scope)}`.`{Escape(Collection)}`";

    private static string Escape(string value) => value.Replace("`", "``", StringComparison.Ordinal);
}
