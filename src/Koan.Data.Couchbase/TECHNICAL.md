# Koan.Data.Couchbase Technical Notes

## Options
- `ConnectionString` supports `auto` orchestration discovery and falls back to `couchbase://localhost` outside containers.
- `Bucket`, `Scope`, `Collection` map to Couchbase hierarchy; naming conventions default to namespace-based collection names.
- Paging defaults (`DefaultPageSize`, `MaxPageSize`) enforce DATA-0061 guardrails.
- `DurabilityLevel` (string) can be set to Couchbase durability levels when stronger write semantics are required.

## Repository
- `CouchbaseRepository<TEntity, TKey>` implements `IDataRepositoryWithOptions` plus bulk operations.
- Keys are normalized to strings, respecting storage optimization hints for GUID identifiers.
- N1QL execution uses `CouchbaseQueryDefinition` to pass statements + parameters; string queries are accepted for quick usage.
- `EnsureCreated` delegates to the bucket collection manager to create scopes/collections when required.
- `DeleteAll` issues a `DELETE ... RETURNING META().id` statement and returns affected count.

## Initialization
- `CouchbaseAutoRegistrar` wires options, naming defaults, adapter factory, health contributor, orchestration evaluator, and cluster provider.
- `CouchbaseClusterProvider` lazily connects to the cluster, caches scope/collection handles, and is shared across repositories.

## Observability
- Telemetry spans use activity names prefixed with `couchbase.*`.
- Health contributor performs a `PingAsync` using the shared cluster connection and reports anonymised connection details.

## Orchestration
- `CouchbaseOrchestrationEvaluator` reuses `BaseOrchestrationEvaluator` for smart host detection. Auto mode provisions `couchbase:community` when no host is found or credentials mismatch.
- Dependency descriptors expose KV + query ports and default environment variables for bootstrap automation.
