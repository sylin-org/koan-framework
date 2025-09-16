using System.Linq.Expressions;

namespace Koan.Data.Abstractions.Vector.Filtering;

// Thin expression -> AST translator for a safe subset.
public static class VectorFilterExpression
{
    public static VectorFilter From<T>(Expression<Func<T, bool>> predicate, Func<MemberExpression, string[]>? pathResolver = null)
    {
        pathResolver ??= DefaultPathResolver;
        return Visit(predicate.Body) ?? throw new NotSupportedException("Expression not supported.");

        VectorFilter? Visit(Expression e)
        {
            switch (e)
            {
                case BinaryExpression be when be.NodeType is ExpressionType.AndAlso or ExpressionType.And:
                    return VectorFilter.And(Visit(be.Left)!, Visit(be.Right)!);
                case BinaryExpression be when be.NodeType is ExpressionType.OrElse or ExpressionType.Or:
                    return VectorFilter.Or(Visit(be.Left)!, Visit(be.Right)!);
                case UnaryExpression ue when ue.NodeType == ExpressionType.Not:
                    return VectorFilter.Not(Visit(ue.Operand)!);
                case BinaryExpression be when IsComparison(be.NodeType):
                    var (path, val) = ExtractPathValue(be.Left, be.Right);
                    var op = be.NodeType switch
                    {
                        ExpressionType.Equal => VectorFilterOperator.Eq,
                        ExpressionType.NotEqual => VectorFilterOperator.Ne,
                        ExpressionType.GreaterThan => VectorFilterOperator.Gt,
                        ExpressionType.GreaterThanOrEqual => VectorFilterOperator.Gte,
                        ExpressionType.LessThan => VectorFilterOperator.Lt,
                        ExpressionType.LessThanOrEqual => VectorFilterOperator.Lte,
                        _ => throw new NotSupportedException()
                    };
                    return new VectorFilterCompare(path, op, val);
                case MethodCallExpression mc when mc.Method.DeclaringType == typeof(string) && mc.Method.Name is "Contains" or "StartsWith":
                    if (mc.Object is not MemberExpression me) throw new NotSupportedException("Method must be on a member");
                    var p = pathResolver(me);
                    var arg = (mc.Arguments[0] as ConstantExpression)?.Value as string ?? throw new NotSupportedException("Requires constant string argument");
                    var pattern = mc.Method.Name == "StartsWith" ? ($"{arg}*") : ($"*{arg}*");
                    return new VectorFilterCompare(p, VectorFilterOperator.Like, pattern);
                default:
                    throw new NotSupportedException($"Unsupported expression: {e.NodeType}");
            }
        }

        static bool IsComparison(ExpressionType t) => t is ExpressionType.Equal or ExpressionType.NotEqual or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual or ExpressionType.LessThan or ExpressionType.LessThanOrEqual;

        (string[] path, object? value) ExtractPathValue(Expression a, Expression b)
        {
            if (a is MemberExpression ma && b is ConstantExpression cb)
                return (pathResolver(ma), cb.Value);
            if (b is MemberExpression mb && a is ConstantExpression ca)
                return (pathResolver(mb), ca.Value);
            throw new NotSupportedException("Only member-to-constant comparisons supported.");
        }

        static string[] DefaultPathResolver(MemberExpression m)
            => new[] { m.Member.Name };
    }
}
