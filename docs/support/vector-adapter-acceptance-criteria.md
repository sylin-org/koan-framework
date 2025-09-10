# Vector Adapter Acceptance Criteria

This document defines testable acceptance criteria for Sora Vector adapters. It complements the authoring checklist and testing guide with normative MUST/SHOULD/MAY language, and ties behavior to provider capabilities. It is the vector analogue to "Data Adapter Acceptance Criteria" and should be read alongside the Vector Search guide and ADR DATA-0054.

Scope: Adapters that expose vector similarity search over entities (IEntity<TKey>) via the Vector Search contracts.

Audience: Adapter authors and maintainers; reviewers; test writers.

References:

- Vector Search contracts and usage: ../guides/adapters/vector-search.md
- ADR DATA-0054: ../decisions/DATA-0054-vector-search-capability-and-contracts.md

## Terms

- MUST: required for acceptance.
- SHOULD: strongly recommended; acceptable to defer with rationale.
- MAY: optional.

## 1) Contract surface and discovery

- MUST implement the vector repository surface as defined by the guide/ADR, e.g. `IVectorSearchRepository<TEntity, TKey>` (or the current contract name), returning `VectorQueryResult<TKey>` from search paths.
- MUST advertise vector capabilities (e.g., `VectorCapabilities`) including, but not limited to:
  - `Knn` (pure vector search), `Hybrid` (vector + keyword/BM25), `Filters`, `Metadata`, `PaginationToken`, `StreamingResults`, `BulkUpsert`, `BulkDelete`, `AtomicBatch`, `MultiVectorPerEntity`, `ScoreNormalization`.
- MUST register provider discovery via the adapter’s initializer and declare priority with `ProviderPriorityAttribute` where needed.
- MUST bind options from `IConfiguration` using the established helper patterns; provide sensible defaults; do not crash if `IConfiguration` is absent in non-host apps.
- MUST declare and honor distance metric options (e.g., cosine, dot product, euclidean) and the embedding `Dimension`. If the provider enforces a specific metric/dimension, the adapter MUST validate and fail fast with an actionable error when mismatched.

## 2) Entity mapping and identifiers

- MUST use `Identifier` as the vector record key. If the provider requires a separate internal id, the adapter MUST maintain a 1:1 mapping with the entity `Identifier` and ensure uniqueness.
- SHOULD support storing/retrieving lightweight metadata (tags/fields used for filtering). If the provider stores the full document, the adapter MUST bound metadata size by configuration and document limits.
- MUST remain compatible with the entity’s source-of-truth storage (if any). Hydration of full entities from another repository is out of scope for the vector adapter but SHOULD be supported via documented composition patterns.

## 3) Index management

- MUST implement idempotent index bootstrap via instructions (e.g., `vector.index.ensureCreated`). Running it multiple times MUST be safe.
- SHOULD expose additional instructions where supported: `vector.index.rebuild`, `vector.index.stats`, `vector.index.clear/deleteAll`.
- MUST parameterize index configuration via options or instruction parameters: `Dimension`, `Metric`, index type/params (e.g., HNSW/IVF settings), shards/replicas if applicable.
- MUST NOT perform destructive actions by default. Destructive operations (drop/recreate) MUST require explicit opt-in (e.g., `AllowDestructive`) and clear documentation.

## 4) Write and delete operations

- MUST support upserting vectors for entities: `UpsertAsync(entityId, embedding, metadata?, cancellation)`; bulk variants SHOULD be available and efficient.
- If the adapter offers embedding generation, it MUST be optional; the primary contract MUST accept a caller-provided embedding. Integration with external embedders SHOULD use DI and be clearly separated.
- MUST support deleting by id and deleting many. If the provider supports native bulk delete/upsert, the adapter MUST implement it and set capability flags accordingly. Otherwise, fall back to efficient batching without setting bulk flags.
- MUST propagate `CancellationToken` in all write/delete paths; cancellation SHOULD abort IO promptly.
- MUST honor `IBatchSet` semantics when a batch API is exposed: `RequireAtomic=true` executes transactionally if supported; if not supported, MUST fail with NotSupported; best-effort mode records per-item outcomes.

## 5) Search behavior

- MUST implement KNN similarity search over a query vector (`float[]`) with a `TopK` parameter. The result MUST include ordered matches with identifiers and scores/distances.
- MUST document score semantics clearly (higher-is-better vs lower-is-better) according to the distance metric; results MUST be deterministically ordered by score then by a stable tiebreaker.
- MUST support filters if the capability is advertised. Filters MUST be pushed down server-side; the adapter MUST NOT implement client-side full-scan fallbacks.
- Hybrid search (vector + keyword) MAY be supported. If advertised, the adapter MUST document how weights are combined and what filter semantics apply.
- Paging:
  - If the provider supports cursor/pagination tokens: MUST expose and honor `ContinuationToken` and `PageSize` in options/results.
  - If only `TopK` is supported: MUST enforce `TopK` guardrails (see Section 7) and MUST NOT claim accurate totals.
  - MUST NOT fetch or materialize unbounded result sets on the client.
- Counts and totals: Vector stores rarely provide exact counts for filtered KNN. Adapters MUST NOT return misleading totals; they MAY return `Unknown` or `Estimated` with clear labeling if supported.
- Cancellation: MUST propagate `CancellationToken` for search; mid-query cancellation MUST abort work and surface `TaskCanceledException`.

## 6) Capabilities and feature flags

- MUST accurately report capabilities. A capability set to true means the adapter will perform the feature natively (no client fallbacks for search/filtering/paging).
- Distance metrics: MUST expose supported metrics and validate per-index usage.
- Multi-vector per entity: If supported, MUST define how vectors are named/typed per entity and how searches target vectors (e.g., default vs named vector).

## 7) Guardrails and limits

- MUST provide guardrails via options:
  - `DefaultTopK` (applied when caller omits `TopK`) and `MaxTopK` (upper bound). Enforce server-side where possible; else enforce before sending.
  - Query timeout per search; default SHOULD be conservative in production; respect an environment-level override if present.
  - Max metadata size per record; reject or truncate with clear policy.
- SHOULD provide a production safety policy to disable expensive hybrid scoring or overly broad filters if the provider has known footguns; document defaults and overrides.

## 8) Naming and index resolution

- MUST resolve physical index/collection names via `StorageNameRegistry` and a provider `INamingDefaultsProvider` where appropriate. Do not scatter hard-coded names.
- SHOULD support set-based routing/suffixing (e.g., per-tenant/per-environment) consistent with the broader naming conventions.

## 9) Diagnostics, health, and observability

- Logging: SHOULD log at Debug/Information for lifecycle events (bootstrap, index ops) and at Warning/Error for failures. Avoid logging embeddings or sensitive metadata.
- Tracing/Metrics: MUST participate in OpenTelemetry if present using `ActivitySource`, with `db.system` reflecting the provider (e.g., weaviate, pinecone). Use operation names like `vector.upsert`, `vector.search`, `vector.delete`, `vector.index.ensureCreated`. Do not record raw vectors.
- Health: MUST provide an `IHealthContributor` readiness probe (e.g., a trivial ping/version call) and register it in the adapter initializer.

## 10) Options, configuration, and defaults

- MUST bind options from configuration sections named consistently (e.g., `Sora:Data:<Adapter>`). Provide sane dev defaults (host, port, scheme), and respect environment overrides.
- MUST fail fast with actionable messages when required configuration (endpoint, API key, metric, dimension) is missing or inconsistent.
- SHOULD allow configuring index parameters (metric, dimension, replicas) per logical set/index via named options.

## 11) Error semantics and safety

- MUST translate provider-specific errors to meaningful exceptions/results aligned with Sora contracts (not-found vs validation vs auth). Include remediation hints where possible.
- MUST avoid partial writes in atomic batch mode. In best-effort mode, MUST report per-item outcomes and accurate counts.
- MUST validate inputs (dimension length equals index dimension; numeric ranges) and reject early with clear messages.
- MUST ensure all external inputs are parameterized/encoded safely for provider APIs; avoid injection vulnerabilities in hybrid query components.

## 12) Acceptance tests (minimum)

Adapters MUST include tests that verify the following:

- Capabilities: vector capability flags accurately reflect native support (Knn, Filters, Hybrid, PaginationToken, Bulk\*, AtomicBatch, etc.).
- Index: `vector.index.ensureCreated` is idempotent; `vector.index.stats`/`clear` where applicable.
- Upsert/Delete: single and bulk upsert/delete; metadata round-trip limits; dimension validation.
- Search: KNN returns correct identifiers ordered by score; the self-vector ranks highest for its own embedding; score monotonicity matches the metric.
- Filters: filtered search returns only matching entities; if Filters capability is false, attempting a filtered search returns NotSupported.
- Paging: `TopK` guardrails enforced; when `ContinuationToken` is supported, paging through multiple pages works and terminates.
- Batch: `RequireAtomic=true` behaves transactionally if supported; otherwise returns NotSupported; best-effort mode records partial failures.
- Cancellation: mid-search and mid-bulk cancellation raises `TaskCanceledException` and stops work.
- Health: health contributor passes under normal conditions and fails with clear diagnostics when broken.

## 13) Delivery checklist (PR gate)

Use this checklist in PRs for new or updated vector adapters. All MUST items are required.

- [ ] Contracts implemented (`IVectorSearchRepository` and related models; capabilities exposed)
- [ ] Options bound with defaults; discovery/priority registered; metric/dimension validated
- [ ] Index ensure-created/management instructions; destructive operations are opt-in only
- [ ] Upsert/Delete single and bulk paths implemented; Atomic batch honored when supported; cancellation flows
- [ ] Search: KNN implemented with score semantics documented; filters/hybrid as per capabilities; no client-side full-scan fallbacks
- [ ] Guardrails: DefaultTopK/MaxTopK/timeouts wired and documented
- [ ] Naming via `StorageNameRegistry`/`INamingDefaultsProvider`
- [ ] Diagnostics: OTel spans; safe logging; health contributor registered
- [ ] Tests cover capabilities, index, upsert/delete, search, filters, paging, batch, cancellation, health
- [ ] Docs updated (adapter README/notes if applicable)

— End —
