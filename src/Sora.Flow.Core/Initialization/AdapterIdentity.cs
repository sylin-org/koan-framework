using System;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using Sora.Core.Utilities.Ids;
using Sora.Flow.Attributes;

namespace Sora.Flow.Initialization;

internal sealed class AdapterIdentity : IAdapterIdentity
{
    public string? System { get; }
    public string? Adapter { get; }
    public string InstanceId { get; }
    public string? Version { get; }
    public string? Bus { get; }
    public string? Group { get; }
    public string[]? Capabilities { get; }
    public string Host { get; }
    public int Pid { get; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public int HeartbeatSeconds { get; }

    public AdapterIdentity(int heartbeatSeconds)
    {
        // Discover first FlowAdapter attribute
    string? sys = null, adp = null; string[]? caps = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var t in asm.GetTypes())
                {
                    if (t.IsAbstract) continue;
                    var fa = t.GetCustomAttribute<FlowAdapterAttribute>();
            if (fa != null) { sys = fa.System; adp = fa.Adapter; caps = fa.Capabilities; break; }
                }
                if (sys != null) break;
            }
            catch { }
        }
        System = sys;
        Adapter = adp;
    Capabilities = caps;
        InstanceId = UlidId.New();
        Version = Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString();
        Bus = Environment.GetEnvironmentVariable("Sora__Messaging__DefaultBus") ?? "default";
        Group = Environment.GetEnvironmentVariable("Sora__Messaging__DefaultGroup") ?? "flow";
        Host = Dns.GetHostName();
        Pid = Process.GetCurrentProcess().Id;
        HeartbeatSeconds = Math.Max(5, heartbeatSeconds);
    }
}
