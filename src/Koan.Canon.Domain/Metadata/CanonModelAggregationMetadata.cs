using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Koan.Canon.Domain.Annotations;

namespace Koan.Canon.Domain.Metadata;

/// <summary>
/// Describes aggregation behaviour declared on a canonical model via attributes.
/// </summary>
public sealed class CanonModelAggregationMetadata
{
    private static readonly ConcurrentDictionary<Type, CanonModelAggregationMetadata> Cache = new();

    private CanonModelAggregationMetadata(
        Type modelType,
        IReadOnlyList<PropertyInfo> keyProperties,
        IReadOnlyDictionary<PropertyInfo, AggregationPolicyKind> policyByProperty,
        IReadOnlyDictionary<string, AggregationPolicyKind> policyByName,
        bool auditEnabled)
    {
        ModelType = modelType;
        KeyProperties = keyProperties;
        PolicyByProperty = policyByProperty;
        PolicyByName = policyByName;
        AggregationKeyNames = keyProperties.Select(static property => property.Name).ToArray();
        AuditEnabled = auditEnabled;
    }

    /// <summary>
    /// Canonical model CLR type.
    /// </summary>
    public Type ModelType { get; }

    /// <summary>
    /// Properties that compose the aggregation key, in declaration order.
    /// </summary>
    public IReadOnlyList<PropertyInfo> KeyProperties { get; }

    /// <summary>
    /// Property name list for serialization and diagnostics.
    /// </summary>
    public IReadOnlyList<string> AggregationKeyNames { get; }

    /// <summary>
    /// Policies keyed by reflected property metadata.
    /// </summary>
    public IReadOnlyDictionary<PropertyInfo, AggregationPolicyKind> PolicyByProperty { get; }

    /// <summary>
    /// Policies keyed by property name for serialization.
    /// </summary>
    public IReadOnlyDictionary<string, AggregationPolicyKind> PolicyByName { get; }

    /// <summary>
    /// Indicates whether auditing is enabled for the canonical type.
    /// </summary>
    public bool AuditEnabled { get; }

    /// <summary>
    /// Retrieves metadata for the specified canonical model type.
    /// </summary>
    public static CanonModelAggregationMetadata For(Type modelType)
    {
        if (modelType is null)
        {
            throw new ArgumentNullException(nameof(modelType));
        }

        return Cache.GetOrAdd(modelType, Create);
    }

    /// <summary>
    /// Retrieves metadata for the specified canonical model type.
    /// </summary>
    public static CanonModelAggregationMetadata For<TModel>()
        where TModel : class
        => For(typeof(TModel));

    /// <summary>
    /// Attempts to retrieve a policy for the provided property metadata.
    /// </summary>
    public bool TryGetPolicy(PropertyInfo property, out AggregationPolicyKind kind)
    {
        if (property is null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        return PolicyByProperty.TryGetValue(property, out kind);
    }

    private static CanonModelAggregationMetadata Create(Type modelType)
    {
        var properties = modelType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (properties.Length == 0)
        {
            throw new InvalidOperationException($"Canonical entity '{modelType.Name}' does not define any discoverable properties.");
        }

        var orderedProperties = properties
            .Where(static property => property.GetIndexParameters().Length == 0)
            .ToArray();

        var keyProperties = orderedProperties
            .Where(static property => property.IsDefined(typeof(AggregationKeyAttribute), inherit: true))
            .OrderBy(static property => property.MetadataToken)
            .ToArray();

        if (keyProperties.Length == 0)
        {
            throw new InvalidOperationException($"Canonical entity '{modelType.Name}' must declare at least one [AggregationKey] property.");
        }

        ValidateKeyProperties(keyProperties);

        var policyPairs = new Dictionary<PropertyInfo, AggregationPolicyKind>();
        foreach (var property in orderedProperties)
        {
            var attribute = property.GetCustomAttribute<AggregationPolicyAttribute>(inherit: true);
            if (attribute is null)
            {
                continue;
            }

            policyPairs[property] = attribute.Kind;
        }

        var policyByName = policyPairs.ToDictionary(static pair => pair.Key.Name, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var auditEnabled = modelType.GetCustomAttribute<CanonAttribute>(inherit: true)?.Audit ?? false;
        return new CanonModelAggregationMetadata(modelType, keyProperties, policyPairs, policyByName, auditEnabled);
    }

    private static void ValidateKeyProperties(IReadOnlyList<PropertyInfo> keyProperties)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < keyProperties.Count; i++)
        {
            var property = keyProperties[i];
            if (!property.CanRead)
            {
                throw new InvalidOperationException($"Aggregation key property '{property.Name}' on '{property.DeclaringType?.Name}' must have a getter.");
            }

            if (property.GetGetMethod(nonPublic: true) is null)
            {
                throw new InvalidOperationException($"Aggregation key property '{property.Name}' on '{property.DeclaringType?.Name}' must expose a getter.");
            }

            if (!set.Add(property.Name))
            {
                throw new InvalidOperationException($"Duplicate aggregation key property '{property.Name}' detected on '{property.DeclaringType?.Name}'.");
            }
        }
    }
}
