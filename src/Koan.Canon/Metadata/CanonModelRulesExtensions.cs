using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Koan.Canon;

/// <summary>
/// Convenience helpers for working with <see cref="CanonModelRules"/> in a strongly typed manner.
/// </summary>
public static class CanonModelRulesExtensions
{
    /// <summary>
    /// Attempts to locate an aggregation policy descriptor for the specified property selector.
    /// </summary>
    public static bool TryGetRule<TModel, TValue>(
        this CanonModelRules metadata,
        Expression<Func<TModel, TValue>> propertySelector,
        out ReconcileRule descriptor)
        where TModel : class
    {
        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (propertySelector is null)
        {
            throw new ArgumentNullException(nameof(propertySelector));
        }

        EnsureCompatibleModel(metadata, typeof(TModel));
        var property = ResolveProperty(propertySelector.Body);
        return metadata.TryGetRule(property, out descriptor);
    }

    /// <summary>
    /// Retrieves an aggregation policy descriptor using a property selector or returns <c>null</c> when not declared.
    /// </summary>
    public static ReconcileRule? GetRuleOrDefault<TModel, TValue>(
        this CanonModelRules metadata,
        Expression<Func<TModel, TValue>> propertySelector)
        where TModel : class
    {
        return metadata.TryGetRule(propertySelector, out var descriptor) ? descriptor : null;
    }

    /// <summary>
    /// Retrieves a required aggregation policy descriptor using a property selector.
    /// </summary>
    public static ReconcileRule GetRequiredRule<TModel, TValue>(
        this CanonModelRules metadata,
        Expression<Func<TModel, TValue>> propertySelector)
        where TModel : class
    {
        if (metadata.TryGetRule(propertySelector, out var descriptor))
        {
            return descriptor;
        }

        var property = ResolveProperty(propertySelector.Body);
        throw new KeyNotFoundException($"Canonical entity '{metadata.ModelType.Name}' does not declare a reconcile rule for property '{property.Name}'.");
    }

    private static void EnsureCompatibleModel(CanonModelRules metadata, Type requestedType)
    {
        if (metadata.ModelType == requestedType)
        {
            return;
        }

        if (metadata.ModelType.IsAssignableFrom(requestedType) || requestedType.IsAssignableFrom(metadata.ModelType))
        {
            return;
        }

        throw new InvalidOperationException($"Aggregation metadata for '{metadata.ModelType.Name}' cannot be used with model type '{requestedType.Name}'.");
    }

    private static PropertyInfo ResolveProperty(Expression expression)
    {
        switch (expression)
        {
            case UnaryExpression unary when unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked:
                return ResolveProperty(unary.Operand);
            case MemberExpression member when member.Member is PropertyInfo property:
                return property;
            default:
                throw new ArgumentException("Aggregation policy selectors must reference a property expression.", nameof(expression));
        }
    }
}
