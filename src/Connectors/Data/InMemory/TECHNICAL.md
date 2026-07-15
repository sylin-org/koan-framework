---
uid: reference.modules.Koan.data.inmemory
title: Koan.Data.Connector.InMemory - Technical Reference
description: Process-local Entity data adapter and conformance oracle.
since: 0.17.0
packages: [Sylin.Koan.Data.Connector.InMemory]
source: src/Connectors/Data/InMemory/
---

## Contract

`InMemoryAdapterFactory` registers provider `inmemory` with priority `-100`. `memory` is a compatible
provider alias. Selection follows Data.Core's normal context, Entity attribute, configured source, and
reference-priority order; the connector adds no separate registration API.

`InMemoryRepository<TEntity,TKey>` is a thin backend over the shared key-value store contract. Its
singleton `InMemoryDataStore` partitions physical dictionaries by routed source, Entity type, and
ambient partition. Process exit discards every dictionary.

## Capabilities

The repository declares:

- `FilterExecutionProfile(InMemory, fullyEvaluated: true)`;
- bulk upsert;
- bulk delete; and
- atomic batch within the process-local store.

It does not declare `DataCaps.Query.ProviderBoundedPaging`. Its query path starts from the resident
full-source dictionary, so slicing a numbered page is not evidence of provider-bounded traversal.

The common key-value family supplies managed-field guards, isolation modes, instructions, and the
provider-neutral Entity repository contract. This connector does not infer remote durability,
distributed atomicity, or production recovery from those shared semantics.

## Streaming boundary

- `AllStream` and `QueryStream` fail correctively with `QueryStreamRejectedException` before yielding;
  there is no complete-result materializing fallback.
- Use `All`/`Query` only for known-small test sets. Use `FirstPage`/`Page` to limit the result returned to
  test code, without inferring an unbounded-data performance guarantee.
- A later resident-incremental implementation must earn a separate capability claim through shared
  conformance before these Entity streams become available.

## Concurrency and isolation

Each physical store is a `ConcurrentDictionary`. Individual key operations and the connector's
declared batch contract are process-local. There is no cross-process coordination, durable journal,
replication, backup, or restart recovery.

`EntityContext.Partition` changes the physical store. Routed data sources also remain distinct. Test
hosts must still own their ambient `AppHost`; `Sylin.Koan.Testing` enters a flow-scoped host around
every inherited Entity battery.

## Evidence

`Koan.Data.Connector.InMemory.Tests` covers CRUD, filtering/capabilities, sorting, batch behavior,
instructions, isolation modes, partitions, host ownership, and managed-field no-leak behavior. The
current suite passes 56/56.

## Unsupported

- persistence after process exit;
- multiple processes or nodes;
- production backup/recovery;
- unbounded-data performance claims; and
- parity with a provider capability the connector does not declare.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)
