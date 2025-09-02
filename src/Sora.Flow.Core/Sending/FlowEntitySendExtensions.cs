using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Sora.Core.Hosting.App;
using Sora.Flow.Model;
using Sora.Flow.Attributes;

namespace Sora.Flow.Sending;

public static class FlowEntitySendExtensions
{
    /// <summary>
    /// Send this FlowEntity to intake as a normalized record using server-side identity stamping.
    /// Defaults: sourceId = "api", occurredAt = UtcNow.
    /// </summary>
    public static Task Send<TModel>(this TModel entity, string? sourceId = null, DateTimeOffset? occurredAt = null, CancellationToken ct = default)
        where TModel : FlowEntity<TModel>, new()
        => Send(new[] { entity }, sourceId, occurredAt, ct);

    /// <summary>
    /// Send a batch of FlowEntity items to intake as normalized records using server-side identity stamping.
    /// Defaults: sourceId = "api", occurredAt = UtcNow for each item.
    /// </summary>
    public static async Task Send<TModel>(this IEnumerable<TModel> entities, string? sourceId = null, DateTimeOffset? occurredAt = null, CancellationToken ct = default)
        where TModel : FlowEntity<TModel>, new()
    {
        if (entities is null) return;
        var list = entities.ToList(); if (list.Count == 0) return;

        var sp = AppHost.Current ?? throw new InvalidOperationException("AppHost.Current is not initialized.");
        var sender = sp.GetRequiredService<IFlowSender>();

        // Discover an adapter host annotated with [FlowAdapter] to infer defaults
        // - If found and caller didn't pass sourceId, use DefaultSource from the attribute
        // - Also pass hostType to the sender for server-side identity stamping (system/adapter)
        var (hostType, defaultSource) = FindAdapterHost();
        var effectiveSource = sourceId ?? (!string.IsNullOrWhiteSpace(defaultSource) ? defaultSource : "api");

        var now = DateTimeOffset.UtcNow;
        var items = new List<FlowSendPlainItem>(list.Count);
        foreach (var e in list)
        {
            var bag = BuildBagFromEntity(e);
            // Try to include a reasonable correlation when available
            string? correlation = null;
            // Reuse Id for correlation to group records
            correlation = e.Id;

            // Stamp identifier.external.{source} from [Key] when available
            var keyProp = e.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => p.GetCustomAttribute<KeyAttribute>(inherit: true) is not null);
            var keyVal = keyProp is null ? null : SafeGet(keyProp, e) as string;
            if (!string.IsNullOrWhiteSpace(keyVal))
            {
                var extKey = $"{Infrastructure.Constants.Reserved.IdentifierExternalPrefix}{effectiveSource}";
                if (!bag.ContainsKey(extKey)) bag[extKey] = keyVal;
            }

            items.Add(new FlowSendPlainItem(
                ModelType: typeof(TModel),
                SourceId: effectiveSource,
                OccurredAt: occurredAt ?? now,
                Bag: bag,
                CorrelationId: correlation));
        }

        await sender.SendAsync(items, envelope: null, message: null, hostType: hostType, ct: ct);
    }

    // Best-effort discovery of an adapter host decorated with [FlowAdapter]
    // Returns (Type, DefaultSource). If multiple exist, prefer the single one; else pick the first.
    private static (Type? HostType, string? DefaultSource) FindAdapterHost()
    {
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var candidates = new List<(Type type, FlowAdapterAttribute attr)>();
            foreach (var asm in assemblies)
            {
                Type?[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t is null) continue;
                    var attr = t.GetCustomAttribute<FlowAdapterAttribute>(inherit: true);
                    if (attr is not null)
                        candidates.Add((t, attr));
                }
            }
            if (candidates.Count == 0) return (null, null);
            var chosen = candidates.Count == 1 ? candidates[0] : candidates[0];
            return (chosen.type, chosen.attr.DefaultSource);
        }
        catch
        {
            return (null, null);
        }
    }

    private static IDictionary<string, object?> BuildBagFromEntity<TModel>(TModel entity) where TModel : FlowEntity<TModel>, new()
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (entity is null) return dict;

        var t = entity.GetType();
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead) continue;
            var val = SafeGet(p, entity);
            if (!IsSimple(val)) continue;
            var name = p.Name;
            dict[name] = val;
            dict[$"{Infrastructure.Constants.Reserved.ModelPrefix}{name}"] = val;
        }
        return dict;
    }

    private static object? SafeGet(PropertyInfo p, object instance)
    {
        try { return p.GetValue(instance); }
        catch { return null; }
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
}
