# Koan.Data.Connector.Mongo Assessment & Couchbase Adapter Implementation Plan

**Status**: Draft
**Author**: Repo AI Assistant
**Date**: 2024-10-31
**Version**: 0.1

---

## Executive Summary

Koan.Data.Connector.Mongo delivers a production-ready document database adapter with first-class DX:

- Opinionated defaults with `auto` connection discovery, naming conventions, and paging guardrails.
- Rich repository surface (`IDataRepository`, LINQ pushdowns, bulk operations, instruction execution) optimized for MongoDB.
- Deep bootstrap integration: conventions and serializers, health contributor, telemetry, orchestration participation, and storage naming defaults.

A Couchbase adapter must meet the same bar while leaning into Couchbase strengths (bucket/scope/collection hierarchy, fast key-value access, N1QL query pushdowns, durability). This document captures the Mongo implementation patterns and outlines a stepwise plan to ship `Koan.Data.Connector.Couchbase` with comparable capabilities and a clean developer experience.

---

## 1. Assessment – Koan.Data.Connector.Mongo

### 1.1 Project layout & build surface
- `Koan.Data.Connector.Mongo.csproj` targets `net9.0` and is packaged as `Sylin.Koan.Data.Connector.Mongo`.
- Entry points live alongside the root namespace (options, repository, registration, telemetry) with supporting folders:
  - `Infrastructure/` for shared constants.
  - `Initialization/` for bootstrap hooks (auto registrar, BSON optimization, custom serializers).
  - `Orchestration/` for Aspire/docker dependency discovery.
  - `Properties/` for manifest metadata.

### 1.2 Options & configuration
- `MongoOptions` provides the user-facing contract: connection string, database, collection naming convention, and paging guardrails.
- `MongoOptionsConfigurator` implements orchestration-aware auto-discovery, reading secrets/user overrides, enforcing defaults, and normalizing URIs.
- `MongoNamingDefaultsProvider` bridges options with `StorageNameResolver` conventions for DX-friendly naming overrides.

### 1.3 Repository capabilities
- `MongoAdapterFactory` exposes the provider via `IDataAdapterFactory`, making it discoverable by name (`mongo`/`mongodb`).
- `MongoRepository<TEntity, TKey>` implements:
  - CRUD and query surfaces (object + LINQ predicates).
  - Capability reporting via `IQueryCapabilities`, `IWriteCapabilities`.
  - Bulk upsert/delete (`IBulkUpsert`, `IBulkDelete`) and batch orchestration with optional transactional writes.
  - Instruction execution (`EnsureCreated`, `Clear`) used by bootstrap and tests.
  - Automatic index provisioning using `IndexMetadata`.
  - Storage optimization awareness (GUID compression) via `StorageOptimizationInfo`.

### 1.4 Initialization & global conventions
- `KoanAutoRegistrar` wires the adapter into the Koan bootstrap:
  - Registers BSON conventions (`IgnoreExtraElements`, `NullBsonValueConvention`, discriminator suppression).
  - Installs a `JObject` serializer for flexible document fields.
  - Registers options, naming defaults, adapter factory, and health contributor.
  - Emits boot reports describing discovery decisions and paging guardrails.
  - Invokes `MongoOptimizationAutoRegistrar` for GUID-aware serialization.
- `MongoOptimizationAutoRegistrar` performs one-time global tuning:
  - Configures GUID serialization and camel-case conventions.
  - Scans entity assemblies to apply smart GUID serializers globally or per type.

### 1.5 Health, telemetry, orchestration
- `MongoHealthContributor` exposes connectivity checks and redacted configuration in Koan health reports.
- `MongoTelemetry` declares an `ActivitySource` for instrumentation.
- `MongoOrchestrationEvaluator` integrates with Koan orchestration/Compose to auto-provision MongoDB containers and environment variables when needed.
- `MongoAdapterFactory` also includes Aspire metadata (`KoanService` attribute) for containerized DX.

### 1.6 Documentation & DX affordances
- README/TECHNICAL docs summarize capabilities, setup, and references to decisions.
- Default naming & paging guardrails enforce best practices without manual configuration.
- Global serializer registration and index auto-creation keep runtime behavior predictable while remaining opt-out capable via options.

### 1.7 Notable patterns to mirror
1. **Options-first DX** – `services.AddKoanOptions<T>()` + configurator layer + naming defaults provider.
2. **Repository contract breadth** – support CRUD, LINQ pushdowns, bulk operations, batch atomicity, instruction execution.
3. **Bootstrap alignment** – auto registrar, health contributor, orchestration evaluator, telemetry source, Aspire metadata.
4. **Optimization hooks** – respect `StorageOptimizationInfo`, apply driver-level tuning (serializer/codec) ahead of repository usage.
5. **Observability** – consistent logging, activity names, boot report contributions.

---

## 2. Couchbase Adapter Goals

- Provide `Koan.Data.Connector.Couchbase` with parity capabilities to Koan.Data.Connector.Mongo, tuned to Couchbase idioms (buckets/scopes/collections, key-value vs. query services, durability constraints).
- Preserve developer ergonomics: minimal configuration (auto discovery when possible), clear documentation, naming conventions, guardrails.
- Integrate seamlessly with Koan bootstrap/orchestration, enabling Aspire Compose deployments and health visibility.
- Optimize performance using Couchbase SDK best practices (cluster/bucket caching, scope-level collection handles, parameterized N1QL, mutation tokens for durability when available).

---

## 3. Implementation Plan – Koan.Data.Connector.Couchbase

### Phase 0 – Foundations & dependencies
1. Add new project `src/Koan.Data.Connector.Couchbase/Koan.Data.Connector.Couchbase.csproj` targeting `net9.0`, referencing `Couchbase.NetClient` (v3+), `Koan.Data.Core`, and required abstractions.
2. Configure packaging metadata aligned with existing adapters (package id `Sylin.Koan.Data.Connector.Couchbase`).
3. Create README/TECHNICAL stubs mirroring Mongo docs with Couchbase-specific guidance.

### Phase 1 – Options & registration surface
1. Define `CouchbaseOptions`:
   - Connection options: connection string/seed nodes, bucket name (required), optional scope/collection overrides, username/password (or use secret providers), durable write settings, query timeout defaults.
   - Naming style + separator for collection naming fallback.
   - Paging guardrails (default/max page size) aligned with DATA-0061.
2. Implement `CouchbaseOptionsConfigurator`:
   - Read configuration keys consistent with Mongo variant (e.g., `Koan:Data:Couchbase:*`).
   - Support auto-discovery via orchestration (Compose/Aspire) with health probes hitting Couchbase Cluster Manager REST or KV ping.
   - Normalize connection strings and apply guardrails.
3. Add `CouchbaseNamingDefaultsProvider` bridging options to `StorageNameResolver` conventions, allowing overrides via `CouchbaseOptions.CollectionName` delegate.
4. Provide `CouchbaseRegistration.AddCouchbaseAdapter(IServiceCollection, Action<CouchbaseOptions>?)` that:
   - Registers options/configurator, naming defaults provider, adapter factory, and health contributor.
   - Ensures SDK-specific global configuration (e.g., serializer settings) runs once.

### Phase 2 – SDK bootstrap & initialization
1. Create `Initialization/CouchbaseAutoRegistrar` implementing `IKoanAutoRegistrar`:
   - Configure default serializers (System.Text.Json or JSON.NET) to respect Koan entity conventions.
   - Register options/configuration components and telemetry.
   - Populate boot report with discovery outcomes, paging guardrails, and durability settings.
2. Add `Initialization/CouchbaseOptimizationInitializer` (if needed) to tune SDK global settings (e.g., ValueTranscoder using `SystemTextJson` with camel case, enabling `NewtonsoftTranscoder` for `JObject` support, string-guid conversions if necessary).
3. Manage cluster/bucket lifecycle via singleton `ICouchbaseClusterProvider` service caching cluster connections and handing out bucket/scope/collection handles safely (use `Cluster.ConnectAsync` with lazy initialization + `IOptionsMonitor<CouchbaseOptions>` updates if necessary).

### Phase 3 – Repository implementation
1. Implement `CouchbaseAdapterFactory` with `[KoanService]` metadata describing Couchbase docker image (`couchbase/server`) and protocol capabilities.
2. Build `CouchbaseRepository<TEntity, TKey>` that matches Mongo repository contracts:
   - Acquire collection handle (bucket + scope + collection). Use naming conventions when collection not explicitly configured.
   - CRUD operations using Couchbase KV operations when possible (`GetAsync`, `UpsertAsync`, `RemoveAsync`) for performance.
   - LINQ-like queries via N1QL: translate `Expression<Func<TEntity, bool>>` using Couchbase LINQ SDK or custom expression visitor to generate parameterized queries. Ensure safe pushdowns or fall back to fetching when unsupported.
   - Implement `QueryAsync(object? query)` to handle raw `QueryRequest` or string N1QL (documented contract) similar to Mongo's no-filter semantics.
   - Provide `CountAsync` using `SELECT COUNT(*)` queries.
   - Respect paging guardrails by applying `LIMIT/OFFSET` when using Koan paging primitives.
   - Implement `IBulkUpsert`/`IBulkDelete` using `Collection.BulkOps` or parallelized tasks with concurrency control; handle durability/persistence requirements via options.
   - Implement `IBatchSet` with optional transactional support using Couchbase transactions API; if transactions unavailable, surface `NotSupportedException` consistent with Mongo.
   - Honor `StorageOptimizationInfo` (e.g., treat GUID string IDs, adjust transcoder to store as binary or keep string per Couchbase best practice).
3. Provide instruction executor support:
   - `EnsureCreated` should create bucket/scope/collection if permitted via Cluster Manager (requires RBAC privileges). For reduced scope, ensure collection exists by issuing `CollectionManager.CreateCollectionAsync` guarded by options toggle.
   - `Clear` should remove all docs within the resolved collection (using N1QL `DELETE FROM bucket.scope.collection` where allowed).
4. Auto-create indexes: use Query Index Management to create primary/indexes based on `IndexMetadata` similar to Mongo. Provide best-effort error handling.

### Phase 4 – Observability & health
1. Implement `CouchbaseTelemetry` with `ActivitySource` similar to Mongo naming (`"Koan.Data.Connector.Couchbase"`).
2. Create `CouchbaseHealthContributor` performing cluster ping (`Cluster.PingAsync`) and returning health report with redacted connection info.
3. Ensure repository methods wrap operations in telemetry spans and structured logging (mirror Mongo activity names: `couchbase.get`, `couchbase.query`, etc.).

### Phase 5 – Orchestration & runtime integration
1. Implement `Orchestration/CouchbaseOrchestrationEvaluator` inheriting `BaseOrchestrationEvaluator`:
   - Determine when to provision Couchbase docker container (likely using `couchbase:enterprise` or community image) with necessary ports (8091-8096, 11210) and initialization commands.
   - Provide environment variables for username/password/bucket creation.
   - Validate discovered hosts via SDK ping or REST API.
2. Extend Aspire metadata in `CouchbaseAdapterFactory` to advertise capabilities/ports.
3. Provide Compose manifests or references if required (matching Mongo's default volume mapping semantics).

### Phase 6 – Documentation, samples, testing
1. Document usage patterns in README (options binding, collection naming, sample queries) and TECHNICAL reference (configuration keys, capabilities, decisions).
2. Add integration tests (or placeholders) in `tests/` mirroring Mongo coverage:
   - Repository CRUD operations (with mocked Couchbase using `Couchbase.MockServer` or integration environment flag).
   - Ensure instructions behave as expected.
3. Update root docs (if necessary) to reference Couchbase adapter and configuration keys.
4. Provide developer setup guidance for local Compose or Docker containers.

### Phase 7 – Rollout & quality gates
1. Ensure analyzers/pre-commit passes (formatting, nullable warnings) and add `EditorConfig` entries if Couchbase SDK introduces new diagnostics.
2. Validate concurrency & disposal with load tests or targeted `BenchmarkDotNet` harness (optional but recommended for parity with Mongo performance).
3. Capture open issues for future enhancements (e.g., FTS support, analytics service queries).

---

## 4. Open Questions & Risks

1. **Couchbase LINQ support** – Evaluate whether to use `Couchbase.Linq` package or author a custom expression translator. Impacts feature completeness and maintenance cost.
2. **Cluster resource management** – Decide between a shared `Cluster` singleton vs. per-repository caching. Need to handle graceful shutdown and credential rotation.
3. **Transaction semantics** – Couchbase distributed transactions require enterprise edition; plan fallback when unavailable.
4. **Collection creation permissions** – Ensure `EnsureCreated` fails gracefully when credentials lack management rights.
5. **Testing strategy** – Determine feasibility of lightweight integration tests (Docker Compose) within CI or rely on mocked interfaces.

---

## 5. Next Steps

1. Socialize this plan with the data platform team for validation, especially around Couchbase operational defaults.
2. Spike a minimal cluster bootstrap + repository subset (Get/Upsert/Delete) to de-risk SDK integration and performance characteristics.
3. Iterate on the remaining phases, tracking progress in project management tooling with milestones aligned to phases above.

