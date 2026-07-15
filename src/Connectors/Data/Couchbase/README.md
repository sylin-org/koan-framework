# Koan.Data.Connector.Couchbase

Couchbase adapter that brings Koan's repository abstractions to Couchbase buckets, scopes, and collections. The adapter mirrors the MongoDB provider surface so document-first applications can swap providers with minimal changes.

## Features

- Options-first configuration with orchestration-aware defaults (`ConnectionString=auto`, `Bucket=Koan`).
- `IDataRepository` implementation with bulk upsert/delete, transactional batches, and instruction execution (`EnsureCreated`, `Clear`).
- LINQ predicate support translated to parameterised N1QL for common comparison and string operators.
- N1QL query support via raw statements or `CouchbaseQueryDefinition` objects.
- Numbered paging and provider-bounded Entity streams through `DataCaps.Query.ProviderBoundedPaging`.
- Health contributor and telemetry activity source (`Koan.Data.Connector.Couchbase`).
- Aspire/orchestration metadata to auto-provision Couchbase containers during local development.

## Streaming boundary

`AllStream` and `QueryStream` request one numbered N1QL page at a time. `batchSize` caps the
Koan-visible candidate page; it does not claim a bound for opaque Couchbase SDK buffers. Streaming
accepts only DATA-0107's first proved user-sort floor: top-level, non-nullable `bool`, `byte`, `sbyte`,
`short`, `ushort`, or `int`. Other user sorts reject before provider I/O. Data.Core separately appends
the usual string Entity identifier as an opaque provider-stable tie-break, not a cross-provider
collation promise.

These streams do not provide snapshot consistency, mutation-safe traversal, resumability, or a public
cursor. Concurrent writes can therefore cause skips or duplicates during offset-based traversal.

See `TECHNICAL.md` for deeper implementation details.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

