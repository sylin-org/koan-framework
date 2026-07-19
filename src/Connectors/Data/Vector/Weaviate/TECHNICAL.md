# Sylin.Koan.Data.Vector.Connector.Weaviate — technical contract

## Activation and routing

`WeaviateVectorModule` registers the provider, discovery adapter, typed options, named HTTP client, and
participation-aware health contributor. The identity is `weaviate`; `[VectorAdapter("weaviate")]`
requests it exactly.

The selected factory/source is passed to the repository. `VectorAdapterNaming` compiles class names from the entity,
source, partition, and active segmentation contributors; no Weaviate-specific tenancy branch exists.

## Configuration and layered discovery

The authoritative section is `Koan:Data:Weaviate`.

- connection: `Endpoint`, `ConnectionString`, or standard `.NET` `ConnectionStrings:Weaviate`;
- authentication: `ApiKey`;
- index/search: `Metric` and `TimeoutSeconds`;
- discovery: `DisableAutoDetection`.

The exact `Endpoint` key expresses user intent even when its value equals the default, and it is never overwritten by
discovery. Otherwise `ConnectionString=auto` uses the shared discovery pipeline and falls back to
`http://localhost:8080`.

The adapter consumes only `Koan.ZenGarden.Contracts`. If the functional Zen Garden engine is referenced and active,
it contributes a health-checked Weaviate offering candidate. Without it the contract is inert. Weaviate still owns
endpoint normalization, health, schema, and operations; Zen Garden contributes but does not elect.

## Schema, operations, and limits

The adapter creates a class on first write, using `vectorizer=none`; supplied embeddings remain application-owned. It
learns dimension from the first embedding and fails a changed dimension before sending an invalid write.

It implements KNN/hybrid search, supported GraphQL filter translation, cursor continuation, single/bulk writes and
deletes, embedding reads, export, clear, and index statistics. Unsupported filter operators fail at the vector filter
gate. `VectorQueryOptions` owns `TopK=10` and positive validation; the adapter does not clamp valid caller intent.

## Health and failures

Readiness checks Weaviate only after an entity/source route selects it. Package presence alone is optional capability,
not a critical dependency. Authentication, schema, dimension, timeout, GraphQL, and REST failures remain explicit and
cancellation flows through HTTP calls.

See [ARCH-0114](../../../../docs/decisions/ARCH-0114-layered-capability-activation.md) for the layered-capability law.

