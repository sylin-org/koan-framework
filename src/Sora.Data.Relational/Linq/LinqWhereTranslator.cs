using System.Linq.Expressions;

namespace Sora.Data.Relational.Linq;

/// <summary>
/// Minimal Expression visitor that translates a restricted subset of LINQ predicates
/// to a SQL WHERE clause with parameter placeholders and a parameters collection.
/// Supported operators: <c>==</c>, <c>!=</c>, <c>&gt;</c>, <c>&gt;=</c>, <c>&lt;</c>, <c>&lt;=</c>, logical <c>&amp;&amp;</c>, <c>||</c>, unary <c>!</c>,
/// null checks, and string methods: <see cref="string.StartsWith(string)"/>, <see cref="string.EndsWith(string)"/>, <see cref="string.Contains(string)"/>.
/// Fallback: throws <see cref="NotSupportedException"/> so callers can decide to partially push down or fallback to in-memory.
/// </summary>
public sealed class LinqWhereTranslator<TEntity>
{
    private readonly ILinqSqlDialect _dialect;
    private readonly List<object?> _parameters = new();

    public LinqWhereTranslator(ILinqSqlDialect dialect) => _dialect = dialect;

    public (string whereSql, IReadOnlyList<object?> parameters) Translate(Expression<Func<TEntity, bool>> predicate)
    {
        var sql = Visit(predicate.Body);
        return (sql, _parameters);
    }

    private string Visit(Expression ex)
    {
        return ex.NodeType switch
        {
            ExpressionType.AndAlso => Binary((BinaryExpression)ex, "AND"),
            ExpressionType.OrElse => Binary((BinaryExpression)ex, "OR"),
            ExpressionType.Equal => Compare((BinaryExpression)ex, "="),
            ExpressionType.NotEqual => Compare((BinaryExpression)ex, "<>"),
            ExpressionType.GreaterThan => Compare((BinaryExpression)ex, ">"),
            ExpressionType.GreaterThanOrEqual => Compare((BinaryExpression)ex, ">="),
            ExpressionType.LessThan => Compare((BinaryExpression)ex, "<"),
            ExpressionType.LessThanOrEqual => Compare((BinaryExpression)ex, "<="),
            ExpressionType.Not => $"(NOT {Visit(((UnaryExpression)ex).Operand)})",
            ExpressionType.Call => Method((MethodCallExpression)ex),
            _ => throw new NotSupportedException($"Expression of type {ex.NodeType} is not supported.")
        };
    }

    private static string ColumnName(Expression ex)
    {
        if (ex is MemberExpression me && me.Expression is ParameterExpression)
        {
            return me.Member.Name; // property name; dialect quoting happens outside
        }
        throw new NotSupportedException("Only direct member access on the parameter is supported.");
    }

    private static bool IsNullConstant(Expression e) => e is ConstantExpression c && c.Value is null;

    private string Compare(BinaryExpression be, string op)
    {
        // Support null comparisons specially to avoid @p = NULL
        if (IsNullConstant(be.Right))
        {
            var col = ColumnName(be.Left);
            return op == "=" ? $"({_dialect.QuoteIdent(col)} IS NULL)" : $"({_dialect.QuoteIdent(col)} IS NOT NULL)";
        }
        if (IsNullConstant(be.Left))
        {
            var col = ColumnName(be.Right);
            return op == "=" ? $"({_dialect.QuoteIdent(col)} IS NULL)" : $"({_dialect.QuoteIdent(col)} IS NOT NULL)";
        }

        var leftCol = ColumnName(be.Left);
        var paramIndex = _parameters.Count;
        var value = Evaluate(be.Right);
        _parameters.Add(value);
        return $"({_dialect.QuoteIdent(leftCol)} {op} {_dialect.Parameter(paramIndex)})";
    }

    private static object? Evaluate(Expression e)
    {
        if (e is ConstantExpression c) return c.Value;
        var lambda = Expression.Lambda(e);
        var compiled = lambda.Compile();
        return compiled.DynamicInvoke();
    }

    private string Binary(BinaryExpression be, string op)
    {
        var left = Visit(be.Left);
        var right = Visit(be.Right);
        return $"({left} {op} {right})";
    }

    private string Method(MethodCallExpression m)
    {
        if (m.Object is MemberExpression me && me.Expression is ParameterExpression && m.Method.DeclaringType == typeof(string))
        {
            var col = _dialect.QuoteIdent(me.Member.Name);
            var raw = m.Arguments.Count == 1 ? Evaluate(m.Arguments[0])?.ToString() ?? string.Empty : string.Empty;
            var arg = _dialect.EscapeLike(raw);
            var idx = _parameters.Count;
            switch (m.Method.Name)
            {
                case nameof(string.StartsWith):
                    _parameters.Add(arg + "%");
                    return $"({col} LIKE {_dialect.Parameter(idx)} ESCAPE '\\')";
                case nameof(string.EndsWith):
                    _parameters.Add("%" + arg);
                    return $"({col} LIKE {_dialect.Parameter(idx)} ESCAPE '\\')";
                case nameof(string.Contains):
                    _parameters.Add("%" + arg + "%");
                    return $"({col} LIKE {_dialect.Parameter(idx)} ESCAPE '\\')";
            }
        }
        throw new NotSupportedException($"Method call {m.Method.Name} is not supported.");
    }
}
