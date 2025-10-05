---
uid: reference.modules.koan.canon.runtime.dapr
title: Koan.Canon.Runtime.Connector.Dapr – Technical Reference
description: Dapr-backed Canon runtime that schedules projection tasks and replays canonical entities.
since: 0.6.3
packages: [Sylin.Koan.Canon.Runtime.Connector.Dapr]
source: src/Koan.Canon.Runtime.Connector.Dapr/
validation:
  date: 2025-09-29
  status: verified
---

## Contract

- Provide an `ICanonRuntime` implementation backed by Dapr queues for Canon projection workloads.
- Ensure projection tasks are created idempotently per reference/version/view combination.
- Auto-register the runtime when the package is referenced so applications need no manual wiring.

## Key components

| Component                                                  | Responsibility                                                                                                                                                |
| ---------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `DaprCanonRuntime`                                         | Core runtime that implements `StartAsync`, `StopAsync`, `ReplayAsync`, and `ReprojectAsync`. Discovers Canon entities and enqueues `ProjectionTask<T>` items. |
| `Koan.Canon.Runtime.Connector.Dapr.Initialization.KoanAutoRegistrar` | Replaces the default `ICanonRuntime` with `DaprCanonRuntime` via `services.Replace(...)` and reports module metadata to the boot report.                      |
| `CanonOptions` (via `IOptionsMonitor`)                     | Supplies the default view name (`DefaultViewName`) used to namespace projection queues.                                                                       |
| `ReferenceItem<T>` / `ProjectionTask<T>`                   | Canon data entities read and written through `Koan.Data.Core.Data<TEntity,TKey>` static methods.                                                              |

## Lifecycle and data flow

1. During startup the Koan auto-registrar replaces the default runtime with `DaprCanonRuntime` and logs the provider selection in the boot report.
2. `StartAsync` emits an informational log confirming activation. `StopAsync` logs the shutdown.
3. `ReplayAsync`:
   - Uses reflection (`DiscoverModels`) to locate all `CanonEntity<>` types loaded in the current AppDomain.
   - Queries `ReferenceItem<T>` statics for entries with `RequiresProjection == true` and enqueues projection tasks per reference.
   - Each task invocation honors the supplied `CancellationToken` and respects the configured `CanonOptions.DefaultViewName`.
4. `ReprojectAsync` resolves the latest `ReferenceItem<T>` for a supplied identifier and enqueues a single task, falling back to the default view when none is provided.
5. `EnqueueIfMissing` composes a deterministic key (`{referenceId}::{version}::{view}`), checks for an existing `ProjectionTask<T>`, and only creates new entries when none are found, keeping task creation idempotent.

## Configuration

- Set `CanonOptions.DefaultViewName` (e.g., `orders:materialized`) so both replay and reprojection target the correct queue/topic.
- Ensure the hosting application references a Koan data provider that supports the Canon models (`ProjectionTask<T>`, `ReferenceItem<T>`). The runtime relies exclusively on static entity APIs (`Data<TEntity,TKey>.GetAsync`, `.Query`, `.UpsertAsync`).
- Run a Dapr sidecar (or Dapr-hosted container) alongside the service; the runtime schedules work and expects downstream Dapr components (bindings, pub-sub, etc.) to exist.
- Use `KoanEnv` configuration helpers (from `Koan.Core`) to map environment-specific view names when multiple tenants/environments share the same runtime.

## Diagnostics

- Logs follow the pattern `[Dapr] Replay ...` and `[Dapr] Reproject ...` and record reference IDs, versions, and views.
- The boot report gains a `provider: Dapr` note from the auto-registrar, which surfaces in `KoanEnv.DumpSnapshot` output.
- Add structured logging sinks or telemetry exporters (via `Koan.Recipe.Observability`) to capture projection throughput.

## Extension points

- Override task-discovery logic by decorating or replacing `ICanonRuntime` before `AddKoan()` executes if you need alternate batching strategies.
- Supply custom `CanonOptions` via configuration binding to alter view routing or throttling knobs.
- When introducing new Canon entity assemblies, ensure they are loaded before `DiscoverModels` executes (e.g., by referencing the project or using `AssemblyLoadContext`).

## Validation notes

- Code reviewed: `DaprCanonRuntime.cs`, `Initialization/KoanAutoRegistrar.cs` (commit state 2025-09-29).
- Verified projection enqueue logic via inspection of `EnqueueIfMissing`, ensuring idempotency over the composite key.
- Confirmed dependency wiring by tracing `services.Replace` in the auto-registrar and dependency on `Koan.Canon.Core`.
- Doc build validated through `docs:build` task (2025-09-29).

