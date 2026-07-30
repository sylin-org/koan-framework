using System.Collections.Frozen;
using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sorting;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo.Runtime;

internal sealed class MongoQueryCompiler<TEntity, TKey>(MongoEntityPlan<TEntity, TKey> entity)
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public MongoQueryPlan Compile(QueryDefinition query, int? hardLimit = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filter = query.Filter is null ? new BsonDocument() : Visit(query.Filter);
        var handledSort = Sort(query.Sort, out var sort);
        var canPage = handledSort.Count == query.Sort.Count;
        var limit = hardLimit ?? (query.HasPagination && canPage ? query.EffectivePageSize() : (int?)null);
        var skip = hardLimit is null && query.HasPagination && canPage ? query.EffectiveOffset() : 0;
        return new MongoQueryPlan(
            filter,
            sort,
            skip,
            limit,
            true,
            handledSort.ToFrozenSet(),
            query.HasPagination && canPage,
            query.CountStrategy is null ? CountExecutionKind.None : CountExecutionKind.Exact);
    }

    public FilterDefinition<BsonDocument> Predicate(Filter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Visit(filter);
    }

    private BsonDocument Visit(Filter filter) => filter switch
    {
        AllOf all => Logical("$and", all.Operands, matchAll: true),
        AnyOf any => Logical("$or", any.Operands, matchAll: false),
        Not not => new BsonDocument("$nor", new BsonArray([Visit(not.Operand)])),
        FieldFilter field => Field(field),
        ClrFilter => throw new NotSupportedException("MongoDB received a CLR residual instead of a pushable filter."),
        _ => throw new NotSupportedException($"MongoDB does not support filter node '{filter.GetType().Name}'.")
    };

    private BsonDocument Logical(string operation, IReadOnlyList<Filter> operands, bool matchAll)
    {
        if (operands.Count == 0)
            return matchAll ? new BsonDocument() : new BsonDocument("$expr", false);
        if (operands.Count == 1) return Visit(operands[0]);
        return new BsonDocument(operation, new BsonArray(operands.Select(Visit)));
    }

    private BsonDocument Field(FieldFilter filter)
    {
        if (filter.IgnoreCase)
            throw new NotSupportedException("MongoDB case-insensitive filter pushdown was not declared.");
        var resolved = FieldPathResolver.Resolve(typeof(TEntity), filter.Field);
        var logicalPath = resolved.CanonicalPath ?? filter.Field;
        var path = entity.Field(logicalPath, resolved, MappingConsumer.Filter);
        var scalar = filter.Value is FilterValue.Scalar one ? one.Value : null;
        var set = filter.Value switch
        {
            FilterValue.Set many => many.Values,
            FilterValue.Scalar single => [single.Value],
            _ => Array.Empty<object?>()
        };
        BsonValue Value(object? value) => entity.FilterValue(logicalPath, resolved, value);
        BsonArray Values() => new(set.Select(Value));

        return filter.Operator switch
        {
            FilterOperator.Eq => new BsonDocument(path, Value(scalar)),
            FilterOperator.Ne when scalar is null => new BsonDocument("$and", new BsonArray([
                new BsonDocument(path, new BsonDocument("$exists", true)),
                new BsonDocument(path, new BsonDocument("$ne", BsonNull.Value))
            ])),
            FilterOperator.Ne => new BsonDocument(path, new BsonDocument("$ne", Value(scalar))),
            FilterOperator.Gt => Compare(path, "$gt", Value(scalar)),
            FilterOperator.Gte => Compare(path, "$gte", Value(scalar)),
            FilterOperator.Lt => Compare(path, "$lt", Value(scalar)),
            FilterOperator.Lte => Compare(path, "$lte", Value(scalar)),
            FilterOperator.In => Compare(path, "$in", Values()),
            FilterOperator.Nin when set.Any(static value => value is null) => new BsonDocument("$and", new BsonArray([
                new BsonDocument(path, new BsonDocument("$exists", true)),
                Compare(path, "$nin", Values())
            ])),
            FilterOperator.Nin => Compare(path, "$nin", Values()),
            FilterOperator.StartsWith => RegexFilter(path, $"^{System.Text.RegularExpressions.Regex.Escape((string)scalar!)}"),
            FilterOperator.EndsWith => RegexFilter(path, $"{System.Text.RegularExpressions.Regex.Escape((string)scalar!)}$"),
            FilterOperator.Contains => RegexFilter(path, System.Text.RegularExpressions.Regex.Escape((string)scalar!)),
            FilterOperator.Exists => Exists(path, scalar as bool? ?? true),
            FilterOperator.Has => new BsonDocument(path, Value(scalar)),
            FilterOperator.HasAny => Compare(path, "$in", Values()),
            FilterOperator.HasAll => Compare(path, "$all", Values()),
            FilterOperator.HasNone => Compare(path, "$nin", Values()),
            FilterOperator.Size => Compare(
                path,
                "$size",
                MongoValues.FromNeutral(Convert.ToInt32(scalar, CultureInfo.InvariantCulture))),
            _ => throw new NotSupportedException(
                $"MongoDB does not support '{filter.Operator}' for field '{filter.Field}'.")
        };
    }

    private IReadOnlyList<SortSpec> Sort(
        IReadOnlyList<SortSpec> requested,
        out SortDefinition<BsonDocument>? definition)
    {
        if (requested.Count == 0)
        {
            definition = null;
            return [];
        }
        var parts = new List<SortDefinition<BsonDocument>>(requested.Count);
        var handled = new List<SortSpec>(requested.Count);
        foreach (var sort in requested)
        {
            if (sort.Path.TraversesCollection || sort.Aggregation != SortAggregation.None) break;
            var path = FieldPath.Of(sort.Path.Members.Select(static member => member.Name).ToArray());
            var resolved = FieldPathResolver.Resolve(typeof(TEntity), path);
            var name = entity.Field(path, resolved, MappingConsumer.Order);
            parts.Add(sort.Desc
                ? Builders<BsonDocument>.Sort.Descending(name)
                : Builders<BsonDocument>.Sort.Ascending(name));
            handled.Add(sort);
        }
        if (handled.Count != requested.Count)
        {
            definition = null;
            return [];
        }
        definition = Builders<BsonDocument>.Sort.Combine(parts);
        return handled;
    }

    private static BsonDocument Compare(string path, string operation, BsonValue value) =>
        new(path, new BsonDocument(operation, value));

    private static BsonDocument RegexFilter(string path, string pattern) =>
        new(path, new BsonRegularExpression(pattern));

    private static BsonDocument Exists(string path, bool desired) => desired
        ? new BsonDocument("$and", new BsonArray([
            new BsonDocument(path, new BsonDocument("$exists", true)),
            new BsonDocument(path, new BsonDocument("$ne", BsonNull.Value))
        ]))
        : new BsonDocument("$or", new BsonArray([
            new BsonDocument(path, new BsonDocument("$exists", false)),
            new BsonDocument(path, BsonNull.Value)
        ]));
}

internal sealed record MongoQueryPlan(
    FilterDefinition<BsonDocument> Filter,
    SortDefinition<BsonDocument>? Sort,
    int Skip,
    int? Limit,
    bool FilterHandled,
    IReadOnlySet<SortSpec> SortHandled,
    bool PaginationHandled,
    CountExecutionKind CountExecution);
