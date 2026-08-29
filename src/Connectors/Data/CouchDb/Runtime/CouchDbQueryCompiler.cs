using System.Text.RegularExpressions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Connector.CouchDb.Runtime;

/// <summary>
/// The Filter AST as Mango. Field paths resolve like the exemplar's: identity to <c>_id</c>,
/// framework-managed discriminators to their verbatim storage name, everything else to the camelCase
/// document path. Only the operators the adapter declares in its <c>FilterSupport</c> ever arrive —
/// an undeclared operator reaching this compiler is a planning defect and refuses by name.
/// </summary>
internal sealed class CouchDbQueryCompiler<TEntity, TKey>(string identityName)
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public JObject Selector(Filter filter) => Visit(filter);

    private static JToken Token(object? value) => value switch
    {
        null => JValue.CreateNull(),
        JToken token => token,
        _ => JToken.FromObject(value, Newtonsoft.Json.JsonSerializer.Create(new Newtonsoft.Json.JsonSerializerSettings
        {
            DateParseHandling = Newtonsoft.Json.DateParseHandling.None
        }))
    };

    private JObject Visit(Filter filter) => filter switch
    {
        AllOf all => all.Operands.Count == 0
            ? [] // an empty conjunction matches everything
            : new JObject { ["$and"] = new JArray(all.Operands.Select(Visit)) },
        AnyOf any => any.Operands.Count == 0
            ? Impossible()
            : new JObject { ["$or"] = new JArray(any.Operands.Select(Visit)) },
        Not not => new JObject { ["$not"] = Visit(not.Operand) },
        FieldFilter field => Field(field),
        _ => throw new NotSupportedException(
            $"Filter node '{filter.GetType().Name}' is not translatable to a Mango selector.")
    };

    /// <summary>A selector no document can match, spelled without inventing an operator.</summary>
    private static JObject Impossible() => new()
    {
        ["$and"] = new JArray(
            new JObject { [Constants_Identity()] = new JObject { ["$gt"] = null } },
            new JObject { [Constants_Identity()] = new JObject { ["$lt"] = null } })
    };

    private static string Constants_Identity() => Infrastructure.Constants.Storage.Identity;

    private JObject Field(FieldFilter f)
    {
        var resolved = FieldPathResolver.Resolve(typeof(TEntity), f.Field);
        // JSON filters bind member names case-insensitively, but the document stores one spelling:
        // compile from the canonical resolved path, exactly as the relational translator does.
        var name = FieldName(resolved.CanonicalPath ?? f.Field, resolved, identityName);
        var comparison = new JObject();

        if (resolved.TargetsCollection)
        {
            switch (f.Operator)
            {
                case FilterOperator.Has:
                    comparison["$all"] = new JArray(Values(f, resolved, single: true).Select(Token));
                    break;
                case FilterOperator.HasAny:
                    comparison["$in"] = new JArray(Values(f, resolved).Select(Token));
                    break;
                case FilterOperator.HasAll:
                    comparison["$all"] = new JArray(Values(f, resolved).Select(Token));
                    break;
                case FilterOperator.HasNone:
                    comparison["$not"] = new JObject { ["$in"] = new JArray(Values(f, resolved).Select(Token)) };
                    break;
                case FilterOperator.Size:
                    comparison["$size"] = Token(ScalarValue(f.Value, typeof(int)));
                    break;
                default:
                    throw new NotSupportedException(
                        $"Operator '{f.Operator}' is not declared for collection field '{f.Field}' on CouchDB; " +
                        "the adapter's FilterSupport excludes it and the coordinator should not have sent it.");
            }
            return new JObject { [name] = comparison };
        }

        switch (f.Operator)
        {
            case FilterOperator.Eq:
            {
                var raw = ScalarValue(f.Value, resolved.ComparableType);
                if (raw is null) comparison["$eq"] = JValue.CreateNull();
                else return new JObject { [name] = Token(raw) };
                break;
            }
            case FilterOperator.Ne:
            {
                var raw = ScalarValue(f.Value, resolved.ComparableType);
                comparison["$ne"] = Token(raw);
                if (raw is null) comparison["$exists"] = true;
                break;
            }
            case FilterOperator.Gt: comparison["$gt"] = Token(ScalarValue(f.Value, resolved.ComparableType)); break;
            case FilterOperator.Gte: comparison["$gte"] = Token(ScalarValue(f.Value, resolved.ComparableType)); break;
            case FilterOperator.Lt: comparison["$lt"] = Token(ScalarValue(f.Value, resolved.ComparableType)); break;
            case FilterOperator.Lte: comparison["$lte"] = Token(ScalarValue(f.Value, resolved.ComparableType)); break;
            case FilterOperator.In: comparison["$in"] = new JArray(Values(f, resolved).Select(Token)); break;
            case FilterOperator.Nin: comparison["$nin"] = new JArray(Values(f, resolved).Select(Token)); break;
            case FilterOperator.Exists:
                // The floor reads "exists" as a non-null value, and this store writes nulls as JSON
                // nulls — a bare $exists would count them, so non-null is part of the selector.
                comparison["$exists"] = ScalarBool(f.Value);
                if (ScalarBool(f.Value)) comparison["$ne"] = null;
                else comparison["$eq"] = null;
                break;
            case FilterOperator.StartsWith: comparison["$regex"] = "^" + RegexSafe(LikeBody(f)); break;
            case FilterOperator.EndsWith: comparison["$regex"] = RegexSafe(LikeBody(f)) + "$"; break;
            case FilterOperator.Contains: comparison["$regex"] = RegexSafe(LikeBody(f)); break;
            default:
                throw new NotSupportedException(
                    $"Operator '{f.Operator}' is not declared for scalar field '{f.Field}' on CouchDB; " +
                    "the adapter's FilterSupport excludes it and the coordinator should not have sent it.");
        }
        return new JObject { [name] = comparison };
    }

    private static string LikeBody(FieldFilter f) => f.Value switch
    {
        FilterValue.Scalar s => s.Value?.ToString() ?? string.Empty,
        _ => string.Empty
    };

    /// <summary>
    /// The LIKE body already carries SQL-escaped wildcards semantics; as a regex it only needs the
    /// metacharacters rendered literal, with the LIKE escapes folded back to their plain characters.
    /// </summary>
    private static string RegexSafe(string pattern) =>
        Regex.Escape(pattern).Replace("\\%", "%", StringComparison.Ordinal).Replace("\\_", "_", StringComparison.Ordinal);

    private static string FieldName(FieldPath path, ResolvedField resolved, string identity) 
    {
        if (resolved.IsManaged) return CouchDbEntityPlan<TEntity, TKey>.ManagedFieldPath(
            resolved.StorageName ?? path.ToString());
        if (path.Segments.Count == 1 && string.Equals(path.Leaf, identity, StringComparison.Ordinal))
            return Infrastructure.Constants.Storage.Identity;
        return string.Join('.', path.Segments.Select(static segment => Camel(segment)));
    }

    private static object?[] Values(FieldFilter f, ResolvedField field, bool single = false)
    {
        var raw = f.Value switch
        {
            FilterValue.Set set => set.Values,
            FilterValue.Scalar s => new[] { s.Value },
            _ => Array.Empty<object?>()
        };
        var converted = raw.Select(value => FilterValueConverter.Convert(value, field.ComparableType)).ToArray();
        return single && converted.Length > 0 ? [converted[0]] : converted;
    }

    private static object? ScalarValue(FilterValue value, Type targetType) =>
        FilterValueConverter.Convert(
            value switch
            {
                FilterValue.Scalar s => s.Value,
                FilterValue.Set set => set.Values.Count > 0 ? set.Values[0] : null,
                _ => null
            }, targetType);

    private static bool ScalarBool(FilterValue value) => value switch
    {
        FilterValue.Scalar s => s.Value is true or "true",
        _ => true
    };

    internal static string Camel(string value) =>
        string.IsNullOrEmpty(value) || char.IsLower(value[0])
            ? value
            : char.ToLowerInvariant(value[0]) + value[1..];
}
