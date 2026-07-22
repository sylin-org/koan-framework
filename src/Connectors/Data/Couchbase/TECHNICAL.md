---
uid: reference.modules.Koan.data.couchbase
title: Koan.Data.Connector.Couchbase - Technical Reference
description: Couchbase adapter for Koan Entity data.
packages: [Sylin.Koan.Data.Connector.Couchbase]
source: src/Connectors/Data/Couchbase/
last_updated: 2026-07-22
---

## Contract

- Referencing the connector makes provider `couchbase` (priority 30) eligible for Data election.
  `AddKoan()` performs all registration.
- The selected factory owns source routing and cluster-provider pooling. Repository operations resolve
  names through that selected provider and never re-enter election.
- `All()` is unpaged. Couchbase appends `LIMIT`/`OFFSET` only when the caller supplies explicit
  pagination.
- The adapter declares `DataCaps.Query.ProviderBoundedPaging` for qualified Entity streams.

## Configuration

Default-source connection configuration is resolved from:

1. `Koan:Data:Couchbase:ConnectionString`
2. `ConnectionStrings:Couchbase`
3. `Koan:Data:Sources:Default:couchbase:ConnectionString`
4. the `CouchbaseOptions.ConnectionString` default (`auto`)

Bucket and optional credentials use `Koan:Data:Couchbase:{Bucket,Username,Password}` or their
provider-specific Default-source equivalents. `Scope`, `Collection`, `QueryTimeoutSeconds`,
`Durability`, and `ManagementUrl` are provider-level native settings. Named sources use
`Koan:Data:Sources:{name}:{Adapter,ConnectionString}` and provider settings under that source.
Generic cross-provider connection, credential, bucket, and user aliases are not consumed.

## Storage and routing

- The Default source reuses the DI-managed `CouchbaseClusterProvider`.
- Named sources with the same resolved cluster identity reuse it. Distinct physical placements receive
  one factory-owned provider per `(connection, bucket, username, password, management URL)`.
- An Entity collection name comes from the already selected naming provider and is normalized to
  Couchbase's identifier rules.
- Ambient Data partitions map to native scopes; named Data sources map to native buckets.
- Scope/collection ensure is memoized and driver handles are cached for the provider lifetime.

## Query and write behavior

- Supported filters lower to parameterized N1QL; unsupported residual work is coordinated by Data
  Core.
- Explicit pages use N1QL `LIMIT`/`OFFSET`; no adapter page default or cap exists.
- Raw provider access accepts a N1QL string plus parameters through `Entity.QueryRaw`/Data Core. The
  connector's query-definition wrapper is internal implementation detail.
- Bulk operations, CAS conditional replace, and transactional batches use Couchbase-native mechanics.
  `BatchOptions.RequireAtomic` requests the transaction guarantee explicitly.

## Initialization and readiness

`CouchbaseDataModule` registers options, discovery, the adapter factory, the shared
cluster provider, and health through `AddKoan()`. It does not register Couchbase as an eager
`IAsyncAdapterInitializer` or global readiness alias.

Availability is not participation. A referenced but unelected connector reports non-critical
`Unknown` health and remains cluster-connection free. Couchbase becomes critical when it wins default
election or a runtime repository/direct-source request selects it. Selected use initializes the
cluster and bucket once through a shared lazy task; health probes use the same provider route.

A fresh real Couchbase instance can take substantial time to initialize management, query/index
services, bucket access, and SDK bootstrap. The current focused run observed about 64 seconds for the
first selected CRUD use; subsequent operations reuse the host-owned provider. This is current
operational evidence, not a latency guarantee.

## Observability

Activities use the `Koan.Data.Connector.Couchbase` source and include Entity, selected source, and
ambient partition. Health probes use SDK `PingAsync`. Startup and facts report effective storage
targets and de-identify connection strings; credentials are never emitted.

## Provider-bounded streaming

`AllStream` and `QueryStream` are coordinated as lazy numbered pages by Data Core. `batchSize` bounds
the Koan-visible candidate page, not opaque SDK buffers. DATA-0107's exact user-sort floor applies;
other sorts reject before provider I/O. Offset traversal is not snapshot isolation, resumable, or
mutation-safe.

## Evidence boundary

The complete connector suite passes 20/20 with zero skips against a real Couchbase Community 8.0.2
container. It covers CRUD, native filter convergence, provider-bounded streaming, conditional
replace, all three declared AODB isolation modes, source-provider deduplication, identifier safety,
and participation-owned readiness. The observed run took 7 minutes 21 seconds because fresh selected
uses repeatedly wait for cluster/query/index readiness; that is provider-specific operational
evidence, not a normal merge-gate requirement or a latency guarantee.

First-publication pack and external-consumer evidence is recorded by
[R13-10](../../../../docs/initiatives/koan-v1/work-items/r13/R13-10-couchbase-provider-promotion.md).

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)
