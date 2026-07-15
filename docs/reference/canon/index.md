---
type: REF
domain: canon
title: "Canon Pillar Reference"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: reviewed
  scope: Koan.Canon.Domain and Koan.Canon.Web public source inventory; no maturity promotion
---

# Canon Pillar Reference

## Contract

- **Input:** a `CanonEntity<TModel>`, optional `CanonizationOptions`, and an optional ordered pipeline
  configured for that model.
- **Output:** a persisted canonical entity, metadata and state, a `CanonizationOutcome`, and the phase
  events produced by configured contributors.
- **Default:** with no configured pipeline, canonization still persists the entity through Koan Data.
- **Errors:** invalid options, missing host/runtime registration, missing canonical records during
  rebuild, and contributor or persistence failures surface as exceptions.
- **Success:** business code describes canonical state and policy while the runtime owns phase order,
  metadata carriage, persistence, observation, and optional Web exposure.

## What Canon is today

Canon is an in-process canonicalization runtime built around `CanonEntity<TModel>`. It provides:

- canonical metadata, source attribution, lineage, lifecycle, and readiness state;
- ordered contributor phases: `Intake`, `Validation`, `Aggregation`, `Policy`, `Projection`, and
  `Distribution`;
- per-operation origin, correlation, staging, rebuild, requested-view, distribution, and tag options;
- default persistence through Koan Data, with replaceable `ICanonPersistence` and `ICanonAuditSink`;
- optional model discovery and generic MVC controllers from `Koan.Canon.Web`.

The phase names express intent. They do not imply a network hop, durable event log, message broker,
AI service, or vector store. Those behaviors require explicit contributors and referenced modules.

## Shortest domain path

```csharp
public sealed class CustomerCanon : CanonEntity<CustomerCanon>
{
    public string DisplayName { get; set; } = "";
}

builder.Services.AddKoan();
builder.Services.AddCanonRuntime(runtime =>
    runtime.ConfigurePipeline<CustomerCanon>(pipeline =>
        pipeline.AddStep(CanonPipelinePhase.Validation, (context, _) =>
        {
            if (string.IsNullOrWhiteSpace(context.Entity.DisplayName))
                throw new ValidationException("DisplayName is required.");

            return ValueTask.CompletedTask;
        })));

var result = await new CustomerCanon { DisplayName = "Ada" }.Canonize(
    origin: "customer-import",
    cancellationToken: ct);
```

`Koan.Canon.Domain` users register `AddCanonRuntime(...)` explicitly. Referencing
`Koan.Canon.Web` lets its Koan module register the runtime, discover concrete `CanonEntity<>` and
`CanonValueObject<>` types, and add their generic controllers during `AddKoan()` composition.

## Runtime surfaces

| Surface | Current meaning |
|---|---|
| `entity.Canonize(origin, configure, ct)` | Resolve the active runtime and canonize one entity. |
| `ICanonRuntime.Canonize<T>(...)` | Execute the configured model pipeline or direct-persist fallback. |
| `ConfigurePipeline<T>(...)` | Replace the descriptor for `T`; add delegate steps or contributors by phase. |
| `RebuildViews<T>(id, views, ct)` | Reload a canonical entity and canonize it with rebuild options. |
| `RegisterObserver(...)` | Observe phase boundaries and errors for the lifetime of the returned registration. |
| `Replay(from, to, ct)` | Enumerate retained process-local `CanonizationRecord` snapshots. |
| `SetRecordCapacity(n)` | Bound the in-memory record queue; the default capacity is 1024. |

Configured phases run in enum order; contributors within a phase run in registration order. The
runtime passes one `CanonPipelineContext<T>` containing the entity, mutable metadata/options
snapshots, services, persistence, optional stage, and a per-operation item bag.

## Web surface

`Koan.Canon.Web` discovers concrete Canon models and exposes:

- `/api/canon/{model}` — entity endpoints whose single and bulk writes pass through Canon;
- `/api/canon/value-objects/{type}` — generic value-object entity endpoints;
- `/api/canon/models` — discovered model and pipeline metadata;
- `/api/canon/admin/records` — retained process-local canonization records;
- `/api/canon/admin/{slug}/rebuild` — request a canonical model rebuild.

These routes are application surfaces, not an authorization policy. Apply Koan/ASP.NET authorization
appropriate to the deployment, especially to the admin endpoints.

## Boundaries and maturity

- `Replay` reads a bounded `ConcurrentQueue` owned by the current runtime. Records do not survive a
  restart and do not constitute event sourcing or a durable replay guarantee.
- A `CanonizationEvent` is a phase result/observation record. It is not Koan Messaging transport.
- `Koan.Core.Pipelines.Pipeline()` is a separate in-process `IAsyncEnumerable<T>` composition API;
  it is not a Canon pipeline descriptor or durability mechanism.
- Canon persistence inherits the selected Data adapter's behavior. Large Entity reads may use
  `AllStream`/`QueryStream` only on adapters qualified by DATA-0107; InMemory, JSON, and Redis reject
  those streaming facades.
- Koan remains pre-1.0. Treat Canon as an implemented, test-owned runtime surface—not blanket
  certification of every ingestion, projection, recovery, or distributed-topology scenario.

## Evidence and related decisions

- [Canon domain source](../../../src/Koan.Canon.Domain/)
- [Canon Web source](../../../src/Koan.Canon.Web/)
- [Canon unit suites](../../../tests/Suites/Canon/Unit/)
- [Canon integration suites](../../../tests/Suites/Canon/Integration/)
- [ARCH-0058 — Canon runtime architecture](../../decisions/ARCH-0058-canon-runtime-architecture.md)
- [DATA-0107 — provider-bounded Entity streams](../../decisions/DATA-0107-provider-bounded-entity-streams.md)
