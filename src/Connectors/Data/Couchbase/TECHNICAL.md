# Koan.Data.Connector.Couchbase Technical Notes

## Options
- `ConnectionString` supports `auto` orchestration discovery and falls back to `couchbase://localhost` outside containers.
- `Bucket`, `Scope`, `Collection` map to Couchbase hierarchy; naming conventions default to namespace-based collection names.
- `DefaultPageSize` controls ordinary adapter paging defaults; Entity streams supply their own bounded candidate-page size.
- `DurabilityLevel` (string) can be set to Couchbase durability levels when stronger write semantics are required.

## Repository
- `CouchbaseDocumentStore<TEntity, TKey>` extends the shared document-store family and declares
  `DataCaps.Query.ProviderBoundedPaging`.
- Keys are normalized to strings, respecting storage optimization hints for GUID identifiers.
- Expression predicates are translated into parameterised N1QL via `CouchbaseLinqQueryTranslator` supporting comparisons, logical operators, `Contains`, `StartsWith`, and `EndsWith`.
- Bulk mutations run in parallel with optional durability and leverage Couchbase distributed transactions when `BatchOptions.RequireAtomic` is requested.
- N1QL execution uses `CouchbaseQueryDefinition` to pass statements + parameters; string queries are accepted for quick usage.
- `EnsureCreated` delegates to the bucket collection manager to create scopes/collections when required.
- `DeleteAll` issues a `DELETE ... RETURNING META().id` statement and returns affected count.

## Provider-bounded streaming

- `AllStream` and `QueryStream` are coordinated as lazy numbered pages by Data.Core; Couchbase applies
  `LIMIT`/`OFFSET` in N1QL before candidate documents are materialized into application memory.
- `batchSize` is the maximum Koan-visible candidate page, not a promise about opaque Couchbase SDK
  buffers.
- Every caller-requested stream sort component must be a top-level, non-nullable `bool`, `byte`,
  `sbyte`, `short`, `ushort`, or `int` member. Every other caller sort, including an explicit Entity
  identifier sort, rejects before provider I/O. Data.Core appends the usual string Entity identifier
  only as an opaque provider-stable tie-breaker; that is not a CLR or cross-provider collation promise.
- Offset paging is not snapshot isolation and does not provide mutation-safe traversal, resume tokens,
  or a public cursor.

## Initialization
- `CouchbaseAutoRegistrar` wires options, naming defaults, adapter factory, health contributor, orchestration evaluator, and cluster provider.
- `CouchbaseClusterProvider` lazily connects to the cluster, caches scope/collection handles, and is shared across repositories.

## Observability
- Telemetry spans use activity names prefixed with `couchbase.*`.
- Health contributor performs a `PingAsync` using the shared cluster connection and reports anonymised connection details.

## Orchestration
- `CouchbaseOrchestrationEvaluator` reuses `BaseOrchestrationEvaluator` for smart host detection. Auto mode provisions `couchbase:community` when no host is found or credentials mismatch.
- Dependency descriptors expose KV + query ports and default environment variables for bootstrap automation.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

