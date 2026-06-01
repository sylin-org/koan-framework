using Newtonsoft.Json.Linq;

namespace Koan.Data.Abstractions.Filtering;

/// <summary>
/// Parses the Koan JSON filter DSL into the unified <see cref="Filter"/> AST for <b>schemaless
/// vector metadata</b> (AI-0036 §10 / DATA-0097 P1). The schemaless twin of
/// <see cref="JsonFilterParser"/>: it accepts the same Mongo-flavoured surface
/// (<c>$and/$or/$not/$nor</c>, <c>$eq/$ne/$gt/$gte/$lt/$lte</c>, <c>$in/$nin/$exists/$between/$contains</c>,
/// wildcard strings, <c>$options.ignoreCase</c>) so a filter authored for <c>?filter=</c> reads the
/// same here, and lowers redundant forms identically (<c>$nor</c> → <c>Not(AnyOf)</c>, <c>$between</c>
/// → <c>AllOf(Gte,Lte)</c>, leading/trailing wildcards → StartsWith/EndsWith/Contains).
/// </summary>
/// <remarks>
/// Two forced differences from <see cref="JsonFilterParser"/>, both because vector metadata has no
/// CLR type to resolve against:
/// <list type="bullet">
/// <item>No <c>FieldPathResolver</c> / <c>FilterValueConverter</c>: a field is a metadata key
/// (<see cref="FieldPath.Of(string[])"/> on the dotted name) and scalars keep their JSON-normalized
/// CLR type (long/double/bool/string/DateTime/Guid) — the coercion contract (AI-0036 §10.2.6).</item>
/// <item>Collection-ness cannot be inferred, so the scalar keywords keep scalar meaning (<c>$in</c> →
/// <see cref="FilterOperator.In"/>, not HasAny) and array-valued metadata uses the <b>explicit</b>
/// collection keywords <c>$has/$hasAny/$hasAll/$hasNone/$size</c>. (The CLR-typed
/// <see cref="JsonFilterParser"/> infers these from the field type; the schemaless reader requires
/// intent.)</item>
/// </list>
/// Fail-loud, never silently widen: null input is "no filter" (returns null → full kNN); a supplied
/// but malformed/unknown-operator/interior-wildcard filter throws <see cref="FilterParseException"/>.
/// An already-built <see cref="Filter"/> passes through unchanged (the facade escape hatch).
/// </remarks>
public static class VectorFilterReader
{
    /// <summary>
    /// Reads a metadata filter. Returns null only for genuinely-absent input (null); throws
    /// <see cref="FilterParseException"/> when a filter is supplied but invalid.
    /// </summary>
    public static Filter? Read(object? input, FilterParseOptions? options = null)
    {
        switch (input)
        {
            case null:
                return null;                       // no filter -> full kNN
            case Filter f:
                return f;                          // already built (facade passthrough; legacy ParseOrThrow parity)
        }

        JToken token;
        try
        {
            token = input switch
            {
                JToken jt => jt,
                string s => string.IsNullOrWhiteSpace(s) ? JValue.CreateNull() : JToken.Parse(Normalize(s)),
                _ => JToken.FromObject(input)
            };
        }
        catch (Exception ex)
        {
            throw new FilterParseException($"Invalid vector filter JSON: {ex.Message}", ex);
        }

        if (token.Type is JTokenType.Null or JTokenType.Undefined) return null;
        if (token is not JObject obj)
            throw new FilterParseException("Vector filter must be a JSON object (or null).");

        return ParseObject(obj, (options ?? new FilterParseOptions()).IgnoreCase);
    }

    private static string Normalize(string input)
    {
        var s = input.Trim();
        if (!s.Contains('"') && s.Contains('\'')) s = s.Replace('\'', '"');
        return s;
    }

    private static Filter ParseObject(JObject obj, bool ignoreCase)
    {
        ignoreCase = MergeOptions(obj, ignoreCase);

        if (obj.TryGetValue("$and", out var andTok) && andTok is JArray andArr)
            return new AllOf(andArr.Select(c => ParseObject(AsObject(c, "$and"), ignoreCase)).ToList());
        if (obj.TryGetValue("$or", out var orTok) && orTok is JArray orArr)
            return new AnyOf(orArr.Select(c => ParseObject(AsObject(c, "$or"), ignoreCase)).ToList());
        if (obj.TryGetValue("$nor", out var norTok) && norTok is JArray norArr)
            return new Not(new AnyOf(norArr.Select(c => ParseObject(AsObject(c, "$nor"), ignoreCase)).ToList()));
        if (obj.TryGetValue("$not", out var notTok) && notTok is JObject notObj)
            return new Not(ParseObject(notObj, ignoreCase));

        var clauses = new List<Filter>();
        foreach (var prop in obj.Properties())
        {
            if (prop.Name.StartsWith('$')) continue; // $options handled inline
            var path = FieldPath.Of(prop.Name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            clauses.Add(ParseComparison(path, prop.Value, ignoreCase));
        }

        return clauses.Count switch
        {
            0 => JsonFilterParser.MatchAll,
            1 => clauses[0],
            _ => new AllOf(clauses)
        };
    }

    private static Filter ParseComparison(FieldPath path, JToken token, bool ignoreCase)
    {
        if (token.Type != JTokenType.Object)
        {
            if (token.Type == JTokenType.String)
            {
                var (pattern, op) = ClassifyWildcard(token.Value<string>() ?? string.Empty, path);
                return Leaf(path, op, FilterValue.Of(pattern), ignoreCase);
            }
            return Leaf(path, FilterOperator.Eq, Scalar(token), ignoreCase);
        }

        var clauses = new List<Filter>();
        foreach (var prop in ((JObject)token).Properties())
        {
            if (prop.Name == "$options") continue;
            clauses.Add(ParseOperator(path, prop.Name, prop.Value, ignoreCase));
        }
        return clauses.Count switch
        {
            0 => JsonFilterParser.MatchAll,
            1 => clauses[0],
            _ => new AllOf(clauses)
        };
    }

    private static Filter ParseOperator(FieldPath path, string op, JToken value, bool ic)
    {
        switch (op.ToLowerInvariant())
        {
            case "$eq": return Leaf(path, FilterOperator.Eq, Scalar(value), ic);
            case "$ne": return Leaf(path, FilterOperator.Ne, Scalar(value), ic);
            case "$gt": return Leaf(path, FilterOperator.Gt, Scalar(value), ic);
            case "$gte": return Leaf(path, FilterOperator.Gte, Scalar(value), ic);
            case "$lt": return Leaf(path, FilterOperator.Lt, Scalar(value), ic);
            case "$lte": return Leaf(path, FilterOperator.Lte, Scalar(value), ic);
            case "$in": return Leaf(path, FilterOperator.In, SetVal(value, op), ic);
            case "$nin": return Leaf(path, FilterOperator.Nin, SetVal(value, op), ic);
            case "$exists":
                return Leaf(path, FilterOperator.Exists, FilterValue.Of(value.Type == JTokenType.Boolean ? value.Value<bool>() : true), ic);
            case "$between":
                if (value is not JArray ba || ba.Count != 2)
                    throw new FilterParseException($"'$between' requires a 2-element array ('{path}').");
                return new AllOf(new Filter[] { Leaf(path, FilterOperator.Gte, Scalar(ba[0]), ic), Leaf(path, FilterOperator.Lte, Scalar(ba[1]), ic) });
            case "$contains": return Leaf(path, FilterOperator.Contains, Scalar(value), ic);
            // explicit collection operators (schemaless: type cannot be inferred)
            case "$has": return Leaf(path, FilterOperator.Has, Scalar(value), ic);
            case "$hasany": return Leaf(path, FilterOperator.HasAny, SetVal(value, op), ic);
            case "$all":
            case "$hasall": return Leaf(path, FilterOperator.HasAll, SetVal(value, op), ic);
            case "$hasnone": return Leaf(path, FilterOperator.HasNone, SetVal(value, op), ic);
            case "$size": return Leaf(path, FilterOperator.Size, Scalar(value), ic);
            default:
                throw new FilterParseException($"Unknown vector filter operator '{op}' on field '{path}'.");
        }
    }

    private static bool MergeOptions(JObject obj, bool parent)
    {
        if (obj.TryGetValue("$options", out var o) && o is JObject opts
            && opts.TryGetValue("ignoreCase", out var ic) && ic.Type == JTokenType.Boolean)
            return ic.Value<bool>();
        return parent;
    }

    private static (string Pattern, FilterOperator Op) ClassifyWildcard(string s, FieldPath path)
    {
        var starts = s.StartsWith('*');
        var ends = s.EndsWith('*');
        var core = s.Trim('*');
        // AI-0036 §10.2.4: no lossy coercion. An interior wildcard has no exact unified target.
        if (core.Contains('*'))
            throw new FilterParseException(
                $"Vector filter pattern '{s}' on '{path}' has an interior wildcard, which has no exact " +
                $"representation; only leading/trailing '*' (StartsWith/EndsWith/Contains) are supported.");
        if (starts && ends) return (core, FilterOperator.Contains);
        if (starts) return (core, FilterOperator.EndsWith);
        if (ends) return (core, FilterOperator.StartsWith);
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
