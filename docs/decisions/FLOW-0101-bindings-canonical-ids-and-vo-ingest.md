---
id: FLOW-0101
slug: bindings-canonical-ids-and-vo-ingest
domain: flow
status: accepted
date: 2025-08-31
title: Flow bindings, canonical ID resolution, and independent value-object ingest
---

## Context

Sample S8 and similar domains must ingest high-volume value objects (e.g., readings) while keeping perennial aggregates (e.g., devices, sensors) lean. Adapters emit OEM/source identifiers and don’t know canonical IDs. We need a uniform, provider-agnostic way to bind external IDs to canonical IDs, persist lineage, and keep aggregates and VOs in separate stores with distinct lifecycles.

## Decision

1) Split aggregates and value objects into separate Flow models/collections. Aggregates are long-lived; VOs are append-only, time-partitioned, and TTL-managed.

2) Introduce semantic binding hints on models via attributes/registry using centralized keys (FlowBindingKeys). Bindings indicate association keys (ReferenceKey), partitioning (PartitionKey), and metadata propagation (Metadata).

3) Maintain `KeyIndex<T>` mappings from `(namespace, sourceId[, composite]) → canonicalId` for aggregates. On VO ingest, resolve canonical IDs using bindings and `KeyIndex` before persistence; store lineage on all artifacts. On miss, record a `RejectionReport` in diagnostics.

4) Keep the approach provider-agnostic; storage adapters interpret scopes and options (e.g., TTL) without leaking provider terms.

## Scope

Applies to Flow runtime, web controllers, and samples (S8) where value objects arrive independently from aggregates. Does not change authorization or tenant isolation policies.

## Consequences

- Pros: Clean partitioning, fast canonical-ID queries, adapter portability, clear lineage, independent retention and replay.
- Cons: Requires maintaining `KeyIndex` consistency, handling out-of-order/missing parents, and managing composite identities for multi-OEM scenarios.

## Implementation notes

- Typed Flow models only; no legacy runtime.
- Constants for binding keys; no magic strings.
- Startup reflection builds binding descriptors per model; cached in a registry.
- VO ingest: `StageRecord<VO>` → association → canonical persistence → `ProjectionTask<Aggregate>`.
- Views: per-aggregate canonical, plus latest/rolling windows for VO.

## Follow-ups

- Add introspection endpoint to list bindings per model.
- Add options to configure required binding keys per environment.
- Add tests for duplicate/out-of-order handling and idempotency.

## References

- Reference: ../reference/flow-bindings-and-canonical-ids.md
- Decisions: ../decisions/DATA-0061-data-access-pagination-and-streaming.md, ../decisions/WEB-0035-entitycontroller-transformers.md
