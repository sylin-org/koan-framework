# Koan.Data.Couchbase – Capability Delta Assessment

**Status**: Draft  \
**Author**: Repo AI Assistant  \
**Date**: 2024-10-31  \
**Version**: 0.1

---

## Overview

Koan.Data.Couchbase v1.0 establishes core CRUD, bulk upsert/delete, and instruction execution across bucket/scope/collection contexts. This document evaluates the additional work required to reach the short-term v1.1 enhancement targets and the longer-term v2.0 vision.

For each goal we capture:

- **Current State** – What the adapter delivers today.
- **Gap** – Capability, API, or infrastructure work still needed.
- **Considerations** – Design, SDK, operational, or DX notes that should shape implementation.
- **Key Actions** – Concrete tasks to close the gap.

---

## Short-Term Enhancements (v1.1)

### 1. Distributed ACID Transactions
- **Current State**: `IBatchSet` explicitly rejects `RequireAtomic` batches, and repository operations rely on individual KV mutations without transactional guarantees.
- **Gap**: No integration with Couchbase Transactions (requires `Couchbase.Transactions` package and orchestrating transaction scopes across mixed operations).
- **Considerations**:
  - Decide between SDK-managed transactions vs. best-effort multi-document operations.
  - Ensure transaction durability settings align with `CouchbaseOptions` (e.g., durability level, timeout).
  - Provide graceful fallback when the Transactions service is not available in the cluster.
- **Key Actions**:
  1. Add transactions package/reference and configure a shared `ITransactions` singleton.
  2. Extend `CouchbaseBatch` to route `RequireAtomic` workloads through a transaction lambda, translating queued mutations into transactional operations.
  3. Surface transaction-related diagnostics (activity names, logs) and configuration toggles in options.
  4. Update documentation and tests to cover transactional semantics and failure modes.

### 2. LINQ Expression Support
- **Current State**: Expression-based queries throw `NotSupportedException`; only raw `CouchbaseQueryDefinition`/string N1QL is supported.
- **Gap**: No expression tree translation to N1QL.
- **Considerations**:
  - Evaluate Couchbase LINQ provider (where available) vs. building a focused expression visitor for basic predicates (`==`, `!=`, range comparisons, logical conjunction/disjunction, pagination, projection of full entity).
  - Maintain parity with Mongo adapter semantics for unsupported expressions (fallbacks or descriptive errors).
  - Ensure generated N1QL respects collection scope naming and parameterization to avoid injection.
- **Key Actions**:
  1. Introduce a LINQ translation layer (either integrate `Couchbase.Linq` or author a minimal translator) producing `CouchbaseQueryDefinition` with named parameters.
  2. Implement `QueryAsync(Expression<Func<TEntity, bool>> predicate, ...)` and `CountAsync(Expression<...>)` using the translator.
  3. Add unit tests covering equality, inequality, range, nested property access, and combined predicates.
  4. Document supported expression patterns and limitations.

### 3. Parallelized Bulk Operations
- **Current State**: `UpsertManyAsync` and `DeleteManyAsync` iterate sequentially, issuing one SDK call at a time.
- **Gap**: No concurrency or batching strategy; throughput is limited for large workloads.
- **Considerations**:
  - Balance parallelism with Couchbase cluster limits and backpressure; expose configurable degree of parallelism.
  - Ensure deterministic error handling (aggregate failures, partial success reporting).
  - Integrate with optional durability requirements and mutation token tracking.
- **Key Actions**:
  1. Introduce configurable bulk execution options (e.g., `CouchbaseOptions.BulkParallelism`, `BulkRetryOptions`).
  2. Replace sequential loops with partitioned parallel execution (TPL Dataflow, `Parallel.ForEachAsync`, or custom Task batching) while honoring cancellation tokens.
  3. Capture per-item failures and surface a rich `BulkOperationException` with contextual diagnostics.
  4. Add metrics/telemetry for bulk throughput and error counts.

### 4. Full-Text Search (FTS) Integration
- **Current State**: Repository exposes only KV and N1QL query paths; no FTS API access or abstractions.
- **Gap**: No way to execute FTS queries or manage indexes.
- **Considerations**:
  - Determine API surface (new repository extension methods vs. dedicated `IFullTextSearchRepository`).
  - Handle FTS index creation/instruction support and align with orchestrated deployments (ensuring Search service availability).
  - Map FTS results (scoring, highlights) into Koan abstractions without breaking existing contracts.
- **Key Actions**:
  1. Add FTS client dependencies (`Cluster.SearchQueryAsync`) and extend options for default index naming.
  2. Implement search methods (e.g., `SearchAsync`) returning result envelopes with score/highlight data.
  3. Extend instruction executor to manage FTS index provisioning when configured.
  4. Provide documentation and samples demonstrating FTS usage and deployment prerequisites.

---

## Long-Term Vision (v2.0)

### 1. Analytics Service Integration
- **Current State**: Adapter queries only the Query service; no analytics dataset awareness.
- **Gap**: No abstractions for analytics datasets, nor routing of analytical workloads.
- **Considerations**:
  - Determine whether to surface analytics via dedicated repository APIs or integration with Koan reporting modules.
  - Manage dataset creation/refresh and service availability checks.
- **Key Actions**:
  1. Extend options to describe analytics datasets/buckets and credentials.
  2. Introduce an analytics execution surface (e.g., `ExecuteAnalyticsAsync`) with schema inference or typed projections.
  3. Provide instruction hooks for dataset creation and refresh scheduling.
  4. Integrate telemetry and health checks for analytics service readiness.

### 2. Vector Search Support
- **Current State**: No vector indexing or query capabilities.
- **Gap**: Await Couchbase vector search general availability; repository lacks embeddings storage conventions.
- **Considerations**:
  - Track Couchbase roadmap; align with Koan AI abstractions when available.
  - Plan for embedding storage (binary vs. JSON array), dimension metadata, and hybrid search (vector + keyword).
- **Key Actions**:
  1. Prototype schema conventions for vector fields and similarity scores.
  2. Add adapter extension points to invoke Couchbase vector search APIs once released.
  3. Develop evaluation harness comparing recall/latency across workloads.
  4. Update documentation with migration guidance from FTS to hybrid/vector search.

### 3. Advanced N1QL Builder (Fluent API)
- **Current State**: Consumers supply raw N1QL or simple query definitions.
- **Gap**: No fluent builder for complex N1QL generation akin to LINQ providers.
- **Considerations**:
  - Ensure builder remains composable yet type-safe, avoiding the need for reflection-heavy solutions.
  - Support joins, aggregations, subqueries, and parameterization.
  - Decide whether builder is adapter-specific or shared across data providers.
- **Key Actions**:
  1. Design fluent DSL covering SELECT/WHERE/GROUP/ORDER constructs with parameter binding.
  2. Implement builder-to-string translation with validation.
  3. Provide integration hooks so repository methods accept builder instances.
  4. Author comprehensive docs and examples, highlighting migration from raw N1QL strings.

### 4. Multi-Collection Joins
- **Current State**: Repository operates on a single collection per entity type.
- **Gap**: No abstractions for joins across collections/scopes/buckets.
- **Considerations**:
  - Align with Koan domain modeling (aggregate roots vs. denormalized documents) to avoid encouraging anti-patterns.
  - Manage index requirements and query performance.
- **Key Actions**:
  1. Extend repository contracts or introduce specialized query surfaces supporting joins.
  2. Build schema metadata describing related collections and required indexes.
  3. Add helper APIs to register and validate join configurations at startup.
  4. Create orchestration tooling to ensure necessary indexes exist before runtime usage.

---

## Summary of Effort

- **v1.1** focuses on enhancing the existing repository surface with transactions, developer-friendly query composition, higher-throughput bulk operations, and search capabilities.
- **v2.0** expands the adapter into advanced analytics and query-building scenarios, preparing for emerging Couchbase features (vector search) and richer domain modeling support.

Each item above should be tracked as its own backlog epic to allow iterative delivery while maintaining adapter stability.

