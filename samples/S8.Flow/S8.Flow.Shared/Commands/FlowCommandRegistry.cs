using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace S8.Flow.Shared.Commands;

public interface IFlowCommandRegistry
{
    IFlowCommandRegistry On(string name, Func<FlowCommandContext, IDictionary<string, object?>, CancellationToken, Task> handler, string? target = null);
}

internal sealed class FlowCommandRouter : IFlowCommandRegistry
{
    private readonly ConcurrentDictionary<string, List<(string? Target, Func<FlowCommandContext, IDictionary<string, object?>, CancellationToken, Task> Handler)>> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public IFlowCommandRegistry On(string name, Func<FlowCommandContext, IDictionary<string, object?>, CancellationToken, Task> handler, string? target = null)
    {
        var list = _handlers.GetOrAdd(name, _ => new());
        list.Add((target, handler));
        return this;
    }

    public async Task DispatchAsync(FlowCommandDispatch cmd, IServiceProvider services, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(cmd.Name, out var handlers))
            return;
        var ctx = new FlowCommandContext
        {
            Services = services,
            TargetAdapter = cmd.Target,
            Issuer = cmd.Source,
            IssuedAt = cmd.IssuedAt
        };
        var tasks = new List<Task>();
        foreach (var (target, handler) in handlers)
        {
            if (cmd.Target == null || string.Equals(cmd.Target, target, StringComparison.OrdinalIgnoreCase))
                tasks.Add(handler(ctx, new Dictionary<string, object?>(cmd.Args, StringComparer.OrdinalIgnoreCase), ct));
        }
        await Task.WhenAll(tasks);
    }
}
