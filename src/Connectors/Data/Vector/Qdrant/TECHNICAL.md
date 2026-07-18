# Sylin.Koan.Data.Vector.Connector.Qdrant — technical contract

## Activation and routing

`QdrantVectorModule` registers the provider, discovery adapter, typed options, HTTP client, orchestration evaluator,
and participation-aware health contributor. The factory identity is `qdrant`. Automatic election uses the shared
vector provider catalog; `[VectorAdapter("qdrant")]` requests it exactly.

Repository instances receive the already-elected factory and source. `VectorAdapterNaming` compiles the collection
name once from entity, source, partition, and active segmentation contributors. `CollectionName` is an explicit
physical-name pin and can therefore bypass those folds.

## Configuration

The authoritative section is `Koan:Data:Qdrant`.

- connection: `Endpoint`, `ConnectionString`, or standard `.NET` `ConnectionStrings:Qdrant`;
- authentication: `ApiKey`;
- schema: `Collection`/`CollectionName`, optional `Dimension`, `Distance`, field-name options;
- behavior: `AutoCreate`, `WaitForResult`, `OnDisk`, `TimeoutSeconds`, and quantization options;
- discovery: `DisableAutoDetection`.

An exact `Endpoint` is authoritative and never replaced by discovery. Otherwise `ConnectionString=auto` uses Koan's
service-discovery candidates and falls back to `http://localhost:6333`.

`Dimension` is deliberately nullable. Ordinary writes create a missing collection from the supplied embedding's
length; explicit pre-creation fails correctively until a dimension is configured.

## Operations and limits

The adapter uses Qdrant's REST API. Arbitrary string identifiers are projected deterministically to UUIDv5 point IDs,
while the original identifier is retained in payload for round-tripping. Metadata filters are translated to native
payload predicates. Export uses the scroll API; search itself does not expose continuation.

`VectorQueryOptions` owns the default (`TopK=10`) and rejects non-positive values. The adapter neither caps nor
rewrites a valid caller value. Backend limits and errors remain Qdrant-owned and are surfaced.

## Health and failures

Discovery validates candidates through `/readyz`, then `/healthz`, then connectivity. Active application readiness
uses `/readyz`. Merely referencing the package does not make Qdrant a critical dependency; selecting it for an
entity/source route does. Authentication, schema, dimension, timeout, and backend errors retain their provider
context. Request cancellation flows to HTTP operations.
