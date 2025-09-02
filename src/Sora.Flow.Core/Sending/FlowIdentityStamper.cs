using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Flow.Infrastructure;
using Sora.Messaging;
using System;
using System.Collections.Generic;

namespace Sora.Flow.Sending;

public interface IFlowIdentityStamper
{
    void Stamp(IDictionary<string, object?> bag, MessageEnvelope? envelope = null, object? message = null, Type? hostType = null);
}

internal sealed class FlowIdentityStamper : IFlowIdentityStamper
{
    public void Stamp(IDictionary<string, object?> bag, MessageEnvelope? envelope = null, object? message = null, Type? hostType = null)
    {
        if (bag is null) return;
        // Respect existing values
        var hasSystem = bag.ContainsKey(Constants.Envelope.System);
        var hasAdapter = bag.ContainsKey(Constants.Envelope.Adapter);
        if (hasSystem && hasAdapter) return;

        string? sys = null, adp = null;

        // 1) From message object properties (System/Adapter)
        if (message is not null)
        {
            try
            {
                var t = message.GetType();
                sys = (t.GetProperty(Constants.Envelope.System, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase)?.GetValue(message) as string) ?? sys;
                adp = (t.GetProperty(Constants.Envelope.Adapter, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase)?.GetValue(message) as string) ?? adp;
            }
            catch { }
        }

        // 2) From envelope headers
        if (string.IsNullOrWhiteSpace(sys) && envelope is not null && envelope.Headers is not null)
            envelope.Headers.TryGetValue(Constants.Envelope.System, out sys);
        if (string.IsNullOrWhiteSpace(adp) && envelope is not null && envelope.Headers is not null)
            envelope.Headers.TryGetValue(Constants.Envelope.Adapter, out adp);

        // 3) From host type annotated with [FlowAdapter]
        if ((string.IsNullOrWhiteSpace(sys) || string.IsNullOrWhiteSpace(adp)) && hostType is not null)
        {
            var meta = AdapterIdentity.FromType(hostType);
            sys ??= meta?.System;
            adp ??= meta?.Adapter;
        }

        // 4) Fallbacks
        sys = string.IsNullOrWhiteSpace(sys) ? "unknown" : sys;
        adp = string.IsNullOrWhiteSpace(adp) ? "unknown" : adp;

        if (!hasSystem) bag[Constants.Envelope.System] = sys;
        if (!hasAdapter) bag[Constants.Envelope.Adapter] = adp;
    }
}

public static class FlowIdentityStamperRegistration
{
    public static IServiceCollection AddFlowIdentityStamper(this IServiceCollection services)
    {
        services.TryAddSingleton<IFlowIdentityStamper, FlowIdentityStamper>();
        return services;
    }
}
