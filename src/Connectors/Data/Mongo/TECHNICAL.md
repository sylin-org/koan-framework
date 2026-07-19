---
uid: reference.modules.Koan.data.mongo
title: Koan.Data.Connector.Mongo - Technical Reference
description: MongoDB adapter for Koan Entity data.
packages: [Sylin.Koan.Data.Connector.Mongo]
source: src/Connectors/Data/Mongo/
last_updated: 2026-07-17
---

## Contract

- Referencing the connector makes provider `mongo` (alias `mongodb`, priority 20) eligible for Data
  election. `AddKoan()` performs all registration.
- The selected factory owns source routing, Mongo client pooling, and collection naming. Repository
  operations never re-enter provider election.
- `All()` is unpaged. Mongo applies `Skip`/`Limit` only when the caller supplies explicit pagination.
- The adapter declares `DataCaps.Query.ProviderBoundedPaging` for qualified Entity streams.

## Configuration

Default-source configuration is resolved from the first applicable provider-specific declaration:

1. `Koan:Data:Mongo:ConnectionString`
2. `ConnectionStrings:Mongo`
3. `Koan:Data:Sources:Default:mongo:ConnectionString`
4. the `MongoOptions.ConnectionString` default (`auto`)

Database and optional credentials use `Koan:Data:Mongo:{Database,Username,Password}` or their
provider-specific Default-source equivalents. Named sources use
`Koan:Data:Sources:{name}:{Adapter,ConnectionString}` and provider settings under that source.
Generic cross-provider connection aliases are not consumed.

A concrete connection is authoritative. `auto` delegates to the shared health-checked discovery
coordinator. The package describes a `mongo` → `mongodb` Zen Garden binding, but that contract remains
inactive unless the functional `Koan.ZenGarden` engine is referenced and enabled. An active Zen Garden
endpoint is one health-checked candidate, not a short circuit.

## Storage and routing

- The Default source reuses the DI-managed `MongoClientProvider`.
- Named sources with the same resolved connection/database reuse that provider; distinct physical
  placements receive one factory-owned client per `(connection, database)`.
- Collection names are resolved through the already selected `INamingProvider` for the current Entity
  and ambient partition, then cached per resolved collection.
- Schema/index ensure is memoized per physical collection for the host lifetime.

## Query and write behavior

- Supported filters lower to native MongoDB definitions; unsupported residual work is coordinated by
  Data Core.
- Explicit pages use MongoDB `Skip`/`Limit`; no adapter page default or cap exists.
- Bulk upsert/delete, transactional batches, conditional replace, TTL indexes, and fast remove are
  advertised through Data capabilities and implemented with native driver operations.
- BSON conventions and serializers are connector implementation details. Driver registration is
  once-guarded and does not capture an application host or silently change failed document
  serialization into a string payload.

## Readiness and observability

Availability is not participation. A referenced but unelected connector reports non-critical
`Unknown` health and does not connect. Mongo becomes critical when it wins default election or a
runtime repository/direct-source request selects it. Active sources are probed through the same
factory route and client pool used by Entity operations.

Activities use the `Koan.Data.Connector.Mongo` source and include Entity, selected source, and ambient
partition. Discovery and health output de-identify connection strings; raw credentials/endpoints are
not emitted as runtime facts.

## Provider-bounded streaming

`AllStream` and `QueryStream` are coordinated as lazy numbered pages by Data Core. `batchSize` bounds
the Koan-visible candidate page, not opaque driver buffers. DATA-0107's exact user-sort floor applies;
other sorts reject before provider I/O. Offset traversal is not snapshot isolation, resumable, or
mutation-safe.

## Evidence boundary

The current focused connector suite passes 68/68 against MongoDB, including CRUD, routing,
partitioning, filtering, managed-field isolation, field transforms, batching, and stream behavior.
This is connector evidence, not a universal MongoDB production/SLO, migration, or compatibility
certification.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)
- [ARCH-0114 layered capability activation](../../../../docs/decisions/ARCH-0114-layered-capability-activation.md)
