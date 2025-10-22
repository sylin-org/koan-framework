using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
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
        IReadOnlyDictionary<PropertyInfo, AggregationPolicyDescriptor> policyByProperty,
        IReadOnlyDictionary<string, AggregationPolicyKind> policyByName,
        IReadOnlyDictionary<string, AggregationPolicyDescriptor> policyDescriptorsByName,
        bool auditEnabled)
    {
        ModelType = modelType;
        KeyProperties = keyProperties;
        PolicyByProperty = policyByProperty;
        PolicyByName = policyByName;
        PolicyDescriptorsByName = policyDescriptorsByName;
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
    public IReadOnlyDictionary<PropertyInfo, AggregationPolicyDescriptor> PolicyByProperty { get; }

    /// <summary>
    /// Policies keyed by property name for serialization.
    /// </summary>
    public IReadOnlyDictionary<string, AggregationPolicyKind> PolicyByName { get; }

    /// <summary>
    /// Detailed policy descriptors keyed by property name.
    /// </summary>
    public IReadOnlyDictionary<string, AggregationPolicyDescriptor> PolicyDescriptorsByName { get; }

    /// <summary>
    /// Indicates whether auditing is enabled for the canonical type.
    /// </summary>
    public bool AuditEnabled { get; }

    /// <summary>
    /// Retrieves metadata for the specified canonical model type.
    /// </summary>
    public static CanonModelAggregationMetadata For([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)] Type modelType)
    {
        if (modelType is null)
        {
            throw new ArgumentNullException(nameof(modelType));
        }

        if (Cache.TryGetValue(modelType, out var cached))
        {
            return cached;
        }

        var created = Create(modelType);
        Cache[modelType] = created;
        return created;
    }

    /// <summary>
    /// Retrieves metadata for the specified canonical model type.
    /// </summary>
    public static CanonModelAggregationMetadata For<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)] TModel>()
        where TModel : class
        => For(typeof(TModel));

    /// <summary>
    /// Attempts to retrieve a policy for the provided property metadata.
    /// </summary>
    public bool TryGetPolicy(PropertyInfo property, out AggregationPolicyDescriptor descriptor)
    {
        if (property is null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        if (PolicyByProperty.TryGetValue(property, out descriptor!))
        {
            return true;
        }

        if (PolicyDescriptorsByName.TryGetValue(property.Name, out descriptor!))
        {
            return true;
        }

        descriptor = null!;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve a policy descriptor by property name.
    /// </summary>
    public bool TryGetPolicy(string propertyName, out AggregationPolicyDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            descriptor = null!;
            return false;
        }

        if (PolicyDescriptorsByName.TryGetValue(propertyName, out descriptor!))
        {
            return true;
        }

        descriptor = null!;
        return false;
    }

    /// <summary>
    /// Retrieves a policy descriptor by property name or returns <c>null</c> when not declared.
    /// </summary>
    public AggregationPolicyDescriptor? GetPolicyOrDefault(string propertyName)
        => TryGetPolicy(propertyName, out var descriptor) ? descriptor : null;

    /// <summary>
    /// Retrieves a required policy descriptor by property name.
    /// </summary>
    public AggregationPolicyDescriptor GetRequiredPolicy(string propertyName)
    {
        if (TryGetPolicy(propertyName, out var descriptor))
        {
            return descriptor;
        }

        throw new KeyNotFoundException($"Canonical entity '{ModelType.Name}' does not declare an aggregation policy for property '{propertyName}'.");
    }

    private static CanonModelAggregationMetadata Create([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)] Type modelType)
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

        var policyPairs = new Dictionary<PropertyInfo, AggregationPolicyDescriptor>();
        foreach (var property in orderedProperties)
        {
            var attribute = property.GetCustomAttribute<AggregationPolicyAttribute>(inherit: true);
            if (attribute is null)
            {
                continue;
            }

            var descriptor = ResolveDescriptor(modelType, property, attribute);
            policyPairs[property] = descriptor;
        }

        var policyByName = policyPairs.ToDictionary(static pair => pair.Key.Name, static pair => pair.Value.Kind, StringComparer.OrdinalIgnoreCase);
        var policyDescriptorsByName = policyPairs.ToDictionary(static pair => pair.Key.Name, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var auditEnabled = modelType.GetCustomAttribute<CanonAttribute>(inherit: true)?.Audit ?? false;
        return new CanonModelAggregationMetadata(modelType, keyProperties, policyPairs, policyByName, policyDescriptorsByName, auditEnabled);
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

    private static AggregationPolicyDescriptor ResolveDescriptor(Type modelType, PropertyInfo property, AggregationPolicyAttribute attribute)
    {
        if (attribute.Kind == AggregationPolicyKind.SourceOfTruth)
        {
            var sources = attribute.ResolveSources();
            if (sources.Count == 0)
            {
                throw new InvalidOperationException($"Canonical entity '{modelType.Name}' property '{property.Name}' declares SourceOfTruth policy but does not specify any Source or Sources.");
            }

            if (attribute.Fallback == AggregationPolicyKind.SourceOfTruth)
            {
                throw new InvalidOperationException($"Canonical entity '{modelType.Name}' property '{property.Name}' cannot declare SourceOfTruth policy with SourceOfTruth fallback.");
            }

            return new AggregationPolicyDescriptor(attribute.Kind, sources, attribute.Fallback);
        }

        var resolvedSources = attribute.ResolveSources();
        if (resolvedSources.Count > 0)
        {
            throw new InvalidOperationException($"Canonical entity '{modelType.Name}' property '{property.Name}' declares {attribute.Kind} policy but also specifies authoritative sources. Sources may only be configured for SourceOfTruth.");
        }

        if (attribute.Fallback != AggregationPolicyKind.Latest)
        {
            throw new InvalidOperationException($"Canonical entity '{modelType.Name}' property '{property.Name}' declares {attribute.Kind} policy and cannot override the fallback behavior.");
        }

        return new AggregationPolicyDescriptor(attribute.Kind, Array.Empty<string>(), AggregationPolicyKind.Latest);
    }
}
