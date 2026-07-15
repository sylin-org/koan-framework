---
uid: reference.modules.Koan.data.abstractions
title: Koan.Data.Abstractions - Technical Reference
description: Provider-facing entity, query, result, and capability contracts.
since: 0.2.x
packages: [Sylin.Koan.Data.Abstractions]
source: src/Koan.Data.Abstractions/
last_updated: 2026-07-15
framework_version: v0.17.0
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

- [DATA-0107 — provider-bounded Entity streams](/docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [DATA-0096 — unified filter pipeline](/docs/decisions/DATA-0096-unified-filter-pipeline.md)
- [ARCH-0084 — unified capability model](/docs/decisions/ARCH-0084-unified-capability-model.md)
- [ARCH-0040 — configuration and constants naming](/docs/decisions/ARCH-0040-config-and-constants-naming.md)
