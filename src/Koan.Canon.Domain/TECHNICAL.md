---
uid: reference.modules.Koan.canon.domain
title: Koan.Canon.Domain - Technical Reference
description: Runtime, pipeline, persistence, and host-ownership contracts for Koan Canon.
since: source-first
packages: [Sylin.Koan.Canon.Domain]
source: src/Koan.Canon.Domain/
last_updated: 2026-07-15
framework_version: pre-1.0
validation:
  date_last_tested: 2026-07-15
  status: tested
  scope: Canon unit 35/35 and integration 6/6; no distributed-topology claim
---

# Koan.Canon.Domain technical reference

## Contract

- Input: one `CanonEntity<TModel>`, optional `CanonizationOptions`, and an optional model pipeline.
- Output: the materialized canonical entity, metadata, outcome, phase events, and rebuild/distribution
  flags.
- Default: a model without a configured pipeline is persisted through `ICanonPersistence`.
- Errors: invalid options, absent runtime/host composition, missing rebuild targets, contributor
  failures, and persistence failures surface to the caller.
- Success: business code owns canonical rules; the runtime owns deterministic phase order, metadata
  carriage, persistence, observation, and bounded process-local replay.

## Primary surfaces

- `CanonEntity<TModel>`: Entity opt-in with `Metadata`, `State`, and the terse `Canonize(...)` verb.
- `ICanonRuntime`: `Canonize`, `RebuildViews`, `Replay`, and observer registration.
- `CanonRuntimeBuilder`: pipeline, defaults, persistence, audit sink, and replay-capacity composition.
- `CanonPipelineBuilder<TModel>`: ordered steps and contributors for the six fixed phases.
- `CanonizationOptions`: origin, correlation, tags, stage behavior, rebuild, requested views, and
  distribution controls.
- `ICanonPersistence`: the complete canonical storage boundary.

## Persistence boundary

`ICanonPersistence` owns five operations:

1. `GetCanonicalAsync<TModel>` loads a canonical snapshot by id.
2. `PersistCanonicalAsync<TModel>` writes and materializes a canonical snapshot.
3. `PersistStageAsync<TModel>` writes and materializes a deferred stage.
4. `GetIndex` resolves an aggregation index entry.
5. `UpsertIndex` writes an aggregation index entry.

`GetCanonicalAsync` returns `null` only when the record is absent. Availability, authorization,
serialization, and provider failures must propagate; the runtime never translates host failure into
"not found."

`DefaultCanonPersistence` lowers all five operations to Koan Entity/Data. The aggregation contributor
and `RebuildViews` call only the configured persistence contract, so replacing persistence also
replaces prior-state reads. Custom stores should return materialized snapshots rather than mutable
references to internal state.

Adding `GetCanonicalAsync` is an intentional pre-1.0 compatibility-tier change in 0.18. Existing
custom implementations must implement the read before upgrading.

## Host selection

Default persistence needs an active provider containing `IDataService`.

- A generic host composed with `AddKoan()` owns the process-default provider for its lifetime.
- `CanonEntity<T>.Canonize(...)` resolves `ICanonRuntime` from the active provider.
- `Canonize(entity, IServiceProvider, ...)` and `RebuildViews(entity, IServiceProvider, ...)` enter
  `AppHost.PushScope(services)` for the full asynchronous operation and restore the prior flow.
- Direct calls on a supplied `ICanonRuntime` do not imply host ownership. Callers using default
  persistence must already execute inside the intended host flow.
- A custom persistence implementation with no Entity/Data dependency may use a standalone runtime
  when its audit sink and contributors are also host-independent. `DefaultCanonAuditSink` uses Koan Data.

There is no process-static fallback, predecessor-host resurrection, or exception-message parsing.

## Pipeline and ordering

Phases run in enum order: `Intake`, `Validation`, `Aggregation`, `Policy`, `Projection`, then
`Distribution`. Contributors within a phase run in registration order. Default aggregation and policy
contributors are inserted around application steps by the model descriptor.

Aggregation keys create shared `CanonIndex` entries. When an index resolves an existing canonical id,
the runtime loads the prior snapshot through `ICanonPersistence`, merges its metadata, and makes it
available to policy contributors. A dangling index whose canonical id is absent currently behaves as
an absent prior snapshot; tightening that integrity policy is a separate compatibility decision.

## Staging, replay, and topology

- `StageOnly` persists a `CanonStage<T>` and returns before the remaining phases.
- `Replay` enumerates a bounded in-memory queue owned by one runtime. It is not durable event sourcing.
- Phase `CanonizationEvent` values are observations, not Koan Communication events or transport.
- Canon is in-process. Distributed intake, locking, retries, delivery, and recovery require explicit
  application/adapter work and are not implied by a phase name.

## Extensibility and operations

- Use contributors for business rules and projections.
- Use `ICanonAuditSink` for policy audit output.
- Replace `ICanonPersistence` only as one complete unit.
- Bound retained records with `SetRecordCapacity`; the default is 1024.
- Apply authorization at application/Web boundaries, especially for rebuild and replay endpoints.
- Treat provider concurrency and transaction guarantees as properties of the selected persistence.

## References

- [Canon pillar reference](../../docs/reference/canon/index.md)
- [ARCH-0058 - Canon runtime architecture](../../docs/decisions/ARCH-0058-canon-runtime-architecture.md)
- [Koan host-context contract](../../docs/decisions/ARCH-0108-corrective-host-context-failures.md)
- [Test authoring](../../docs/engineering/test-authoring.md)
