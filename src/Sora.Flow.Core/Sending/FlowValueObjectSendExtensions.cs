using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Sora.Flow.Model;

namespace Sora.Flow.Sending;

/// <summary>
/// BEAUTIFUL messaging-first Flow value object extensions.
/// Routes all sends through the messaging system for proper orchestration.
/// </summary>
public static class FlowValueObjectSendExtensions
{
    /// <summary>
    /// Send this FlowValueObject through the messaging system (broadcast).
    /// Routes through Sora.Messaging for orchestrator handling.
    /// </summary>
    public static Task Send<TValueObject>(this TValueObject valueObject, CancellationToken ct = default)
        where TValueObject : FlowValueObject<TValueObject>, new()
    {
        if (valueObject is null) throw new ArgumentNullException(nameof(valueObject));
        return Flow.Send(valueObject).Broadcast(ct);
    }

    /// <summary>
    /// Send this FlowValueObject to a specific target through messaging.
    /// Target format: "system:adapter" (e.g., "bms:simulator")
    /// </summary>
    public static Task SendTo<TValueObject>(this TValueObject valueObject, string target, CancellationToken ct = default)
        where TValueObject : FlowValueObject<TValueObject>, new()
    {
        if (valueObject is null) throw new ArgumentNullException(nameof(valueObject));
        return Flow.Send(valueObject).To(target, ct);
    }

    /// <summary>
    /// Send a batch of FlowValueObject items through messaging (broadcast).
    /// </summary>
    public static async Task Send<TValueObject>(this IEnumerable<TValueObject> valueObjects, CancellationToken ct = default)
        where TValueObject : FlowValueObject<TValueObject>, new()
    {
        if (valueObjects is null) return;
        var list = valueObjects.ToList();
        if (list.Count == 0) return;

        var tasks = list.Select(vo => vo.Send(ct));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Send a batch of FlowValueObject items to a specific target.
    /// </summary>
    public static async Task SendTo<TValueObject>(this IEnumerable<TValueObject> valueObjects, string target, CancellationToken ct = default)
        where TValueObject : FlowValueObject<TValueObject>, new()
    {
        if (valueObjects is null) return;
        var list = valueObjects.ToList();
        if (list.Count == 0) return;

        var tasks = list.Select(vo => vo.SendTo(target, ct));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Send FlowValueObject directly to Flow intake for processing.
    /// Routes to the Flow orchestrator for value object processing and storage.
    /// </summary>
    public static async Task SendToFlowIntake<TValueObject>(this TValueObject valueObject, string? sourceId = null, CancellationToken ct = default)
        where TValueObject : FlowValueObject<TValueObject>, new()
    {
        if (valueObject is null) throw new ArgumentNullException(nameof(valueObject));
        
        // For value objects, we need to manually build the Flow payload
        var bag = BuildBagFromValueObject(valueObject);
        var item = FlowSendPlainItem.Of<TValueObject>(
            bag: bag,
            sourceId: sourceId ?? "orchestrator",
            occurredAt: DateTimeOffset.UtcNow);

        var sp = Sora.Core.Hosting.App.AppHost.Current 
            ?? throw new InvalidOperationException("AppHost.Current is not initialized.");
        var sender = sp.GetRequiredService<IFlowSender>();
        
        await sender.SendAsync(new[] { item }, ct: ct);
    }

    /// <summary>
    /// INTERNAL: Send batch of FlowValueObjects directly to Flow intake.
    /// </summary>
    internal static async Task SendToFlowIntake<TValueObject>(this IEnumerable<TValueObject> valueObjects, string? sourceId = null, CancellationToken ct = default)
        where TValueObject : FlowValueObject<TValueObject>, new()
    {
        if (valueObjects is null) return;
        var list = valueObjects.ToList();
        if (list.Count == 0) return;

        var tasks = list.Select(vo => vo.SendToFlowIntake(sourceId, ct));
        await Task.WhenAll(tasks);
    }

    private static IDictionary<string, object?> BuildBagFromValueObject<TVO>(TVO valueObject) where TVO : FlowValueObject<TVO>, new()
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (valueObject is null) return dict;

        var t = valueObject.GetType();
        foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!p.CanRead) continue;
            
            object? val;
            try { val = p.GetValue(valueObject); }
            catch { continue; }
            
            if (!IsSimple(val)) continue;
            
            var name = p.Name;
            dict[name] = val;
            dict[$"{Infrastructure.Constants.Reserved.ModelPrefix}{name}"] = val;
        }
        
        return dict;
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