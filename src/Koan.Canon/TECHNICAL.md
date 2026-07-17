---
uid: reference.modules.Koan.canon
title: Koan.Canon - Technical Reference
description: Functional Canon activation, discovery, pipeline, persistence, and host ownership.
since: source-first
packages: [Sylin.Koan.Canon]
source: src/Koan.Canon/
last_updated: 2026-07-17
framework_version: pre-1.0
validation:
  date_last_tested: 2026-07-17
  status: tested
  scope: Canon unit 37/37, integration 6/6, non-Web bootstrap 1/1, CustomerCanon host 1/1
---

# Koan.Canon technical reference

## Composition

`CanonModule` activates by package reference during `AddKoan()`. It discovers concrete
`ICanonPipelineContributor<T>` implementations from Koan's generated registry, registers each once,
and compiles deterministic model pipelines once per host. Explicit `AddCanonRuntime(...)` configuration
remains an advanced override and is not part of normal application startup.

Contracts live in `Sylin.Koan.Canon.Contracts`; the functional assembly owns only activation, pipeline
compilation, defaults, runtime execution, and default persistence/audit implementations.

## Runtime law

Phases run as `Intake`, `Validation`, `Aggregation`, `Policy`, `Projection`, and `Distribution`.
Contributors within a phase use `Order`, then type name. One context carries the entity, metadata,
options, services, persistence, stage, and operation-local items.

- `Failed` terminates with `CanonizationOutcome.Failed`; later phases, indexing, and canonical writes do
  not run.
- `Parked` terminates with `CanonizationOutcome.Parked`.
- An unconfigured application model still receives the built-in aggregation/policy behavior and default
  persistence.
- Explicit pipeline configuration replaces the discovered descriptor for that model.

## Host and storage ownership

The runtime, configuration, contributor catalog, persistence, and audit sink are host-owned singletons.
There is no process-wide contributor snapshot. Entity terminal methods resolve the active Koan host;
service-provider overloads establish and restore their host flow for the full asynchronous operation.

`DefaultCanonPersistence` lowers the complete `ICanonPersistence` contract to Koan Entity/Data. Replacing
that contract replaces canonical reads/writes, stage writes, and aggregation-index operations together.
The default audit sink also uses Koan Data.

## Operational limits

- Runtime replay is a bounded process-local queue (default 1024), not durable event sourcing.
- Canon is currently in-process. Distributed locking, delivery, retries, and recovery are not implied.
- Authorization belongs at the application or Web projection boundary.
- Concurrency and transaction guarantees come from the selected persistence implementation.

See the [public pillar reference](../../docs/reference/canon/index.md).
