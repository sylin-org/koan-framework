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
            var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            var tags = new List<string>();
            foreach (var p in props)
            {
                var attrs = p.GetCustomAttributes<AggregationTagAttribute>(inherit: true);
                tags.AddRange(attrs.Select(a => a.Path).Where(s => !string.IsNullOrWhiteSpace(s))!);
            }
            return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        });
    }

    /// <summary>
    /// Discover external-id property names from [EntityLink(typeof(TModel), LinkKind.ExternalId)]
    /// across all loaded assemblies. Returned names are distinct and case-insensitive.
    /// </summary>
    public static string[] GetExternalIdKeys(Type modelType)
    {
        return s_externalIdProps.GetOrAdd(modelType, static mt =>
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                Type?[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t is null || !t.IsClass || t.IsAbstract) continue;
                    var props = t.GetProperties(BindingFlags.Instance | BindingFlags.Public);
                    foreach (var p in props)
                    {
                        var links = p.GetCustomAttributes<EntityLinkAttribute>(inherit: true);
                        foreach (var link in links)
                        {
                            if (link is null) continue;
                            if (link.Kind != LinkKind.ExternalId) continue;
                            if (link.FlowEntityType != mt) continue;
                            if (!string.IsNullOrWhiteSpace(p.Name)) names.Add(p.Name);
                        }
                    }
                }
            }
            // Stable ordering for predictability
            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        });
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
                if (baseType.GetGenericTypeDefinition() != typeof(FlowEntity<>)) continue;
                var modelName = GetModelName(type);
                s_byName[modelName] = type;
            }
        }
    }
}
