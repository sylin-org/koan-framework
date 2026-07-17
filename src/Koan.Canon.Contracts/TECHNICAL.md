---
uid: reference.modules.Koan.canon.contracts
title: Koan.Canon.Contracts - Technical Reference
description: Inert model, metadata, pipeline, persistence, and audit contracts for Koan Canon.
since: source-first
packages: [Sylin.Koan.Canon.Contracts]
source: src/Koan.Canon.Contracts/
last_updated: 2026-07-17
framework_version: pre-1.0
validation:
  date_last_tested: 2026-07-17
  status: tested
  scope: strict build plus Canon unit, integration, bootstrap, and CustomerCanon host tests
---

# Koan.Canon.Contracts technical reference

## Boundary

This assembly is intentionally inert. It contains no `KoanModule`, hosted service, provider election,
controller, or startup side effect. Modules may safely consume its public contracts without activating
Canon.

Its main surfaces are:

- `CanonEntity<T>` and `CanonValueObject<T>` for canonical models;
- `CanonMetadata`, state, lineage, attribution, aggregation, and audit vocabulary;
- `ICanonPipelineContributor<T>` and the six `CanonPipelinePhase` values;
- `ICanonRuntime`, `ICanonPersistence`, `ICanonAuditSink`, and pipeline descriptors;
- `CanonizationOptions`, results, outcomes, and phase events.

`ICanonModel` and `ICanonPipelineContributor` are source-discovery markers. The functional runtime owns
activation and compiles discovered generic contributors into model pipelines once per host.

## Persistence contract

`ICanonPersistence` owns canonical read/write, stage write, and aggregation-index lookup/upsert as one
complete boundary. A `null` canonical read means absent; availability, authorization, serialization,
and provider failures propagate.

## Semantic limits

- A `CanonizationEvent` records a pipeline phase result. It is not a communication event or transport.
- Replay vocabulary does not promise a durable event store.
- Pipeline phase names do not imply network, broker, AI, or distributed execution.

See the [functional runtime](../Koan.Canon/TECHNICAL.md) and
[Canon pillar reference](../../docs/reference/canon/index.md).
