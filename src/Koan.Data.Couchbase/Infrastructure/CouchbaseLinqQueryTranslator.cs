using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using Koan.Data.Core.Optimization;

namespace Koan.Data.Couchbase.Infrastructure;

internal static class CouchbaseLinqQueryTranslator
{
    private const string DocumentAlias = "doc";

    internal static bool TryTranslate<TEntity, TKey>(
        Expression<Func<TEntity, bool>> predicate,
        StorageOptimizationInfo optimization,
        out CouchbaseLinqTranslation translation)
        where TEntity : class
    {
        var visitor = new Translator(optimization);
        try
        {
            var clause = visitor.Translate(predicate.Body);
            translation = new CouchbaseLinqTranslation(clause, visitor.GetParameters());
            return true;
        }
        catch (NotSupportedException)
        {
            translation = default!;
            return false;
        }
    }

    private sealed class Translator
    {
        private readonly StorageOptimizationInfo _optimization;
        private readonly Dictionary<string, object?> _parameters = new(StringComparer.Ordinal);

        public Translator(StorageOptimizationInfo optimization) => _optimization = optimization;

        public IReadOnlyDictionary<string, object?> GetParameters() => _parameters;

        public string Translate(Expression expression)
        {
            expression = StripConvert(expression);
            return expression.NodeType switch
            {
                ExpressionType.Constant => TranslateConstant((ConstantExpression)expression),
                ExpressionType.MemberAccess => TranslateMemberAccess((MemberExpression)expression),
                ExpressionType.Not => $"NOT ({Translate(((UnaryExpression)expression).Operand)})",
                ExpressionType.AndAlso => Combine((BinaryExpression)expression, "AND"),
                ExpressionType.OrElse => Combine((BinaryExpression)expression, "OR"),
                ExpressionType.Equal => Compare((BinaryExpression)expression, "="),
                ExpressionType.NotEqual => Compare((BinaryExpression)expression, "!="),
                ExpressionType.GreaterThan => Compare((BinaryExpression)expression, ">"),
                ExpressionType.GreaterThanOrEqual => Compare((BinaryExpression)expression, ">="),
                ExpressionType.LessThan => Compare((BinaryExpression)expression, "<"),
                ExpressionType.LessThanOrEqual => Compare((BinaryExpression)expression, "<="),
                ExpressionType.Call => TranslateMethodCall((MethodCallExpression)expression),
                _ => throw new NotSupportedException($"Unsupported expression node '{expression.NodeType}'.")
            };
        }

        private static Expression StripConvert(Expression expression)
        {
            while (expression is UnaryExpression unary &&
                   (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked))
            {
                expression = unary.Operand;
            }
            return expression;
        }

        private string Combine(BinaryExpression node, string op)
        {
            var left = Translate(node.Left);
            var right = Translate(node.Right);
            return $"({left} {op} {right})";
        }

        private string Compare(BinaryExpression node, string op)
        {
            var left = StripConvert(node.Left);
            var right = StripConvert(node.Right);

            if (TryGetField(left, out var field))
            {
                if (TryEvaluate(right, out var value))
                {
                    if (value is null)
                    {
                        return op switch
                        {
                            "=" => $"({field} IS NULL)",
                            "!=" => $"({field} IS NOT NULL)",
                            _ => throw new NotSupportedException("NULL comparisons only support equality operators.")
                        };
                    }
                    return $"({field} {op} {AddParameter(NormalizeValue(value))})";
                }
            }

            if (TryGetField(right, out field) && TryEvaluate(left, out var leftValue))
            {
                if (leftValue is null)
                {
                    return op switch
                    {
                        "=" => $"({field} IS NULL)",
                        "!=" => $"({field} IS NOT NULL)",
                        _ => throw new NotSupportedException("NULL comparisons only support equality operators.")
                    };
                }
                var placeholder = AddParameter(NormalizeValue(leftValue));
                var reversedOp = ReverseOperator(op);
                return $"({field} {reversedOp} {placeholder})";
            }

            throw new NotSupportedException("Unable to translate comparison expression to N1QL.");
        }

        private static string ReverseOperator(string op) => op switch
        {
            ">" => "<",
            "<" => ">",
            ">=" => "<=",
            "<=" => ">=",
            _ => op
        };

        private string TranslateMethodCall(MethodCallExpression expression)
        {
            if (expression.Method.DeclaringType == typeof(string))
            {
                return TranslateStringMethod(expression);
            }

            if (expression.Method.DeclaringType == typeof(Enumerable) && expression.Method.Name == nameof(Enumerable.Contains))
            {
                var sourceExpr = expression.Arguments[0];
                var itemExpr = expression.Arguments[1];
                if (!TryGetField(itemExpr, out var field))
                {
                    throw new NotSupportedException("Enumerable.Contains requires an entity member as the item argument.");
                }
                if (!TryEvaluate(sourceExpr, out var valuesObj) || valuesObj is not System.Collections.IEnumerable enumerable)
                {
                    throw new NotSupportedException("Enumerable.Contains requires a resolvable constant collection.");
                }

                var values = enumerable.Cast<object?>().Select(NormalizeValue).ToArray();
                var placeholder = AddParameter(values);
                return $"({field} IN {placeholder})";
            }

            throw new NotSupportedException($"Unsupported method call '{expression.Method.Name}'.");
        }

        private string TranslateStringMethod(MethodCallExpression expression)
        {
            if (expression.Object is null)
            {
                throw new NotSupportedException($"Unsupported static string method '{expression.Method.Name}'.");
            }

            if (!TryGetField(expression.Object, out var field))
            {
                throw new NotSupportedException("String member methods must be invoked on an entity property.");
            }

            if (expression.Arguments.Count != 1 || !TryEvaluate(expression.Arguments[0], out var value))
            {
                throw new NotSupportedException($"String method '{expression.Method.Name}' requires a resolvable constant argument.");
            }

            value = NormalizeValue(value);
            return expression.Method.Name switch
            {
                nameof(string.Contains) => $"(CONTAINS({field}, {AddParameter(value)}) = TRUE)",
                nameof(string.StartsWith) => $"({field} LIKE {AddParameter(ToLikePattern(value, suffix: "%"))})",
                nameof(string.EndsWith) => $"({field} LIKE {AddParameter(ToLikePattern(value, prefix: "%"))})",
                _ => throw new NotSupportedException($"String method '{expression.Method.Name}' is not supported.")
            };
        }

        private static object? ToLikePattern(object? value, string? prefix = null, string? suffix = null)
        {
            var str = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return string.Concat(prefix ?? string.Empty, str, suffix ?? string.Empty);
        }

        private string TranslateConstant(ConstantExpression expression)
        {
            if (expression.Value is bool boolean)
            {
                return boolean ? "TRUE" : "FALSE";
            }
            if (expression.Value is null)
            {
                return "NULL";
            }
            return AddParameter(NormalizeValue(expression.Value));
        }

        private string TranslateMemberAccess(MemberExpression member)
        {
            if (member.Expression is ParameterExpression)
            {
                if (IsIdMember(member.Member.Name))
                {
                    return "META().id";
                }
                return $"{DocumentAlias}.{NormalizeProperty(member.Member.Name)}";
            }

            if (TryEvaluate(member, out var value))
            {
                return AddParameter(NormalizeValue(value));
            }

            throw new NotSupportedException("Unable to translate member access to N1QL.");
        }

        private bool TryGetField(Expression expression, out string field)
        {
            expression = StripConvert(expression);
            if (expression is MemberExpression member)
            {
                var segments = new Stack<string>();
                Expression? current = member;
                while (current is MemberExpression currentMember)
                {
                    segments.Push(NormalizeProperty(currentMember.Member.Name));
                    current = currentMember.Expression;
                }

                if (current is ParameterExpression)
                {
                    if (segments.Count == 1 && IsIdMember(segments.Peek()))
                    {
                        field = "META().id";
                        return true;
                    }

                    field = $"{DocumentAlias}.{string.Join('.', segments)}";
                    return true;
                }
            }

            field = string.Empty;
            return false;
        }

        private string AddParameter(object? value)
        {
            var name = "$p" + _parameters.Count.ToString(CultureInfo.InvariantCulture);
            _parameters[name] = value;
            return name;
        }

        private bool TryEvaluate(Expression expression, out object? value)
        {
            expression = StripConvert(expression);
            switch (expression)
            {
                case ConstantExpression constant:
                    value = constant.Value;
                    return true;
                case MemberExpression member when member.Expression is ConstantExpression constant:
                    value = ExtractMemberValue(constant.Value, member.Member.Name);
                    return true;
                default:
                    try
                    {
                        var lambda = Expression.Lambda(expression);
                        value = lambda.Compile().DynamicInvoke();
                        return true;
                    }
                    catch
                    {
                        value = null;
                        return false;
                    }
            }
        }

        private static object? ExtractMemberValue(object? instance, string memberName)
        {
            if (instance is null)
            {
                return null;
            }

            var type = instance.GetType();
            var field = type.GetField(memberName);
            if (field is not null)
            {
                return field.GetValue(instance);
            }

            var property = type.GetProperty(memberName);
            if (property is not null)
            {
                return property.GetValue(instance);
            }

            return null;
        }

        private static object? NormalizeValue(object? value)
        {
            return value switch
            {
                Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
                Enum enumeration => enumeration.ToString(),
                DateTime dt when dt.Kind == DateTimeKind.Unspecified => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
                _ => value
            };
        }

        private bool IsIdMember(string memberName)
            => string.Equals(memberName, _optimization.IdPropertyName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(memberName, "Id", StringComparison.OrdinalIgnoreCase);

        private static string NormalizeProperty(string property)
            => property[..1].ToLowerInvariant() + property[1..];
    }
}

internal readonly record struct CouchbaseLinqTranslation(string WhereClause, IReadOnlyDictionary<string, object?> Parameters);
