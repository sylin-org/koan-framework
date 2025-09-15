using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Flow.Monitoring;

/// <summary>
/// Untyped monitor hook invoked after a projection is computed and before commit.
/// Implementations may mutate the model snapshot and policy bag atomically persisted by the runtime.
/// </summary>
public interface IFlowMonitor
{
    Task OnProjectedAsync(Type modelType, FlowMonitorContext ctx, CancellationToken ct);
}

/// <summary>
/// Typed monitor hook. Preferred when the monitor only applies to a specific model type.
/// </summary>
/// <typeparam name="TModel">Flow canonical model type.</typeparam>
public interface IFlowMonitor<TModel>
{
    Task OnProjectedAsync(FlowMonitorContext ctx, CancellationToken ct);
}

/// <summary>
/// Context passed to monitors with mutable model snapshot and policy bag.
/// </summary>
public sealed class FlowMonitorContext
{
    public FlowMonitorContext(string modelName, string referenceId, IDictionary<string, object?> model, IDictionary<string, string> policies)
    {
        ModelName = modelName;
        ReferenceId = referenceId;
        Model = model;
        Policies = policies;
    }

    public string ModelName { get; }
    public string ReferenceId { get; }
    public IDictionary<string, object?> Model { get; }
    public IDictionary<string, string> Policies { get; }
}
