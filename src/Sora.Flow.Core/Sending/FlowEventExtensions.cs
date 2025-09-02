using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.ComponentModel.DataAnnotations;
using Sora.Flow.Model;

namespace Sora.Flow.Sending;

public static class FlowEventExtensions
{
    /// <summary>
    /// Build a FlowEvent from a FlowEntity/FlowValueObject instance. Adds both plain and model.* entries.
    /// If a [Key] is present and a source name can be inferred/provided, also stamps identifier.external.{source}.
    /// </summary>
    public static FlowEvent ToFlowEvent(this object model, string? sourceName = null)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        var payload = new FlowEvent();
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var t = model.GetType();
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead) continue;
            object? val;
            try { val = p.GetValue(model); } catch { continue; }
            if (!IsSimple(val)) continue;
            var name = p.Name;
            dict[name] = val;
            dict[$"{Infrastructure.Constants.Reserved.ModelPrefix}{name}"] = val;
        }

        // Best-effort external-id stamping
        var (hostType, defaultSource) = FindAdapterHost();
        var src = sourceName ?? defaultSource;
        var keyProp = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetCustomAttribute<KeyAttribute>(inherit: true) is not null);
        var keyVal = keyProp is null ? null : (keyProp.GetValue(model) as string);
        if (!string.IsNullOrWhiteSpace(src) && !string.IsNullOrWhiteSpace(keyVal))
        {
            var extKey = $"{Infrastructure.Constants.Reserved.IdentifierExternalPrefix}{src}";
            if (!dict.ContainsKey(extKey)) dict[extKey] = keyVal;
        }

        foreach (var kv in dict) payload.With(kv.Key, kv.Value);
        return payload;
    }

    public static IEnumerable<FlowEvent> ToFlowEvents<T>(this IEnumerable<T> models, string? sourceName = null)
    {
        if (models is null) yield break;
        foreach (var m in models) yield return ToFlowEvent(m!, sourceName);
    }

    private static bool IsSimple(object? val)
    {
        if (val is null) return false;
        var t = val.GetType();
        if (t.IsPrimitive) return true;
        if (t == typeof(string) || t == typeof(decimal)) return true;
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(Guid)) return true;
        if (t.IsEnum) return true;
        return false;
    }

    // Local copy to avoid tight coupling; matches behavior in FlowEntitySendExtensions
    private static (Type? HostType, string? DefaultSource) FindAdapterHost()
    {
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                Type?[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t is null) continue;
                    var attr = t.GetCustomAttribute<Sora.Flow.Attributes.FlowAdapterAttribute>(inherit: true);
                    if (attr is not null)
                        return (t, attr.DefaultSource);
                }
            }
        }
        catch { }
        return (null, null);
    }
}
