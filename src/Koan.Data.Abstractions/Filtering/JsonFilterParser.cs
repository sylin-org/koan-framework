using Newtonsoft.Json.Linq;

namespace Koan.Data.Abstractions.Filtering;

/// <summary>Options controlling DSL parsing (currently case-insensitivity for string comparisons).</summary>
public sealed class FilterParseOptions
{
    public bool IgnoreCase { get; init; }
}

/// <summary>
/// Parses the Koan JSON filter DSL into the normalized <see cref="Filter"/> AST. The DSL
/// surface (Mongo-flavoured: <c>$and/$or/$not/$nor</c>, <c>$eq/$ne/$gt/$gte/$lt/$lte</c>,
/// <c>$in/$nin/$all/$size/$exists/$between</c>, wildcard strings, <c>$options.ignoreCase</c>)
/// is preserved; the keyword maps to a scalar or collection operator based on the resolved
/// leaf type — e.g. <c>{ "Tags": { "$in": ["x"] } }</c> on a <c>List&lt;string&gt;</c> field
/// becomes <see cref="FilterOperator.HasAny"/> (overlap), which is the fix for the original
/// <c>$in</c>-on-collection crash. Redundant operators are lowered here (<c>$nor</c> →
/// <c>Not(AnyOf)</c>, <c>$between</c> → <c>AllOf(Gte, Lte)</c>, wildcards → StartsWith/EndsWith/Contains).
///
/// Fails loud: malformed JSON, an unknown operator, a non-array <c>$in</c>, or <c>$size</c>
/// on a scalar field throws <see cref="FilterParseException"/>; an unknown field throws
/// <see cref="InvalidFilterFieldException"/>. Both map to <c>400</c> in the web layer.
/// </summary>
public static class JsonFilterParser
{
    /// <summary>Match-everything filter (empty conjunction is vacuously true).</summary>
    public static Filter MatchAll { get; } = new AllOf(Array.Empty<Filter>());

    public static Filter Parse<T>(string? json, FilterParseOptions? options = null)
        => Parse(typeof(T), json, options);

    public static Filter Parse(Type entityType, string? json, FilterParseOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(json)) return MatchAll;

        JObject doc;
        try { doc = JObject.Parse(Normalize(json!)); }
        catch (Exception ex) { throw new FilterParseException($"Invalid filter JSON: {ex.Message}", ex); }

        return ParseObject(entityType, doc, (options ?? new FilterParseOptions()).IgnoreCase);
    }

    private static string Normalize(string input)
    {
        var s = input.Trim();
        // Tolerate single-quoted JSON from querystrings, but only when there are no real double
        // quotes to corrupt. (The old builder also blanket-Uri-unescaped the payload, which
        // mangled values containing % or quotes — that is intentionally dropped here.)
        if (!s.Contains('"') && s.Contains('\'')) s = s.Replace('\'', '"');
        return s;
    }

    private static Filter ParseObject(Type entityType, JObject obj, bool ignoreCase)
    {
        ignoreCase = MergeOptions(obj, ignoreCase);

        if (obj.TryGetValue("$and", out var andTok) && andTok is JArray andArr)
            return new AllOf(andArr.Select(c => ParseObject(entityType, AsObject(c, "$and"), ignoreCase)).ToList());
        if (obj.TryGetValue("$or", out var orTok) && orTok is JArray orArr)
            return new AnyOf(orArr.Select(c => ParseObject(entityType, AsObject(c, "$or"), ignoreCase)).ToList());
        if (obj.TryGetValue("$nor", out var norTok) && norTok is JArray norArr)
            return new Not(new AnyOf(norArr.Select(c => ParseObject(entityType, AsObject(c, "$nor"), ignoreCase)).ToList()));
        if (obj.TryGetValue("$not", out var notTok) && notTok is JObject notObj)
            return new Not(ParseObject(entityType, notObj, ignoreCase));

        var clauses = new List<Filter>();
        foreach (var prop in obj.Properties())
        {
            if (prop.Name.StartsWith('$')) continue; // operators incl. $options handled above/inline
            var path = FieldPath.Of(prop.Name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var field = FieldPathResolver.Resolve(entityType, path); // throws InvalidFilterFieldException -> 400
            clauses.Add(ParseComparison(path, field, prop.Value, ignoreCase));
        }

        return clauses.Count switch
        {
            0 => MatchAll,
            1 => clauses[0],
            _ => new AllOf(clauses)
        };
    }

    private static Filter ParseComparison(FieldPath path, ResolvedField field, JToken token, bool ignoreCase)
    {
        var ic = ignoreCase && field.ComparableType == typeof(string);

        if (token.Type != JTokenType.Object)
        {
            if (field.TargetsCollection)
                return Leaf(path, FilterOperator.Has, Scalar(token), ic);

            if (field.ComparableType == typeof(string) && token.Type == JTokenType.String)
            {
                var (pattern, op) = ClassifyWildcard(token.Value<string>() ?? string.Empty);
                return Leaf(path, op, FilterValue.Of(pattern), ic);
            }

            return Leaf(path, FilterOperator.Eq, Scalar(token), ic);
        }

        var clauses = new List<Filter>();
        foreach (var prop in ((JObject)token).Properties())
        {
            if (prop.Name == "$options") continue;
            clauses.Add(ParseOperator(path, field, prop.Name, prop.Value, ic));
        }
        return clauses.Count switch
        {
            0 => MatchAll,
            1 => clauses[0],
            _ => new AllOf(clauses)
        };
    }

    private static Filter ParseOperator(FieldPath path, ResolvedField field, string op, JToken value, bool ic)
    {
        var coll = field.TargetsCollection;
        switch (op)
        {
            case "$eq": return coll ? Leaf(path, FilterOperator.Has, Scalar(value), ic) : Leaf(path, FilterOperator.Eq, Scalar(value), ic);
            case "$ne": return coll ? new Not(Leaf(path, FilterOperator.Has, Scalar(value), ic)) : Leaf(path, FilterOperator.Ne, Scalar(value), ic);
            case "$gt": return Leaf(path, FilterOperator.Gt, Scalar(value), ic);
            case "$gte": return Leaf(path, FilterOperator.Gte, Scalar(value), ic);
            case "$lt": return Leaf(path, FilterOperator.Lt, Scalar(value), ic);
            case "$lte": return Leaf(path, FilterOperator.Lte, Scalar(value), ic);
            case "$in": return Leaf(path, coll ? FilterOperator.HasAny : FilterOperator.In, SetVal(value, op), ic);
            case "$nin": return Leaf(path, coll ? FilterOperator.HasNone : FilterOperator.Nin, SetVal(value, op), ic);
            case "$all":
                if (!coll) throw new FilterParseException($"'$all' requires a collection field ('{path}').");
                return Leaf(path, FilterOperator.HasAll, SetVal(value, op), ic);
            case "$size":
                if (!coll) throw new FilterParseException($"'$size' requires a collection field ('{path}').");
                return Leaf(path, FilterOperator.Size, Scalar(value), ic);
            case "$exists":
                return Leaf(path, FilterOperator.Exists, FilterValue.Of(value.Type == JTokenType.Boolean ? value.Value<bool>() : true), ic);
            case "$between":
                if (value is not JArray ba || ba.Count != 2)
                    throw new FilterParseException($"'$between' requires a 2-element array ('{path}').");
                return new AllOf(new Filter[]
                {
                    Leaf(path, FilterOperator.Gte, Scalar(ba[0]), ic),
                    Leaf(path, FilterOperator.Lte, Scalar(ba[1]), ic)
                });
            case "$contains":
                return coll ? Leaf(path, FilterOperator.Has, Scalar(value), ic) : Leaf(path, FilterOperator.Contains, Scalar(value), ic);
            default:
                throw new FilterParseException($"Unknown filter operator '{op}' on field '{path}'.");
        }
    }

    private static bool MergeOptions(JObject obj, bool parent)
    {
        if (obj.TryGetValue("$options", out var o) && o is JObject opts
            && opts.TryGetValue("ignoreCase", out var ic) && ic.Type == JTokenType.Boolean)
            return ic.Value<bool>();
        return parent;
    }

    private static (string Pattern, FilterOperator Op) ClassifyWildcard(string s)
    {
        var starts = s.StartsWith('*');
        var ends = s.EndsWith('*');
        if (starts && ends) return (s.Trim('*'), FilterOperator.Contains);
        if (starts) return (s.TrimStart('*'), FilterOperator.EndsWith);
        if (ends) return (s.TrimEnd('*'), FilterOperator.StartsWith);
        return (s, FilterOperator.Eq);
    }

    private static FieldFilter Leaf(FieldPath path, FilterOperator op, FilterValue value, bool ic) => new(path, op, value, ic);

    private static FilterValue Scalar(JToken t) => FilterValue.Of(JsonToClr(t));

    private static FilterValue SetVal(JToken t, string op)
        => t is JArray a ? FilterValue.Many(a.Select(JsonToClr).ToList()) : throw new FilterParseException($"'{op}' requires an array.");

    private static JObject AsObject(JToken t, string op)
        => t as JObject ?? throw new FilterParseException($"'{op}' operands must be objects.");

    private static object? JsonToClr(JToken t) => t.Type switch
    {
        JTokenType.Integer => t.Value<long>(),
        JTokenType.Float => t.Value<double>(),
        JTokenType.Boolean => t.Value<bool>(),
        JTokenType.Date => t.Value<DateTime>(),
        JTokenType.Guid => t.Value<Guid>(),
        JTokenType.Null => null,
        JTokenType.String => t.Value<string>(),
        _ => t.ToString()
    };
}
