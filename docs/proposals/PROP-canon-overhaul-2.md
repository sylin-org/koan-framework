# PROP Canon Overhaul 2: Transport-Agnostic Canonization Engine

---
**Contract**
- Inputs: Koan canonization requirements, current Koan.Canon codebase constraints, Koan engineering and architecture directives.
- Outputs: A clean-slate architecture and implementation plan for Koan.Canon that removes transport coupling, preserves CanonEntity usage, and enables multi-stage transformation hooks.
- Success Criteria: Canonization pipeline can run in-process or via transport, developers canonize entities with low cognitive load, every pipeline phase exposes deterministic hooks, and operational tooling (replay, projection rebuild) is first-class.
- Failure Modes: Retaining messaging coupling, ambiguous extension points, runtime APIs with Async suffixes, or undocumented migration steps.
---

## Summary
Koan.Canon presently mixes domain canonization with queue transport plumbing, reflection-driven orchestration, and sprawling background services. This proposal rebuilds Koan.Canon as a modular engine that keeps `CanonEntity<T>` as the semantic opt-in, trims the default storage footprint to a shared `CanonIndex` plus optional `CanonStage<T>`, and introduces a transport-agnostic pipeline with precise transformation hooks. Developers canonize inline with `entity.Canonize(...)`, or opt into centralized processing via `entity.SendForCanonization()`. Messaging becomes a thin adapter layered over the same engine. The plan below realigns data layout, APIs, and extensibility without preserving legacy scaffolding.

## Problem Statement
The current implementation violates separation of concerns:
- Core canon logic depends on Koan.Messaging and queue naming conventions, preventing inline usage.
- `CanonOrchestratorBase` mixes intake, validation, aggregation, projection, and messaging acknowledgements inside an 800+ line background service.
- Reflection-heavy registries (`CanonRegistry`, `CanonMessagingInitializer`) create brittle behavior and hinder testability.
- DX friction: developers must craft transport envelopes instead of writing `entity.Canonize("crm")`.
- Operational features (replay, projection rebuild) are placeholders.

Given the greenfield stance, we can recompose the module without back-compat shims.

## Goals
1. Deliver a pipeline engine that canonizes `CanonEntity<T>` instances without any messaging dependencies.
2. Retain `CanonEntity<T>` as the canonical marker while relying on attributes and descriptors for metadata enrichment.
3. Expose transformation hooks at every pipeline stage with deterministic ordering and per-model/global registration.
4. Provide a succinct runtime API (`ICanonRuntime`) with verbs like `Canonize`, `RebuildViews`, and `Replay` (no `Async` suffix). 
5. Offer optional transport adapters (MQ, HTTP, etc.) that wrap the engine instead of owning it.
6. Improve observability (events, metrics, healing queues) and operations (reprojection, targeted replays).
7. Document the architecture, DX, and migration path clearly for Koan teams.

## Non-Goals
- Preserving existing background service classes or message contracts.
- Maintaining binary or configuration compatibility with current queues.
- Retrofitting all existing samples in this proposal (follow-up tasks can do updates once the engine lands).
- Designing the full healing UI or dashboards (out of scope; we focus on canon services).

## Architectural Overview
The system consolidates into five cooperating packages:

1. **Domain (`Koan.Canon.Domain`)**: Houses `CanonEntity<T>`, `CanonValueObject<T>`, descriptors, and strongly typed records. Maintains `[OptimizeStorage]` and naming overrides by default.
2. **Metadata (`Koan.Canon.Metadata`)**: Provides `ICanonMetadataProvider` that scans assemblies once at boot, producing `CanonModelDescriptor<T>` objects (aggregation keys, parent relationships, external ID policies, default policies, custom transforms).
3. **Engine (`Koan.Canon.Engine`)**: Implements the canonical pipeline orchestrator and store abstractions. All pipeline steps are discoverable through DI.
4. **Runtime (`Koan.Canon.Runtime`)**: Exposes `ICanonRuntime` surface plus CLI tooling hooks. Bridges developer calls to the engine.
5. **Transport (`Koan.Canon.Transport.*`)**: Optional adapters (initial focus on Koan.Messaging). Each adapter materializes `CanonTransportPublisher` and `CanonTransportListener` that call into the engine.

### Canon Pipeline Composition
```
Intake → Validation → Aggregation → Policy → Projection → Distribution
```

Each phase is represented by a service contract and can host multiple ordered contributors. All mutations occur on typed models (`T : CanonEntity<T>`). The engine persists canonical state through `ICanonStore<T>`, whose default implementation uses only canonical entities plus a shared index. Stage storage is opt-in when deferred processing is enabled.

### Transformation Hook Strategy
| Phase | Contract | Hook Types |
| --- | --- | --- |
| Intake | `IIntakeStep` | `BeforeIntake` (mutate request), `AfterIntake` (augment metadata, inject diagnostics). |
| Validation | `IValidationStep` | `Validate` returns `ValidationResult`; optional `OnValid` and `OnInvalid` transforms for last-mile adjustments. |
| Aggregation | `IAggregationStep` | `OnSelectAggregationKey`, `OnResolveCanonicalId`, `OnConflict` (decide merge, park, heal). |
| Policy | `IPolicyStep` | `OnPolicyEvaluated`, `OnPolicyApplied` to tailor attribute selection or versioning. |
| Projection | `IProjectionStep` | `OnBuildCanonicalView`, `OnBuildLineage`, `OnProduceCustomView` for extra projections. |
| Distribution | `IDistributionStep` | `BeforeDistribute`, `AfterDistribute`, pluggable distributors (MQ, HTTP webhook, streaming). |

Developers register hooks through fluent builders exposed by `ICanonPipelineBuilder`. Hooks support global defaults and per-model overrides (via descriptors) while guaranteeing stable ordering.

### Data & Storage Plan
- **Canonical entity**: `CanonEntity<T>` stores the elected values plus embedded metadata (`CanonMetadata`) tracking source provenance, external IDs, policy outcomes, and lineage pointers.
- **Shared index**: `CanonIndex` replaces `KeyIndex<T>`, `IdentityLink<T>`, and similar artifacts. It records aggregation keys and external IDs across all models (columns: entity type, aggregation key/value, origin, external Id, canonical Id).
- **Optional staging**: `CanonStage<T>` provides a compact envelope (`Status`, `Origin`, payload, metadata) only when deferred or distributed processing is configured.
- **Derived projections**: Canonical and lineage views are produced via `IProjectionStep` implementations. Persisted projections remain optional; by default they can be re-derived from canonical state and metadata.
- `ICanonStore<T>` encapsulates persistence for all three constructs so providers can override storage without touching pipeline code.

### Runtime Surface
```
public interface ICanonRuntime
{
    Task<CanonizationResult<T>> Canonize<T>(T entity, CanonizationOptions? options = null, CancellationToken ct = default)
        where T : CanonEntity<T>;

    Task RebuildViews<T>(string canonicalId, string[]? views = null, CancellationToken ct = default)
        where T : CanonEntity<T>;

    IAsyncEnumerable<CanonizationRecord> Replay(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);

    IDisposable RegisterObserver(ICanonPipelineObserver observer);
}
```
`ICanonPipelineObserver` receives callbacks for `BeforePhase`, `AfterPhase`, `OnError`, enabling policy analytics and telemetry. CLI tools consume runtime to replay or reproject without messaging.

### Developer Experience
- **Inline canonization**:
   ```csharp
   var device = new Device { Serial = "SN-123" };
   var canonical = await device.Canonize(origin: "crm-us-east");
   ```
   Extension methods live on `CanonEntity<T>` and delegate to `ICanonRuntime` resolved from DI.
- **Distributed canonization**:
   ```csharp
   await device.SendForCanonization();
   ```
   This publishes a `CanonTransportEnvelope` only when a transport adapter (e.g., messaging) is registered. The receiving service calls the same engine.
- **Deferred processing**: `await device.StageForCanonization(...)` writes to `CanonStage<T>`, letting event-driven background services drain stages using the pipeline.
- **DTO-friendly path**: `await CanonEntity.Canonize(payload, origin)` remains available for contexts where entities are materialized late.
- **Configuration**: `builder.Services.AddCanonEngine()` wires metadata provider, runtime, default store, and pipeline steps. Optional `.AddCanonTransportMessaging()` registers bridge listeners and publishers.
- **Policy overrides**: teams implement `IPolicyStep` (or any phase contract) and register them via `ICanonPipelineBuilder.ConfigureModel<CustomerCanon>(...)`.

### Observability & Events
`ICanonEventPublisher` emits structured events (`CanonizationStarted`, `CanonizationCompleted`, `CanonizationParked`, `CanonizationFailed`). Default implementation forwards to Koan event bus; alternative sinks (logging, telemetry) may be layered. Observers registered via `ICanonRuntime.RegisterObserver(...)` can instrument every pipeline phase.

## Edge Cases
- Source payload lacks required aggregation keys → validation hook rejects and parks record with actionable diagnostics.
- Conflicting aggregation data (two sources claiming same key) → aggregation hook sends both to healing queue, while selecting deterministic canonical owner.
- Massive payload or streaming data → ingestion hook can stream to blob storage and inject pointer before pipeline continues.
- Parent-child relationship unresolved → aggregation hook attempts external ID resolution; failing that, parks record with dependency metadata.
- Transport listener outage → distribution hook retries via resilient `ICanonTransportPublisher` or stores message for later replay.

## Implementation Plan
1. **Foundation**
   - Introduce package layout, `ICanonMetadataProvider`, and descriptor generation.
   - Implement `CanonMetadata`, `CanonIndex`, `CanonStage<T>`, and the default `ICanonStore<T>` that uses them.
   - Scaffold `ICanonPipelineBuilder` and default step contracts with no-op implementations.
2. **Engine & Runtime**
   - Build the orchestrator that executes the ordered steps, including observer notifications.
   - Implement `ICanonRuntime` plus entity extension methods (`Canonize`, `SendForCanonization`, `StageForCanonization`).
   - Provide CLI wrappers for canonize, replay, and projection rebuild operations.
3. **Transport Adapters**
   - Extract messaging bridge into `Koan.Canon.Transport.Messaging`, translating envelopes into pipeline calls.
   - Supply adapter hooks so other transports (HTTP, streaming) can be plugged in later without engine changes.
4. **Feature Completeness**
   - Port existing behaviors (standardization, aggregation, policy enforcement, projection) into step implementations that operate on the simplified data model.
   - Replace polling workers with event-driven processors that drain `CanonStage<T>` or react to transport events.
5. **Documentation & Samples**
   - Update docs and samples to demonstrate local-first, distributed, and hybrid deployment patterns.
   - Produce migration guidance for moving existing projects to the new engine.

## Migration Strategy
- Keep legacy pipeline behind a feature toggle while the new engine reaches parity.
- Provide migration scripts that collapse `KeyIndex<T>`, `IdentityLink<T>`, and related tables into `CanonIndex`, and embed reference metadata onto canonical entities.
- Add temporary shims so existing `.Send()` calls map to `Canonize(origin)` or `SendForCanonization()` until teams update their code.
- Swap services to `ICanonRuntime` incrementally, validating outputs with integration tests before disabling legacy components.
- Retire legacy messaging initializers and workers once all consumers run exclusively on the new pipeline.

## Open Questions
- Do we need a declarative DSL for pipeline configuration beyond the builder pattern?
- Should healing queue semantics live inside the engine or remain a transport concern?
- How aggressively should we prune legacy types (e.g., `CanonAction`, `CanonAck`) once adapters migrate?
- Which observability provider (OpenTelemetry, Koan native) becomes the default event sink?

## References
- `docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- `docs/decisions/ARCH-0040-config-and-constants-naming.md`
- Existing proposals in `docs/proposals/` for precedent on structure.
