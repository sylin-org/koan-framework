# Sylin.Koan.Data.Vector.Connector.Milvus — technical contract

## Activation and routing

`MilvusVectorModule` registers the provider, discovery adapter, options, named HTTP client, and participation-aware
health contributor. The provider identity is `milvus`; `[VectorAdapter("milvus")]` is exact.

The repository receives the selected factory/source and uses `VectorAdapterNaming` once to compile its collection
route. Ambient partition and segmentation contributors therefore affect physical naming without Milvus-specific
tenant logic. `CollectionName` is an explicit pin and bypasses that derivation.

## Configuration

The authoritative section is `Koan:Data:Milvus`.

- connection: `Endpoint`, `ConnectionString`, or standard `.NET` `ConnectionStrings:Milvus`;
- authentication: `Token`, or `Username` and `Password`;
- schema: `Database`, `Collection`, optional `Dimension`, primary/vector/metadata field names, and `Metric`;
- behavior: `Consistency`, `AutoCreate`, and `TimeoutSeconds`;
- discovery: `DisableAutoDetection`.

An exact `Endpoint` is authoritative. Otherwise `ConnectionString=auto` uses Koan discovery and falls back to
`http://localhost:19530`. The adapter uses Milvus REST v2; the server deployment remains responsible for its usual
etcd and object-storage dependencies.

`Dimension` is nullable. The first write creates a missing collection from the supplied embedding length; explicit
pre-creation fails with a correction until a dimension is configured.

## Operations and limits

Implemented operations are ensure/create, single and bulk upsert, single and bulk delete, KNN search with supported
metadata predicates, and collection clear. Embedding retrieval, export, hybrid text search, continuation, and index
statistics are intentionally unclaimed.

`VectorQueryOptions` owns `TopK=10` and positive-value validation. Milvus receives the exact valid value; it may reject
a request that exceeds its own deployment limits rather than Koan silently changing intent.

## Health and failures

Discovery and active readiness use the REST `/v2/health` contract (discovery may fall back to connectivity). The health
contributor probes only after the provider/source participates in a route; reference alone is not a critical readiness
dependency. Authentication, collection, dimension, consistency, timeout, and REST failures retain their Milvus
context, and cancellation reaches outbound requests.

