using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Koan.Canon.Domain.Metadata;

/// <summary>
/// Convenience helpers for working with <see cref="CanonModelAggregationMetadata"/> in a strongly typed manner.
/// </summary>
public static class CanonModelAggregationMetadataExtensions
{
    /// <summary>
    /// Attempts to locate an aggregation policy descriptor for the specified property selector.
    /// </summary>
    public static bool TryGetPolicy<TModel, TValue>(
        this CanonModelAggregationMetadata metadata,
        Expression<Func<TModel, TValue>> propertySelector,
        out AggregationPolicyDescriptor descriptor)
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
        return metadata.TryGetPolicy(property, out descriptor);
    }

    /// <summary>
    /// Retrieves an aggregation policy descriptor using a property selector or returns <c>null</c> when not declared.
    /// </summary>
    public static AggregationPolicyDescriptor? GetPolicyOrDefault<TModel, TValue>(
        this CanonModelAggregationMetadata metadata,
        Expression<Func<TModel, TValue>> propertySelector)
        where TModel : class
    {
        return metadata.TryGetPolicy(propertySelector, out var descriptor) ? descriptor : null;
    }

    /// <summary>
    /// Retrieves a required aggregation policy descriptor using a property selector.
    /// </summary>
    public static AggregationPolicyDescriptor GetRequiredPolicy<TModel, TValue>(
        this CanonModelAggregationMetadata metadata,
        Expression<Func<TModel, TValue>> propertySelector)
        where TModel : class
    {
        if (metadata.TryGetPolicy(propertySelector, out var descriptor))
        {
            return descriptor;
        }

        var property = ResolveProperty(propertySelector.Body);
        throw new KeyNotFoundException($"Canonical entity '{metadata.ModelType.Name}' does not declare an aggregation policy for property '{property.Name}'.");
    }

    private static void EnsureCompatibleModel(CanonModelAggregationMetadata metadata, Type requestedType)
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
