using System.Linq.Expressions;
using System.Reflection;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Mapping.Composition;

internal static class MappingExpression
{
    public static (MappingPath Path, Type Type) PropertyPath<T, TValue>(Expression<Func<T, TValue>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        Expression current = expression.Body;
        while (current is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
            current = unary.Operand;

        var members = new Stack<PropertyInfo>();
        while (current is MemberExpression member)
        {
            if (member.Member is not PropertyInfo property)
                throw new ArgumentException("A mapping expression may contain public properties only.", nameof(expression));
            if (property.GetIndexParameters().Length != 0)
                throw new ArgumentException("A mapping expression cannot select an indexer.", nameof(expression));
            members.Push(property);
            current = member.Expression
                ?? throw new ArgumentException("A mapping expression must begin at its lambda parameter.", nameof(expression));
        }

        if (current != expression.Parameters[0] || members.Count == 0)
            throw new ArgumentException("Select one property path rooted at the lambda parameter.", nameof(expression));

        return (MappingPath.Of(members.Select(static property => property.Name).ToArray()), typeof(TValue));
    }
}
