using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Sora.Flow.Sending;

namespace S8.Flow.Shared.Commands;

public static class FlowCommand
{
    public static void Send(string name, object? args = null, string? target = null, string? source = null)
    {
        var dict = args switch
        {
            null => new Dictionary<string, object?>(),
            IDictionary<string, object?> d => new Dictionary<string, object?>(d, StringComparer.OrdinalIgnoreCase),
            _ => Flatten(args)
        };
        var cmd = new FlowCommandDispatch(name, target, dict, DateTimeOffset.UtcNow, source);
        // Fire-and-forget: send on the bus (no await)
        var sp = Sora.Core.Hosting.App.AppHost.Current;
        var sender = sp?.GetService(typeof(IFlowSender)) as IFlowSender;
        if (sender != null)
        {
            var dictArgs = new Dictionary<string, object?>(cmd.Args, StringComparer.OrdinalIgnoreCase);
            var item = FlowSendPlainItem.Of<FlowCommandDispatch>(dictArgs, cmd.Target ?? "broadcast", cmd.IssuedAt, cmd.Name);
            _ = sender.SendAsync(new[] { item }, null, null, null, CancellationToken.None);
        }
    }

    private static Dictionary<string, object?> Flatten(object obj)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in obj.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            dict[prop.Name] = prop.GetValue(obj);
        }
        return dict;
    }
}
