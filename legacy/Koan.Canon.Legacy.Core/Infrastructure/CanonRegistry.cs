using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Koan.Canon.Attributes;
using Koan.Canon.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Canon.Infrastructure;

/// <summary>
/// Structure containing entity identification information for optimization scenarios.
/// </summary>
public struct EntityIdStructure
{
    public KeyComponent[] KeyComponents { get; set; }
}

/// <summary>
/// Represents a key component for entity identification.
/// </summary>
public struct KeyComponent
{
    public string Path { get; set; }
}

public static class CanonRegistry
{
    private static readonly ConcurrentDictionary<Type, string> s_modelNames = new();
    private static readonly ConcurrentDictionary<Type, string[]> s_aggTags = new();
    private static readonly ConcurrentDictionary<string, Type> s_byName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<Type, string[]> s_externalIdProps = new();
    private static readonly ConcurrentDictionary<Type, (Type Parent, string ParentKeyPath)?> s_voParent = new();

    public static string GetModelName(Type t)
    {
        return s_modelNames.GetOrAdd(t, static type =>
        {
            var attr = type.GetCustomAttribute<CanonModelAttribute>();
            if (attr is not null && !string.IsNullOrWhiteSpace(attr.Name)) return attr.Name.Trim();
            return CanonSets.ModelName(type);
        });
    }

    public static string[] GetAggregationTags(Type t)
    {
        return s_aggTags.GetOrAdd(t, static type =>
        {
            var tags = new List<string>();
            
            // Check for class-level [AggregationKeys] attribute (for DynamicCanonEntity)
            var classLevelAttr = type.GetCustomAttribute<AggregationKeysAttribute>(inherit: true);
            if (classLevelAttr?.Keys != null)
            {
                tags.AddRange(classLevelAttr.Keys.Where(s => !string.IsNullOrWhiteSpace(s))!);
            }
            
            // Check for property-level [AggregationKey] attributes (for CanonEntity<T>)
            var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            foreach (var p in props)
            {
                var keyAttr = p.GetCustomAttribute<AggregationKeyAttribute>(inherit: true);
                if (keyAttr != null)
                {
                    // Convert C# property name to JSON property name using camelCase
                    var jsonPropertyName = GetJsonPropertyName(p);
                    tags.Add(jsonPropertyName);
                }
            }
            
            // Check for property-level [AggregationTag] attributes (legacy - deprecated)
            foreach (var p in props)
            {
                var attrs = p.GetCustomAttributes<AggregationTagAttribute>(inherit: true);
                tags.AddRange(attrs.Select(a => a.Path).Where(s => !string.IsNullOrWhiteSpace(s))!);
            }
            
            return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        });
    }

    /// <summary>
    /// Returns the parent Canon entity type and key path for a value-object type (deriving from <see cref="Koan.Canon.Model.CanonValueObject{T}"/>),
    /// based on the first [ParentKey(parent: ...)] property found. Returns null if not applicable.
    /// The parent key path is automatically resolved to the [Key] property of the parent type.
    /// </summary>
    public static (Type Parent, string ParentKeyPath)? GetValueObjectParent(Type t)
    {
        return s_voParent.GetOrAdd(t, static type =>
        {
            var bt = type.BaseType;
            if (bt is null || !bt.IsGenericType || bt.GetGenericTypeDefinition() != typeof(CanonValueObject<>)) return null;
            
            // Determine parent via first [Parent(typeof(...))]
            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var pk = p.GetCustomAttribute<ParentAttribute>(inherit: true);
                if (pk is null || pk.ParentType is null) continue;

                // Find the [Key] property on the parent type
                var parentKeyProperty = pk.ParentType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(prop => prop.GetCustomAttribute<KeyAttribute>(inherit: true) != null);

                if (parentKeyProperty == null)
                    throw new InvalidOperationException($"Parent type {pk.ParentType.Name} has no [Key] property for ParentKey resolution");

                var path = GetJsonPropertyName(p); // Use property name as path since PayloadPath is no longer supported
                if (string.IsNullOrWhiteSpace(path)) continue;
                return (pk.ParentType, path);
            }
            return null;
        });
    }

    // Separate cache for entity parents to avoid conflicts
    private static readonly ConcurrentDictionary<Type, (Type Parent, string ParentKeyPath)?> s_entityParent = new();
    
    /// <summary>
    /// Returns the parent Canon entity type and key path for a CanonEntity{T} type with [ParentKey] attributes.
    /// This is used for entities (not value objects) that have parent relationships.
    /// </summary>
    public static (Type Parent, string ParentKeyPath)? GetEntityParent(Type t)
    {
        return s_entityParent.GetOrAdd(t, static type =>
        {
            var bt = type.BaseType;
            if (bt is null || !bt.IsGenericType || bt.GetGenericTypeDefinition() != typeof(CanonEntity<>)) return null;
            
            // Determine parent via first [Parent(typeof(...))]
            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var pk = p.GetCustomAttribute<ParentAttribute>(inherit: true);
                if (pk is null || pk.ParentType is null) continue;

                // Find the [Key] property on the parent type
                var parentKeyProperty = pk.ParentType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(prop => prop.GetCustomAttribute<KeyAttribute>(inherit: true) != null);

                if (parentKeyProperty == null)
                    throw new InvalidOperationException($"Parent type {pk.ParentType.Name} has no [Key] property for ParentKey resolution");

                var path = GetJsonPropertyName(p); // Use property name as path since PayloadPath is no longer supported
                if (string.IsNullOrWhiteSpace(path)) continue;
                return (pk.ParentType, path);
            }
            return null;
        });
    }

    /// <summary>
    /// Gets external ID keys for automatic population based on CanonPolicy configuration.
    /// For AutoPopulate policy: returns the ExternalIdKey or default key property.
    /// For other policies: returns empty array (manual or disabled external ID handling).
    /// </summary>
    public static string[] GetExternalIdKeys(Type modelType)
    {
        return s_externalIdProps.GetOrAdd(modelType, type =>
        {
            var policy = type.GetCustomAttribute<CanonPolicyAttribute>();
            if (policy?.ExternalIdPolicy == ExternalIdPolicy.AutoPopulate)
            {
                // Use specified key or determine default key property
                return new[] { policy.ExternalIdKey ?? GetDefaultEntityKey(type) };
            }
            // Manual, Disabled, or SourceOnly policies don't return specific keys
            return Array.Empty<string>();
        });
    }

    /// <summary>
    /// Determines the default entity key property for external ID generation.
    /// For strong-typed entities: first [Key] property name (camelCase).
    /// For dynamic entities: "id".
    /// </summary>
    private static string GetDefaultEntityKey(Type modelType)
    {
        // Check if it's a DynamicCanonEntity
        var isDynamic = typeof(IDynamicCanonEntity).IsAssignableFrom(modelType);
        
        if (isDynamic)
        {
            return "id"; // Default for dynamic entities
        }
        
        // For strong-typed entities, find the [Key] property
        var keyProperty = modelType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(prop => prop.GetCustomAttribute<KeyAttribute>(inherit: true) != null);
        
        if (keyProperty != null)
        {
            // Convert to camelCase JSON property name
            var jsonPropertyAttr = keyProperty.GetCustomAttribute<JsonPropertyAttribute>();
            if (!string.IsNullOrEmpty(jsonPropertyAttr?.PropertyName))
            {
                return jsonPropertyAttr.PropertyName;
            }
            return char.ToLowerInvariant(keyProperty.Name[0]) + keyProperty.Name[1..];
        }
        
        // Fallback to first aggregation tag if no [Key] property found
        var aggTags = GetAggregationTags(modelType);
        return aggTags.Length > 0 ? aggTags[0] : "id";
    }

    public static Type? ResolveModel(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (s_byName.TryGetValue(name, out var t)) return t;
        Scan();
        s_byName.TryGetValue(name, out t);
        return t;
    }

    /// <summary>
    /// Gets the JSON property name for a C# property, respecting JsonProperty attributes
    /// and falling back to camelCase conversion to match the system's JSON serialization.
    /// </summary>
    public static string GetJsonPropertyName(PropertyInfo property)
    {
        // Check for explicit JsonProperty attribute
        var jsonPropertyAttr = property.GetCustomAttribute<JsonPropertyAttribute>();
        if (jsonPropertyAttr?.PropertyName != null)
        {
            return jsonPropertyAttr.PropertyName;
        }
        
        // Use camelCase conversion to match CamelCasePropertyNamesContractResolver
        var camelCaseResolver = new CamelCasePropertyNamesContractResolver();
        return camelCaseResolver.GetResolvedPropertyName(property.Name);
    }

    /// <summary>
    /// Gets entity identification structure for a model type, including key components for optimization.
    /// </summary>
    public static EntityIdStructure? GetEntityIdStructure(Type modelType)
    {
        try
        {
            var aggregationTags = GetAggregationTags(modelType);
            if (aggregationTags.Length == 0) return null;

            var keyComponents = aggregationTags.Select(tag => new KeyComponent { Path = tag }).ToArray();
            return new EntityIdStructure { KeyComponents = keyComponents };
        }
        catch
        {
            return null;
        }
    }

    private static void Scan()
    {
        // Use cached assemblies instead of bespoke AppDomain scanning
        var assemblies = Koan.Core.Hosting.Bootstrap.AssemblyCache.Instance.GetAllAssemblies();
        foreach (var asm in assemblies)
        {
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
            catch { continue; }
            foreach (var type in types)
            {
                if (type is null || !type.IsClass || type.IsAbstract) continue;
                if (type.GetCustomAttribute<CanonIgnoreAttribute>() is not null) continue;
                // Accept sealed Canonicals: CanonEntity<T> with T==type
                var baseType = type.BaseType;
                if (baseType is null || !baseType.IsGenericType) continue;
                if (baseType.GetGenericTypeDefinition() == typeof(CanonEntity<>))
                {
                    var modelName = GetModelName(type);
                    s_byName[modelName] = type;
                }
                else if (baseType.GetGenericTypeDefinition() == typeof(CanonValueObject<>))
                {
                    // Register VO by name as well to enable routing and API discovery symmetry
                    var modelName = GetModelName(type);
                    s_byName[modelName] = type;
                }
                else if (baseType.GetGenericTypeDefinition() == typeof(DynamicCanonEntity<>))
                {
                    // Register DynamicCanonEntity types for model resolution
                    var modelName = GetModelName(type);
                    s_byName[modelName] = type;
                }
            }
        }
    }
}



