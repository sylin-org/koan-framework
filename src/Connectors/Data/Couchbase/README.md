# Koan.Data.Connector.Couchbase

Couchbase adapter that brings Koan's repository abstractions to Couchbase buckets, scopes, and collections. The adapter mirrors the MongoDB provider surface so document-first applications can swap providers with minimal changes.

## Features

- Options-first configuration with orchestration-aware defaults (`ConnectionString=auto`, `Bucket=Koan`).
- `IDataRepository` implementation with bulk upsert/delete, transactional batches, and instruction execution (`EnsureCreated`, `Clear`).
- LINQ predicate support translated to parameterised N1QL for common comparison and string operators.
- N1QL query support via raw statements or `CouchbaseQueryDefinition` objects.
- Guardrail-aware paging when `DataQueryOptions` are supplied.
- Health contributor and telemetry activity source (`Koan.Data.Connector.Couchbase`).
- Aspire/orchestration metadata to auto-provision Couchbase containers during local development.

See `TECHNICAL.md` for deeper implementation details.

