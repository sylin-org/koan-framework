# Koan.Canon.Runtime.Dapr

## Contract
- **Purpose**: Bridge Koan Canon orchestration onto Dapr, enqueueing projection tasks and replaying Canon entities.
- **Primary inputs**: `CanonOptions` defaults, discovered `CanonEntity<T>` models, Koan data stores via `ReferenceItem<T>` and `ProjectionTask<T>`.
- **Outputs**: Projection task records persisted through `ProjectionTask<T>.UpsertAsync(...)` and Dapr-aware orchestration logs.
- **Failure modes**: Missing Canon models, misconfigured options (no default view name), Dapr sidecar unavailable during enqueue, or storage provider exceptions when persisting tasks.
- **Success criteria**: Projection tasks created exactly-once per reference/version/view combination, replay operations log activity, and reproject requests schedule work idempotently.

## Quick start
```csharp
using Koan.Canon.Runtime;
using Koan.Canon.Runtime.Dapr;

public sealed class CanonModule : ICanonRuntimeModule
{
    public void ConfigureRuntimes(ICanonRuntimeRegistry registry)
    {
        registry.UseRuntime<DaprCanonRuntime>();
    }
}

public async Task ReprojectOrderAsync(string referenceId, CancellationToken ct)
{
    await CanonRuntime.Current.ReprojectAsync(referenceId, viewName: "orders:materialized", ct);
}
```
- Register `DaprCanonRuntime` inside your Canon module to activate the Dapr-backed projection scheduler.
- Use the runtime facade to replay or reproject entities; the runtime resolves `ReferenceItem<T>` and `ProjectionTask<T>` using first-class `Data<TEntity, TKey>` statics.

## Configuration
- Set `CanonOptions.DefaultViewName` (for example `orders:materialized`) to control the Dapr queue namespace.
- Ensure Koan data providers for `ReferenceItem<T>` and `ProjectionTask<T>` are reachable; large datasets should rely on `ProjectionTask<T>.QueryStream(...)` when backfilling.
- Run a Dapr sidecar alongside your service; the runtime only enqueues tasks but assumes Dapr components exist for delivery.

## Edge cases
- Large replay windows: throttle via `CancellationToken` or split by time window to avoid saturating task queues.
- Missing reference entities: runtime skips silently; enable debug logging to surface gaps during migrations.
- Duplicate tasks: `EnqueueIfMissing` guards on reference/version/view, but external deletions can reintroduce work.
- Cancellation: long scans honor `CancellationToken`; pass request-linked tokens from controllers to allow client aborts.

## Related packages
- `Koan.Canon.Core` – canonical orchestration primitives consumed here.
- `Koan.Canon.Web` – HTTP endpoints for invoking replay/reprojection.
- `Koan.Core` – configuration helpers used for options wiring.

## Reference
- See `DaprCanonRuntime` for runtime details and task enqueue logic.
- Technical reference: [`TECHNICAL.md`](./TECHNICAL.md) (validated 2025-09-29).
