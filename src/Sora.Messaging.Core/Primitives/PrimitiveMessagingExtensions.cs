using Microsoft.Extensions.DependencyInjection;
using Sora.Core.Hosting.App;
using Sora.Messaging.Provisioning;
using Sora.Messaging.Primitives;

namespace Sora.Messaging;

/// <summary>
/// High-level primitive sending helpers. Current implementation uses the existing IMessageBus.
/// Providers will later add specialized routing using the computed routing key string.
/// </summary>
public static class PrimitiveMessagingExtensions
{
    public const string HeaderKind = "x-sora-kind";
    public const string HeaderCommandTarget = "x-command-target";
    public const string HeaderFlowAdapter = "x-flow-adapter";
    public const string HeaderFlowEventAlias = "x-flow-event-alias";

    public static Task SendCommand(this ICommandPrimitive command, string targetService, CancellationToken ct = default)
        => Dispatch(command!, targetService, kind: "command", ct);

    public static Task Announce(this IAnnouncementPrimitive announcement, CancellationToken ct = default)
        => Dispatch(announcement!, target: null, kind: "announcement", ct);

    public static Task PublishFlowEvent(this IFlowEventPrimitive flowEvent, string? adapter = null, string? alias = null, CancellationToken ct = default)
        => Dispatch(flowEvent!, target: adapter, kind: "flow-event", ct, extraHeaders: alias is null ? null : new Dictionary<string, string?> { [HeaderFlowEventAlias] = alias });

    private static async Task Dispatch(object primitive, string? target, string kind, CancellationToken ct, IDictionary<string, string?>? extraHeaders = null)
    {
        var sp = AppHost.Current ?? throw new InvalidOperationException("AppHost.Current not set. Ensure AddSora() has run.");
        var busSel = sp.GetRequiredService<IMessageBusSelector>();
        var bus = busSel.ResolveDefault(sp);
        // For now just send the primitive directly. Provider routing adaptation will come later.
        // We opportunistically attach headers if the message has mutable header bag via reflection pattern: HeaderBag / IDictionary<string,string?> property.
        TryStampHeaders(primitive, kind, target, extraHeaders);
        await bus.SendAsync(primitive, ct).ConfigureAwait(false);
    }

    private static void TryStampHeaders(object primitive, string kind, string? target, IDictionary<string, string?>? extra)
    {
        try
        {
            var t = primitive.GetType();
            var bagProp = t.GetProperties().FirstOrDefault(p => p.CanRead && p.CanWrite && typeof(IDictionary<string, string?>).IsAssignableFrom(p.PropertyType));
            if (bagProp?.GetValue(primitive) is IDictionary<string, string?> bag)
            {
                if (!bag.ContainsKey(HeaderKind)) bag[HeaderKind] = kind;
                if (target is not null && kind == "command" && !bag.ContainsKey(HeaderCommandTarget)) bag[HeaderCommandTarget] = target;
                if (extra != null)
                {
                    foreach (var kv in extra)
                        if (!bag.ContainsKey(kv.Key)) bag[kv.Key] = kv.Value;
                }
            }
        }
        catch { /* non-fatal */ }
    }
}
