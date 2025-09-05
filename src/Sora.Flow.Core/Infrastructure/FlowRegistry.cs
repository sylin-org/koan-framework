using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sora.Flow.Attributes;
using Sora.Flow.Model;

namespace Sora.Flow.Infrastructure;

public static class FlowRegistry
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
            var attr = type.GetCustomAttribute<FlowModelAttribute>();
            if (attr is not null && !string.IsNullOrWhiteSpace(attr.Name)) return attr.Name.Trim();
            return FlowSets.ModelName(type);
        });
    }

    public static string[] GetAggregationTags(Type t)
    {
        return s_aggTags.GetOrAdd(t, static type =>
        {
            var tags = new List<string>();
            
            // Check for class-level [AggregationKeys] attribute
            var classLevelAttr = type.GetCustomAttribute<AggregationKeysAttribute>(inherit: true);
            if (classLevelAttr?.Keys != null)
            {
                tags.AddRange(classLevelAttr.Keys.Where(s => !string.IsNullOrWhiteSpace(s))!);
            }
            
            // Check for property-level [AggregationTag] attributes (legacy)
            var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            foreach (var p in props)
            {
                var attrs = p.GetCustomAttributes<AggregationTagAttribute>(inherit: true);
                tags.AddRange(attrs.Select(a => a.Path).Where(s => !string.IsNullOrWhiteSpace(s))!);
            }
            
            return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        });
    }

    /// <summary>
    /// Returns the parent Flow entity type and key path for a value-object type (deriving from <see cref="Sora.Flow.Model.FlowValueObject{T}"/>),
    /// based on the first [ParentKey(parent: ...)] property found. Returns null if not applicable.
    /// </summary>
    public static (Type Parent, string ParentKeyPath)? GetValueObjectParent(Type t)
    {
        return s_voParent.GetOrAdd(t, static type =>
        {
            var bt = type.BaseType;
            if (bt is null || !bt.IsGenericType || bt.GetGenericTypeDefinition() != typeof(FlowValueObject<>)) return null;
            // Determine parent via first [ParentKey(parent: ...)]
            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var pk = p.GetCustomAttribute<ParentKeyAttribute>(inherit: true);
                if (pk is null || pk.Parent is null) continue;
                var path = string.IsNullOrWhiteSpace(pk.PayloadPath) ? p.Name : pk.PayloadPath;
                if (string.IsNullOrWhiteSpace(path)) continue;
                return (pk.Parent, path);
            }
            return null;
        });
    }

    /// <summary>
    /// External-id property discovery via attributes is deprecated. Reserved keys (identifier.external.*) are used.
    /// </summary>
    public static string[] GetExternalIdKeys(Type modelType)
    {
        // vNext: rely on reserved identifier.external.* keys; explicit [EntityLink] is removed.
        return s_externalIdProps.GetOrAdd(modelType, static _ => Array.Empty<string>());
    }

    public static Type? ResolveModel(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (s_byName.TryGetValue(name, out var t)) return t;
        Scan();
        s_byName.TryGetValue(name, out t);
        return t;
    }

    private static void Scan()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
            catch { continue; }
            foreach (var type in types)
            {
                if (type is null || !type.IsClass || type.IsAbstract) continue;
                if (type.GetCustomAttribute<FlowIgnoreAttribute>() is not null) continue;
                // Accept sealed canonicals: FlowEntity<T> with T==type
                var baseType = type.BaseType;
                if (baseType is null || !baseType.IsGenericType) continue;
                if (baseType.GetGenericTypeDefinition() == typeof(FlowEntity<>))
                {
                    var modelName = GetModelName(type);
                    s_byName[modelName] = type;
                }
                else if (baseType.GetGenericTypeDefinition() == typeof(FlowValueObject<>))
                {
                    // Register VO by name as well to enable routing and API discovery symmetry
                    var modelName = GetModelName(type);
                    s_byName[modelName] = type;
                }
            }
        }
    }
}
