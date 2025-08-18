using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;

namespace Sora.Web.Filtering;

/// <summary>
/// Builds a LINQ Expression from a simple JSON filter payload.
/// Supported:
/// - Equality on primitive and string fields: { "name": "leo" }
/// - Wildcards in strings using '*': begins/ends/contains
/// - $and / $or with array operands
/// - $not with object operand
/// - $in: { "status": { "$in": ["a","b"] } }
/// - $exists: { "middleName": { "$exists": true } }
/// Notes: Top-level fields only in v1; relaxed single-quote JSON is accepted.
/// </summary>
public static class JsonFilterBuilder
{
    public sealed class BuildOptions
    {
        public bool IgnoreCase { get; init; } = false;
    }

    public static bool TryBuild<TEntity>(string? json, out Expression<Func<TEntity, bool>>? predicate, out string? error, BuildOptions? options = null)
    {
        predicate = null; error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            predicate = static (TEntity _) => true;
            return true;
        }
        // Tolerate single-quoted JSON from querystrings
        var normalized = NormalizeJson(json!);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(normalized); }
        catch (Exception ex) { error = $"Invalid filter JSON: {ex.Message}"; return false; }

        try
        {
            var param = Expression.Parameter(typeof(TEntity), "e");
            var body = BuildNode<TEntity>(doc.RootElement, param, options ?? new());
            predicate = Expression.Lambda<Func<TEntity, bool>>(body, param);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string NormalizeJson(string input)
    {
        var s = input.Trim();
        // If it looks like single-quoted JSON, convert quotes
        var hasDouble = s.Contains('"');
        var hasSingle = s.Contains('\'');
        if (!hasDouble && hasSingle)
        {
            s = s.Replace('\'', '"');
        }
        // Some clients send filter objects without URL-encoding; attempt to unescape common patterns
        s = Uri.UnescapeDataString(s);
        return s;
    }

    private static Expression BuildNode<TEntity>(JsonElement node, ParameterExpression param, BuildOptions opts)
    {
        return node.ValueKind switch
        {
            JsonValueKind.Object => BuildObject<TEntity>(node, param, opts),
            JsonValueKind.True => Expression.Constant(true),
            JsonValueKind.False => Expression.Constant(false),
            _ => throw new InvalidOperationException("Filter root must be an object")
        };
    }

    private static Expression BuildObject<TEntity>(JsonElement obj, ParameterExpression param, BuildOptions opts)
    {
    // Merge any local $options with parent options
    opts = MergeOptions(obj, opts);

        // Logical operators take precedence
        if (obj.TryGetProperty("$and", out var andNode) && andNode.ValueKind == JsonValueKind.Array)
        {
            Expression? acc = null;
            foreach (var child in andNode.EnumerateArray())
            {
        var e = BuildObject<TEntity>(child, param, opts);
                acc = acc == null ? e : Expression.AndAlso(acc, e);
            }
            return acc ?? Expression.Constant(true);
        }
        if (obj.TryGetProperty("$or", out var orNode) && orNode.ValueKind == JsonValueKind.Array)
        {
            Expression? acc = null;
            foreach (var child in orNode.EnumerateArray())
            {
        var e = BuildObject<TEntity>(child, param, opts);
                acc = acc == null ? e : Expression.OrElse(acc, e);
            }
            return acc ?? Expression.Constant(false);
        }
        if (obj.TryGetProperty("$not", out var notNode) && notNode.ValueKind is JsonValueKind.Object)
        {
        var inner = BuildObject<TEntity>(notNode, param, opts);
            return Expression.Not(inner);
        }

        // Field comparisons (top-level only)
        Expression? result = null;
        foreach (var prop in obj.EnumerateObject())
        {
        if (prop.Name.StartsWith("$")) continue; // skip operators incl. $options
            var member = Expression.PropertyOrField(param, prop.Name);
            var expr = BuildComparison(member, prop.Value, opts);
            result = result == null ? expr : Expression.AndAlso(result, expr);
        }
        return result ?? Expression.Constant(true);
    }

    private static Expression BuildComparison(MemberExpression member, JsonElement value, BuildOptions opts)
    {
        // Support: literal, wildcard string, or operator object
        if (value.ValueKind != JsonValueKind.Object)
        {
            return BuildEquality(member, value, opts);
        }
        // Operator object
        opts = MergeOptions(value, opts);
        Expression? acc = null;
        foreach (var op in value.EnumerateObject())
        {
            if (op.Name == "$options") continue;
            Expression piece = op.Name switch
            {
                "$in" => BuildIn(member, op.Value, opts),
                "$exists" => BuildExists(member, op.Value),
                _ => BuildEquality(member, op.Value, opts)
            };
            acc = acc == null ? piece : Expression.AndAlso(acc, piece);
        }
        return acc ?? Expression.Constant(true);
    }

    private static Expression BuildExists(MemberExpression member, JsonElement value)
    {
        var desired = value.ValueKind == JsonValueKind.True;
        var notNull = Expression.NotEqual(member, Expression.Constant(null, member.Type));
        return desired ? notNull : Expression.Not(notNull);
    }

    private static Expression BuildIn(MemberExpression member, JsonElement arrayNode, BuildOptions opts)
    {
        if (arrayNode.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("$in expects an array");

        // Handle strings and primitives; coerce to member type when possible
        if (member.Type == typeof(string))
        {
            // Coerce to non-null strings to avoid List<string?> nullability mismatch
            var items = arrayNode
                .EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? (e.GetString() ?? string.Empty) : (e.ToString() ?? string.Empty))
                .ToList();
            if (opts.IgnoreCase)
            {
                items = items.Select(s => s.ToLowerInvariant()).ToList();
                var loweredMember = Expression.Call(Expression.Coalesce(member, Expression.Constant(string.Empty)), nameof(string.ToLowerInvariant), Type.EmptyTypes);
                var constExprLower = Expression.Constant(items);
                var containsLower = typeof(Enumerable)
                    .GetMethods()
                    .First(m => m.Name == "Contains" && m.GetParameters().Length == 2)
                    .MakeGenericMethod(typeof(string));
                return Expression.Call(containsLower, constExprLower, loweredMember);
            }
            else
            {
                var constExpr = Expression.Constant(items);
                var contains = typeof(Enumerable)
                    .GetMethods()
                    .First(m => m.Name == "Contains" && m.GetParameters().Length == 2)
                    .MakeGenericMethod(typeof(string));
                return Expression.Call(contains, constExpr, member);
            }
        }
        else
        {
            var list = arrayNode.EnumerateArray().Select(e => JsonToObject(e, member.Type)).ToList();
            var constExpr = Expression.Constant(list);
            var contains = typeof(Enumerable)
                .GetMethods()
                .First(m => m.Name == "Contains" && m.GetParameters().Length == 2)
                .MakeGenericMethod(member.Type);
            return Expression.Call(contains, constExpr, member);
        }
    }

    private static Expression BuildEquality(MemberExpression member, JsonElement value, BuildOptions opts)
    {
        if (member.Type == typeof(string))
        {
            var str = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            var (pattern, mode) = ClassifyPattern(str ?? string.Empty);
            // Receivers for string methods; coalesce only when invoking methods
            Expression receiverSensitive = member; // preserve null semantics for equality
            Expression receiverInsensitive = Expression.Call(Expression.Coalesce(member, Expression.Constant(string.Empty)), nameof(string.ToLower), Type.EmptyTypes);
            var constValue = Expression.Constant(opts.IgnoreCase ? (pattern ?? string.Empty).ToLower() : pattern, typeof(string));
            var constEq = Expression.Constant(opts.IgnoreCase ? (str ?? string.Empty).ToLower() : str, typeof(string));

            // Important: use one-argument overloads for provider compatibility (e.g., Mongo LINQ v3)
            return mode switch
            {
                MatchMode.Equals => opts.IgnoreCase
                    ? (str is null
                        ? Expression.Equal(member, Expression.Constant(null, typeof(string)))
                        : Expression.AndAlso(
                            Expression.NotEqual(member, Expression.Constant(null, typeof(string))),
                            Expression.Equal(receiverInsensitive, constEq)))
                    : Expression.Equal(receiverSensitive, Expression.Constant(str, typeof(string))),
                MatchMode.StartsWith => Expression.Call(opts.IgnoreCase ? receiverInsensitive : Expression.Coalesce(member, Expression.Constant(string.Empty)), nameof(string.StartsWith), Type.EmptyTypes, constValue),
                MatchMode.EndsWith => Expression.Call(opts.IgnoreCase ? receiverInsensitive : Expression.Coalesce(member, Expression.Constant(string.Empty)), nameof(string.EndsWith), Type.EmptyTypes, constValue),
                MatchMode.Contains => Expression.Call(opts.IgnoreCase ? receiverInsensitive : Expression.Coalesce(member, Expression.Constant(string.Empty)), nameof(string.Contains), Type.EmptyTypes, constValue),
                _ => opts.IgnoreCase
                    ? Expression.Equal(receiverInsensitive, constEq)
                    : Expression.Equal(receiverSensitive, Expression.Constant(str, typeof(string)))
            };
        }
        else
        {
            var rhs = JsonToObject(value, member.Type);
            return Expression.Equal(member, Expression.Constant(rhs, member.Type));
        }
    }

    private enum MatchMode { Equals, StartsWith, EndsWith, Contains }
    private static (string Pattern, MatchMode Mode) ClassifyPattern(string input)
    {
        var starts = input.StartsWith("*");
        var ends = input.EndsWith("*");
        if (starts && ends) return (input.Trim('*'), MatchMode.Contains);
        if (starts) return (input.TrimStart('*'), MatchMode.EndsWith);
        if (ends) return (input.TrimEnd('*'), MatchMode.StartsWith);
        return (input, MatchMode.Equals);
    }

    private static object? JsonToObject(JsonElement value, Type targetType)
    {
        try
        {
            if (targetType == typeof(string)) return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            if (targetType == typeof(int) || targetType == typeof(int?)) return value.ValueKind switch { JsonValueKind.Number => value.GetInt32(), JsonValueKind.String => int.Parse(value.GetString()!), _ => 0 };
            if (targetType == typeof(long) || targetType == typeof(long?)) return value.ValueKind switch { JsonValueKind.Number => value.GetInt64(), JsonValueKind.String => long.Parse(value.GetString()!), _ => 0L };
            if (targetType == typeof(bool) || targetType == typeof(bool?)) return value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.String => bool.Parse(value.GetString()!), _ => false };
            if (targetType == typeof(double) || targetType == typeof(double?)) return value.ValueKind switch { JsonValueKind.Number => value.GetDouble(), JsonValueKind.String => double.Parse(value.GetString()!), _ => 0d };

            // Fallback: use System.Text.Json to convert
            var raw = value.GetRawText();
            return JsonSerializer.Deserialize(raw, targetType);
        }
        catch
        {
            // Relaxed coercion: best-effort
            var s = value.ToString();
            return Convert.ChangeType(s, Nullable.GetUnderlyingType(targetType) ?? targetType);
        }
    }

    private static BuildOptions MergeOptions(JsonElement node, BuildOptions parent)
    {
        try
        {
            if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty("$options", out var optsNode) && optsNode.ValueKind == JsonValueKind.Object)
            {
                bool ignoreCase = parent.IgnoreCase;
                if (optsNode.TryGetProperty("ignoreCase", out var ic))
                {
                    if (ic.ValueKind == JsonValueKind.True) ignoreCase = true;
                    else if (ic.ValueKind == JsonValueKind.False) { /* keep current */ }
                }
                if (ignoreCase != parent.IgnoreCase)
                {
                    return new BuildOptions { IgnoreCase = ignoreCase };
                }
            }
        }
        catch
        {
            // ignore option parse errors; keep parent options
        }
        return parent;
    }
}
