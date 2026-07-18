---
uid: reference.modules.Koan.data.abstractions
title: Koan.Data.Abstractions - Technical Reference
description: Provider-facing entity, query, result, and capability contracts.
since: 0.2.x
packages: [Sylin.Koan.Data.Abstractions]
source: src/Koan.Data.Abstractions/
last_updated: 2026-07-17
framework_version: source-first
validation:
  date_last_tested: 2026-07-15
  status: reviewed
  scope: public contract source inventory; runtime behavior remains owned by Data.Core and adapter suites
---

## Contract

- This project defines the boundary shared by Data.Core and data adapters. Application-facing
  `Entity<T>` statics live in `Koan.Data.Core`, not here.
- `IDataRepository<TEntity, TKey>` owns key reads, writes, bulk operations, readiness, and repository
  creation semantics.
- `IQueryRepository<TEntity, TKey>` accepts one structured `QueryDefinition` and returns a
  `RepositoryQueryResult<TEntity>` that reports which query axes the provider handled.
- Adapters declare optional behavior through `DataCaps` and the shared `CapabilitySet`; consumers
  negotiate those facts instead of inferring support from a provider name.

## Key types

- `IEntity<TKey>` — the minimal entity identity contract.
- `IDataRepository<TEntity, TKey>` — provider CRUD, bulk, readiness, and batch boundary.
- `IQueryRepository<TEntity, TKey>` — structured query and count execution.
- `QueryDefinition` — filter AST, sort, projection, page, partition, and optional count strategy.
- `RepositoryQueryResult<TEntity>` and `CountResult` — values plus execution/estimate facts.
- `DataCaps` — query, write, isolation, and retention capability tokens.
- `PatchPayload<TKey>` and `PatchOp` — the provider-neutral patch operation accepted by Data.Core.
- `IFieldTransform`, `IFieldTransformContributor`, and `IFieldTransformInspector` — neutral contracts for
  host-compiled round-trip storage transforms and cross-pillar inspection.

## Stored-field transform boundary

- A functional module contributes an `IFieldTransformContributor` through standard DI. Data.Core owns compilation,
  ordering, per-type memoization, clone-before-write, and reverse-on-read placement.
- A contributor returns `null` for Entity types it does not affect. Stable contributor ids and order are diagnostic
  and composition inputs, not application configuration.
- `IFieldTransformInspector` lets another pillar make a safe structural decision without referencing the functional
  module. Cache uses it to exclude transformed Entity types; it does not learn Classification internals.
- These contracts do not authorize adapters to apply, omit, or reorder transforms. Supported application paths enter
  through Data.Core; direct repository use is outside that facade guarantee.

## Projection isolation

- This contract assembly does not reference ASP.NET Core or a JSON Patch library.
- HTTP and agent projections parse their native request formats and normalize them to `PatchPayload<TKey>` before
  calling Data. Adapters therefore implement Data semantics without inheriting protocol machinery.
- `PatchPayload<TKey>` is the one Data patch shape; projection-specific request documents and media types remain with
  their projection owners.

## Query ownership

- Data.Core plans pushdown and residual work. An adapter translates and executes the filter it is
  given; it does not invent a second query planner.
- `QueryDefinition.CountStrategy == null` means no total was requested. Pagination alone must not
  cause an adapter to add count work.
- `DataCaps.Query.ProviderBoundedPaging` means the adapter faithfully executes the
  coordinator-supplied pushable candidate filter and applies the requested candidate page before
  application materialization. Each result must report provider-handled pagination and the complete
  total order. It does not promise cursor resumption, snapshot isolation, or mutation-safe iteration.
- There is no public `Pager`, cursor, resume-token, or provider stream primitive in this assembly.
  Data.Core composes its qualified `AllStream`/`QueryStream` facade from numbered provider pages.

## Error and compatibility posture

- Unsupported optional behavior should be reported through capability negotiation or a corrective
  `NotSupportedException`, not silently approximated as a stronger guarantee.
- Provider errors are adapter-owned; shared contracts do not prescribe one universal exception
  wrapper.
- Adding a capability token is not sufficient by itself: the adapter must implement and test the
  advertised behavior.

## References

- [DATA-0107 — provider-bounded Entity streams](https://github.com/sylin-org/Koan-framework/blob/main/docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [DATA-0096 — unified filter pipeline](https://github.com/sylin-org/Koan-framework/blob/main/docs/decisions/DATA-0096-unified-filter-pipeline.md)
- [ARCH-0084 — unified capability model](https://github.com/sylin-org/Koan-framework/blob/main/docs/decisions/ARCH-0084-unified-capability-model.md)
- [ARCH-0040 — configuration and constants naming](https://github.com/sylin-org/Koan-framework/blob/main/docs/decisions/ARCH-0040-config-and-constants-naming.md)
