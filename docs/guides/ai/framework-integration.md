# Native AI Koan — Epic

A cross-cutting initiative to bring native AI capabilities to Koan with the same principles as the rest of the framework: controller-only HTTP, sane defaults, auto-setup, discovery via capability flags, and strong observability. This epic moves from in-app library mode to optional sidecar and central proxy/connector, compounding value at each step.

## Goals

- Turnkey AI inference (chat + embeddings) with streaming, minimal config.
- First-class RAG with Redis-first vector + cache; pluggable vector adapters.
- Strong governance: budgets, moderation/redaction, model allow-lists, safe tool registry.
- Observability-by-default: OTel spans/metrics for tokens, cost, latency; boot report.
- Portable deployment modes: library → sidecar → central proxy (multi-tenant).

## Out of Scope

- Model training/registry, experiment tracking, distributed training. Provide integration guides only.

---

## Low-hanging fruits (Sprint 0)

Focus on strategic fundamentals that unblock later work and reduce churn.

- API contract & schemas
  - Define request/response DTOs for `/ai/chat`, `/ai/embed`, `/ai/rag/query` and error shapes (ProblemDetails) with examples.
  - Pin header contracts (Koan-AI-Provider, Koan-AI-Model, Koan-AI-Streaming, Koan-Session-Id, Koan-Tenant/Project) and SSE event format (event names, data frames, termination semantics).
- Protocol surfaces
  - Draft gRPC proto mirroring the core endpoints for internal S2S; generate C# server/client; keep behind feature flag.
  - Define scope for OpenAI-compatible shim (subset, stability window) to minimize compatibility churn.
- Tokenization & cost
  - Pick tokenizer strategy per provider (native when available; fallback estimator) with accuracy target and unit tests.
- RAG defaults
  - Set chunk size/overlap defaults, embedding version tag, invalidation rules, and citations schema.
- Secrets provider
  - Define abstraction and adapters (Azure Key Vault/AWS Secrets Manager) with per-tenant resolution for proxy mode.
- Vector prioritization
  - Add `pgvector` as a V1.1 milestone (fast-follow) with parity tests; document Redis guardrails (memory caps, persistence, cluster/HA).

Outcome: stable surface area, internal protocol ready, safe defaults for RAG and secrets, and clear vector path.

---

## Epics overview and draft feature sets

- Epic A: Core & Providers
  - Features: Contracts, options, constants; OpenAI + Ollama providers; tokenizer/cost; boot report.
  - Deliverables: Koan.AI.Core, Providers.OpenAI/Local, unit/integration tests, docs.
- Epic W: Web & Protocols
  - Features: REST controllers with SSE; schemas; gRPC internal surface; OpenAI-compatible shim; optional adapters for MCP (Model Context Protocol) and AI-RPC for interoperability.
  - Deliverables: Koan.Web.AI, gRPC proto + server, OpenAPI spec, examples.
- Epic V: Vector & RAG
  - Features: Redis vector + cache; pgvector fast-follow; RAG defaults, ingestion, citations; migration utils.
  - Deliverables: Koan.Data.Vector.Redis/pgvector, S5.AI.Rag sample, probe scripts.
- Epic G: Observability & Governance
  - Features: OTel metrics/spans; budgets; moderation/redaction; model allow-lists; SSE SLOs.
  - Deliverables: meters, dashboards, policies, tests.
- Epic P: Proxy/Connector & Tenancy
  - Features: Sidecar proxy; central proxy with quotas; secrets provider; admin APIs.
  - Deliverables: Koan.Web.AI.Proxy, tenancy headers, usage accounting, key management adapters.
- Epic RU: Platform Ramp-up
  - Features: Redis core cache/session; MySQL relational adapter; CouchDB adapter (optional).
  - Deliverables: Koan.Caching.Redis, Koan.Data.Relational.MySql, Koan.Data.CouchDb.
- Epic E: Messaging & Tools
  - Features: MQ transformers; safe tool registry; audited function calling.
  - Deliverables: handlers, registry, tests, docs.
 - Epic X (On-ramp Priority): Data Bridge & Replication
   - Features: Entity snapshot export/import, CDC via Debezium/Kafka, AI-aware vector indexing, virtualization (composed reads), scheduled materialization, diff/reconcile, connectors interop.
   - Deliverables: Koan.Data.Transfer, Koan.Data.Replication, Koan.Web.Transfer, guides and probes.

---

## Tracks and dependency graph

- On-ramp Track (priority): Platform Ramp-up (OR1) → Data Bridge & Replication (D0–D8) → Event-driven Transformers (E1) → AI-aware Indexer (D3) → Vector & RAG (V1/V1.1/R1).
- Native AI Track: Sprint 0 fundamentals → Core/Providers (A0–A2) → Web & Protocols (A3, W2) → Observability/Governance (A4, G1).
- Proxy/Connector Track: Sidecar (P1) → Central Proxy (T1) with Secrets (S1) and SSE SLOs (P2).

---

## Epic Structure and Stories

Each story is a contained deliverable with clear acceptance criteria; stories build on each other.

### Story A0: API Contracts & Schemas (Sprint 0)

- Deliverables
  - JSON schemas and DTOs for chat, embeddings, RAG; SSE event spec; headers contract list; ProblemDetails error catalog.
  - OpenAPI annotations and example payloads; sample curl/httpie snippets.
- Acceptance Criteria
  - Schemas pass validation; OpenAPI renders with examples; SSE event stream verified in a minimal controller.

### Story A1: AI Core Contracts and Options

- Deliverables
  - Koan.AI.Core: contracts (IChatCompletion, IEmbedding, IToolCall, IVectorStore), AiOptions, capability flags.
  - Constants for headers/keys/tags (Koan-AI-Provider, Koan-AI-Model, Koan-Vector-Provider, ai.* OTel tags).
- Acceptance Criteria
  - NuGet package builds; unit tests for options binding and capability serialization.
  - Docs section describing contracts and headers.

#### Milestones & Deliverables

- Code
  - New project: `src/Koan.AI.Core/` with interfaces, `AiOptions`, `AiCapabilityFlags`, `AiErrorKind`, and DI extension `AddKoanAI()`.
  - Constants: `Infrastructure/AiConstants.cs` (headers, env keys, default models), `Infrastructure/AiTelemetry.cs` (activity names, tags, metric names).
  - Result types: `AiUsage` (tokens/cost), `AiMessage`, `AiToolDefinition`, `AiToolCall`.
- Docs
  - Guides: add “Core concepts” page referencing headers and options; examples of binding options and capability negotiation.
- Tests
  - Options binding (env → options), capability flag round-trip, DI registration, and null/invalid model error mapping.
- Quality gates
  - XML docs on public contracts; analyzers enabled; no magic values (all constants centralized).

Note: Incorporate tokenizer interfaces and cost estimator hooks here; include provider tokenizers or fallbacks with accuracy tests (±5%).

### Story A2: Providers — OpenAI and Local (Ollama)

- Deliverables
  - Provider adapters with streaming (SSE/chunked), timeouts/retries, error normalization.
  - Auto-probe: prefer Ollama if reachable; else OpenAI if key present; else feature disabled.
- Acceptance Criteria
  - Integration tests exercising streaming and error handling.
  - Boot report lists provider, model, streaming/tool capability; keys redacted.

#### Milestones & Deliverables

- Code
  - `Koan.AI.Providers.OpenAI` and `Koan.AI.Providers.Local` (Ollama) adapters with `HttpClientFactory`, timeouts, retry/backoff (transient only), and SSE/stream chunk parser.
  - Provider capability detection (max tokens, tool support) and mapping to `AiCapabilityFlags`.
  - Boot reporter contributor to append AI section (safe/redacted).
- Docs
  - Provider setup (env keys), model default table, streaming behavior, error codes normalization table.
- Tests
  - Live-disabled integration tests gated by env; recorded tests for chat/streaming; unit tests for parser edge cases (empty/partial chunks).
- Observability
  - Spans for provider calls with tags: provider, model, status, token usage; histogram for latency.

Note: Provider tokenization wired to A1’s tokenizer abstraction; add retry policy docs and backoff bounds.

### Story A3: Web AI Controllers

- Deliverables
  - Koan.Web.AI: controllers only: POST /ai/chat (SSE), POST /ai/embed, GET /ai/models.
  - Minimal TinyAI template that wires DI and env options.
- Acceptance Criteria
  - Endpoints function in a sample app; OpenAPI appears with schemas; no inline endpoints.

#### Milestones & Deliverables

- Code
  - Controllers: `ChatController` (POST /ai/chat with SSE), `EmbeddingsController` (POST /ai/embed), `ModelsController` (GET /ai/models).
  - SSE utility for proper flush/backpressure; request validators (budget/model allow-list hooks).
  - DI wiring extension `AddKoanWebAI()`; OpenAPI schema annotations; problem details mapping for AI errors.
- Docs
  - Endpoint reference with request/response examples; SSE client examples; model override via headers.
- Tests
  - Minimal WebApplicationFactory-based tests for 200-streaming, invalid model 400, budget exceeded 429.
- DX
  - TinyAI template: Program.cs shows controller wiring via DI and env-only configuration.

Note: Controllers must strictly follow Story A0 schemas; SSE event framing must include heartbeat/keepalive and explicit termination event.

### Story W2: Protocol Surfaces (gRPC + OpenAI-compatible shim)

- Deliverables
  - gRPC proto files for Chat, Embeddings, RAG; C# server implementation gated behind feature flag; basic client.
  - OpenAI-compatible shim endpoints (subset) with clear stability window and mapping table.
  - Protocol adapters (desirable):
    - MCP: optional server role exposing tool registry and chat endpoints as MCP tools/resources; discovery docs.
    - AI-RPC: mapping layer aligning /ai/chat and /ai/embed to AI-RPC method shapes; interop notes and limits.
- Acceptance Criteria
  - Internal service-to-service chat/stream works via gRPC; shim passes basic compatibility tests; perf parity within agreed budget.


### Story A4: Observability and Cost

- Deliverables
  - OTel spans: ai.request with provider/model/status; metrics for tokens, latency; cost estimator hooks.
  - Prompt hashing and redacted logging by default.
- Acceptance Criteria
  - Metrics visible in sample; logs contain hashes not content; configurable opt-in for transcript storage.
-
### Story OR1: Platform Ramp-up (Redis Core, MySQL, CouchDB) — formerly RU0

- Why
  - Ensure foundational data/services are ready for AI features: low-latency cache/session, broad relational coverage, and a document store option beyond Mongo.
- Prioritization
  - 1) Redis Core (cache/session) → prerequisite for AI cache and memory.
  - 2) MySQL adapter → large install base; complements Postgres/SqlServer/Sqlite.
  - 3) CouchDB adapter → optional for teams with Couch; Mongo already supported.
- Deliverables
  - Redis Core: namespaced cache/session APIs with TTL, connection health, and DI options; boot report integration.
  - MySQL Adapter: `Koan.Data.MySql` with capability flags, DDL policy adherence, pushdown-first queries, Direct escape hatch.
  - CouchDB Adapter: `Koan.Data.CouchDb` with capability discovery, health checks, and basic CRUD/query parity with Mongo adapter where sensible.
- Acceptance Criteria
  - Redis: cache get/set with TTL, session store API, health/readiness green; perf sanity on small payloads.
  - MySQL: CRUD + paging pushdown; entity mapping parity; Direct API works with parameterized queries.
  - CouchDB: CRUD, view/query basics; capability surfaced; health checks pass.

#### Milestones & Deliverables

- Code
  - `Koan.Data.Redis` (or `Koan.Caching.Redis`):
    - Cache service with namespacing (tenant/project/model), TTL, serialization strategy, and bulk operations.
    - Session store interface with sliding/absolute expirations (to back AI session memory pre-Vector).
    - Health checks, metrics (hits/misses, latency), boot report contributor.
  - `Koan.Data.MySql`:
    - Relational adapter integrating with existing relational toolkit and LINQ translator; discovery of server version and feature flags (window functions, CTE support).
    - DDL policy compliance; naming conventions; transaction and batch support.
    - Direct ADO boundary with parameterization and tracing tags (db.system=mysql).
  - `Koan.Data.CouchDb`:
    - Client wiring with HTTP auth/TLS; database bootstrap/health; CRUD and query-by-view.
    - Capability flags (attachments, partitioning, mango query support if enabled).
    - Consistent telemetry tags (db.system=couchdb) and error normalization.
- Docs
  - Examples for Redis/MySQL/CouchDB including docker-compose snippets and env configuration.
  - Capability matrices and known differences vs. existing adapters (e.g., Postgres/Mongo).
  - DDL/governance policy notes for MySQL; data model guidance for CouchDB.
- Tests
  - Redis: cache TTL correctness, namespacing isolation, perf microbench.
  - MySQL: pushdown paging tests, transactionality, Direct command tests.
  - CouchDB: CRUD/view query tests, capability gating when features absent.
- Observability/Ops
  - Metrics: cache hit ratio, db latency histograms per adapter; readiness checks with clear failure reasons.
  - Probe enhancements: extend `ai-probe.ps1` or add `resource-probe.ps1` to validate Redis/MySQL/CouchDB connectivity and print boot-style summaries.


#### Milestones & Deliverables

- Code
  - `ActivitySource` and `Meter` registration in `Koan.AI.Core`; counters/gauges for tokens (prompt/completion/total), estimated cost, and request counts.
  - Prompt hashing utility (SHA-256) and redaction policy; transcript persistence interface (disabled by default).
- Docs
  - How to view metrics (OTel/Prom); privacy posture; enabling transcript storage and risks.
- Tests
  - Metric emission smoke tests; hash determinism; redaction on by default.
- Ops
  - Sample dashboard JSON (optional) and recommended alerts (latency, error rate, budget breaches).

Note: Add tokenization accuracy gauges and cost estimation error bounds to dashboard; document privacy posture for metrics.

### Story V1: Redis Vector + Cache (Redis-first)

- Deliverables
  - Koan.Data.Vector.Redis with capability negotiation (dims, metric, HNSW).
  - Embedding pipeline with batch/retry and cache hooks; prompt/response cache (TTL) in Redis.
- Acceptance Criteria
  - Upsert/query/delete work; P95 < 100ms on small corpora; cache hit ratio observed in sample.

#### Milestones & Deliverables

- Code
  - `Koan.Data.Vector.Redis`: index bootstrap (HNSW), capability detection (metric, dims), health checks, upsert/query/delete/search-with-metadata, pagination.
  - Embedding pipeline service with batch/retry and backpressure; validation of vector dimensions.
  - AI cache: prompt→response cache with TTL, namespacing (tenant/project/model), and invalidation hooks.
- Docs
  - Redis Stack requirement, connection examples, capability table; cache keys structure and TTL guidance.
- Tests
  - Perf microbench for small corpora; query recall sanity test; cache hit/miss counters.
- Observability
  - Metrics for cache hits/misses, vector query latency, index size; health endpoint surfacing readiness.

Note: Add Redis guardrails: memory cap guidance, persistence (RDB/AOF) recommendations, and HA/cluster checklist.

### Story V1.1: pgvector Adapter (fast-follow)

- Deliverables
  - `Koan.Data.Vector.Postgres` (pgvector) with capability detection, migrations/bootstrap, parity query API.
  - Perf and recall parity tests vs. Redis adapter on sample corpora; connection resiliency and pooling guidance.
- Acceptance Criteria
  - Env swap from Redis to pgvector without code changes; parity tests pass thresholds; boot report surfaces pgvector capabilities.

### Story R1: RAG Sample and Probes

- Deliverables
  - Sample S5.AI.Rag: index docs/; POST /ai/rag/query returns streaming answer with citations.
  - scripts/ai-probe.ps1 validates providers, Redis connectivity, and prints boot-style summary.
- Acceptance Criteria
  - One-command dev run; first success in <10 minutes following guide.

#### Milestones & Deliverables

- Code
  - `samples/S5.AI.Rag`: ingestion (docs/ scan, chunking), embeddings upsert, query endpoint that does retrieve→rerank (optional)→answer with citations; streaming output.
  - Background job for incremental indexing; session memory in Redis with TTL.
  - `scripts/ai-probe.ps1`: checks provider connectivity, lists models, pings Redis, validates index and prints boot-style JSON.
- Docs
  - Concise examples; troubleshooting matrix; performance tips.
- Tests
  - Smoke test that a known query returns expected doc ids; probe script exit codes for CI.

Note: Fix defaults here—chunk size/overlap, embedding version stamps, invalidation strategy (full or delta), citations JSON schema; optional rerank toggle.

### Story G1: Budgets, Moderation, and Allow-lists

- Deliverables
  - Per-request/service token/time budgets; moderation/redaction middleware; model allow-list.
- Acceptance Criteria
  - Budget breach produces graceful fallback/termination with clear error; moderated content blocked/logged.

#### Milestones & Deliverables

- Code
  - Budget middleware/policy: per-request and per-service token/time limits; budget headers in responses; graceful cut-off for streaming.
  - Moderation hook (provider-based or heuristic) applied pre/post; redact or block per policy.
  - Model allow-list enforcement with environment/Options; violation → 403.
- Docs
  - Budget configuration examples; moderation trade-offs; model catalog governance.
- Tests
  - Budget edge cases (zero/very small budgets), long prompts, streaming cut-off correctness; moderation pass/block cases.
- Observability
  - Metrics: budget breaches, moderated counts; logs with reasons (no content leakage).

Note: Budget enforcement must integrate with SSE termination correctly; include examples and tests for partial streams.

### Story P1: Sidecar Proxy (single-tenant)

- Deliverables
  - Koan.Web.AI.Proxy service project with same controllers; optional OpenAI-compatible route shape.
  - Env routing with fallback (local → hosted); shared Redis cache/memory/vector.
- Acceptance Criteria
  - Existing sample switches to proxy via base URL/env only; latency overhead within acceptable bounds.

#### Milestones & Deliverables

- Code
  - New service: `src/Koan.Web.AI.Proxy/` exposing same controllers; optional OpenAI-compatible shim endpoints.
  - Env-based routing and fallback (local → hosted); connection pooling; SSE passthrough with minimal buffering.
  - Health/readiness endpoints; containerfile and sample compose profile.
- Docs
  - How to switch from in-app to sidecar; environment variables; security posture; resource sizing.
- Tests
  - E2E tests pointing sample to proxy; resiliency tests for provider failures; latency budget regression checks.
- Ops
  - Boot report includes upstream providers and Redis; redaction preserved; structured logs.

Note: Add secrets provider integration for keys; route selection policy exposed via headers and logs.

### Story T1: Multi-tenant Central Proxy

- Deliverables
  - Tenant/project headers (Koan-Tenant, Koan-Project); usage accounting; quotas/rate limits.
  - Admin APIs for model catalogs and usage; dashboards via OTel/Prom.
- Acceptance Criteria
  - Two apps share proxy with isolated quotas; admin sees per-tenant metrics.

#### Milestones & Deliverables

- Code
  - Tenancy headers/claims mapping; per-tenant usage accounting in Redis (counters/budgets) and optional rate limiting.
  - Admin APIs: get models/catalog, get usage per tenant/project; API keys or JWT; optional mTLS.
  - Policy store abstraction for per-tenant model allow-lists and budgets.
- Docs
  - Multi-tenant deployment guide; security considerations; key rotation.
- Tests
  - Quota enforcement across tenants; noisy-neighbor scenarios; admin API authz tests.
- Observability
  - Metrics partitioned by tenant/project; dashboards snippets.

Note: Include per-tenant secret scoping and rotation guidance; document on-behalf-of flows.

### Story V2: Weaviate Adapter + Migration

- Deliverables
  - Koan.Data.Vector.Weaviate with capability discovery; TLS/auth.
  - Migration utility Redis ↔ Weaviate (ids/vectors/metadata).
- Acceptance Criteria
  - Env swap without code changes; migration preserves recall within tolerance on sample set.

#### Milestones & Deliverables

- Code
  - `Koan.Data.Vector.Weaviate` with capability discovery, TLS/auth, collection bootstrap; parity query API.
  - Migration utility: export/import Redis ↔ Weaviate (ids, vectors, metadata) with resumable batches.
- Docs
  - Adapter configuration; feature comparison vs. Redis; migration guide with checksums/recall validation.
- Tests
  - Adapter conformance tests; migration correctness on sample corpus; performance sanity benchmarks.
- Observability
  - Health checks and capability surface in boot report; metrics aligned with Redis adapter.

### Story E1: Messaging Transformers and Tool Registry

- Deliverables
  - MQ handlers for summarize/classify/enrich with idempotency, batching.
  - Safe tool registry for function calling; audit of tool use.
- Acceptance Criteria
  - Transformers operate under retries/DLQ; tool calls restricted to allow-listed functions.

#### Milestones & Deliverables

- Code
  - Messaging transformers: summarize/classify/enrich handlers with idempotency keys, batch windows, and retry semantics aligned with existing messaging patterns.
  - Safe tool registry: define tool interface, allow-list configuration, audit of tool-invocations; integration with chat for function calling.
- Docs
  - Examples wiring transformers; tool registry design and safety model; sample allow-list config.
- Tests
  - Idempotency test under retries; DLQ path; tool call audit log presence; deny on non-allow-listed tool.
- Observability
  - Metrics for messages processed, failures, retries; tool call counts by name/tenant.

---

## AI-adjacent stack alignment (formats, servers, knowledge)

This section aligns external protocols/formats with Koan plans. Items are tagged as Coming Soon (CS) or Future (F).

- Data & Storage
  - Parquet (CS): add to D5 materialization; optional in D1 export (fast-follow).
  - JSON-LD & Schema.org (CS): D1 manifests include JSON-LD context; guidance for Schema.org types on Entities.
  - Arrow (F): optional in-memory batches for high-throughput transfer paths.
- Model serialization & exchange
  - GGUF (CS): ai-probe mentions local GGUF models with Ollama/vLLM notes.
  - ONNX/TorchScript/SavedModel (F): integration guides for inference via sidecar; not core.
- Inference & serving
  - OpenAI/Anthropic/Gemini (CS/Phase 2): providers.
  - Ollama (CS); vLLM/TGI (F guide, optional adapter later).
  - KServe/KFServing (CS guide): interop guide + readiness checks.
- Vector & knowledge query
  - Redis HNSW (CS), pgvector (CS fast-follow), Weaviate (F + migration).
  - FAISS/HNSWlib (F): local adapter guidance; not core dep.
  - SPARQL/RDF (F): export guide and RAG metadata notes.
  - GraphQL (CS): persisted queries guidance for RAG backends.
- Agent & tool protocols
  - Safe tool registry (CS) with allow-list; SK/LangChain bridges (F guides/adapters).
  - Assistant/Messages mapping (F): compatibility notes to /ai/chat.
- Emerging standards
  - MCP & AI-RPC (CS posture): optional adapters; interop matrix; feature-flagged.
  - MMEPs (F): track; add once core stabilizes.

---

## On-ramp priority: Data Bridge & Replication stories

### Story D0: Transfer Contracts & Headers

- Deliverables
  - Contracts: `EntityChange` (op, keys, version, payload), `TransferJob` (source, sink, format, compression, status), error/ProblemDetails, and constants for headers (Koan-Change-Seq, Koan-Transfer-JobId, Koan-Transfer-Format).
  - Policy hooks: projection/redaction rules, PII flags.
- Acceptance Criteria
  - DTOs and headers documented; OpenAPI examples; unit tests for serialization and policy application.

### Story D1: Snapshot Export/Import (JSONL/CSV + FS/S3/Blob)

- Deliverables
  - `Koan.Data.Transfer` services and `Koan.Web.Transfer` controllers: POST /transfer/export, POST /transfer/import.
  - Formats: JSONL first, CSV; compression (gzip); optional encryption; presigned URL support.
  - Manifests: counts, checksums, schema version.
- Acceptance Criteria
  - Export/import succeeds for a representative EntitySet; manifests validate; parity check passes (count/hash).

### Story D2: CDC via Debezium/Kafka

- Deliverables
  - Source adapter consuming Debezium topics; normalizer mapping to `EntityChange`.
  - Replicators to Postgres and Mongo adapters; idempotency/dedupe and ordering safeguards.
- Acceptance Criteria
  - End-to-end replication with retries/DLQ; ordering preserved; lag and throughput metrics emitted.

### Story D3: AI-aware Vector Indexer

- Deliverables
  - Replication hook to (re)embed selected fields; upsert vectors into Redis (V1) and pgvector (V1.1) with embedding version tags and invalidation.
  - Policy: projection/redaction before embeddings; size caps.
- Acceptance Criteria
  - On change, vectors updated; queries reflect latest version; metrics for index latency.

### Story D4: Virtualization (Composite Reads)

- Deliverables
  - Virtual EntitySet definition referencing multiple sources; pushdown where possible; in-memory fallback signaled via `Koan-InMemory-Paging` header.
  - Controller exposure using standard filter/paging semantics.
- Acceptance Criteria
  - Union/join scenarios return consistent Entity shape; fallback header emitted when used; perf within documented bounds.

### Story D5: Scheduled Materialization Jobs

- Deliverables
  - Job runner with cron options; snapshot-to-sink pipelines; Parquet fast-follow; manifests generation; notifications on completion/failure.
- Acceptance Criteria
  - Nightly export to FS/S3 with manifests; retries and partial resume work; metrics for job duration and size.

### Story D6: Diff & Reconcile

- Deliverables
  - Diff engine computing counts/hashes; report generation; targeted replay API to repair drifts.
- Acceptance Criteria
  - Drift detected and repaired on a test dataset; audit log links changes; metrics for drift rate.

### Story D7: Observability & Governance for Transfers

- Deliverables
  - OTel spans around source read/transform/sink write; metrics for throughput, error rate, lag, dedupe hits.
  - Policy docs for PII redaction, naming conventions, and retention.
- Acceptance Criteria
  - Dashboards show pipeline health; redaction on by default; alerts configured for lag/error thresholds.

### Story D8: Connectors Interop Guides (Airbyte/Singer/dbt/Kafka Connect)

- Deliverables
  - Guides and shims for ingesting via Airbyte/Singer into Koan JSON store; dbt using Koan exports as sources; Kafka Connect examples.
- Acceptance Criteria
  - Example pipelines run using provided configs; docs include troubleshooting and capability notes.

Note: E1 (Messaging Transformers) ties into D2 by enriching changes in-flight; D3 wires into V1/V1.1.

### Story S1: Secrets Provider & Key Management (Sprint 0–1)

- Deliverables
  - Secrets abstraction with adapters for Azure Key Vault and AWS Secrets Manager; per-tenant key resolution; caching with TTL and auto-refresh hooks.
  - CLI/docs for bootstrap and rotation; diagnostics and redacted boot report entries.
- Acceptance Criteria
  - Proxy can resolve per-tenant keys without code changes; rotation updates take effect without restart; secrets never logged.

### Story P2: SSE Performance & Backpressure SLOs

- Deliverables
  - Define SLOs (TTFB, token cadence, max stall); implement flush/backpressure handling; heartbeat frames; configurable chunk sizes.
- Acceptance Criteria
  - Load test meets SLOs; regression checks in CI; documented tuning knobs.

### Story T2: Record/Replay Harness Standardization

- Deliverables
  - Unified record/replay harness for providers with pluggable cassettes; corpus for chat/embedding; CI integration.
- Acceptance Criteria
  - Tests run deterministically without live provider; failure diffs are actionable; docs show how to add new cases.

---

## Cross-cutting Quality Plan

- Capability discovery surfaced via headers and boot report; consistent constants across web/messaging/data.
- Security: RBAC reuse from Koan Web; secrets never logged; redact-by-default; no transcript persistence unless configured.
- Tests: unit for options/contracts; integration for providers; perf checks for Redis vector; record/replay harness for LLM calls.
- Docs-first: each story updates guides and adds ADRs where architecture shifts.

## Documentation & ADRs

- Guides: docs/guides/ai/index.md and this epic; quickstarts for library, sidecar, and central proxy.
- ADRs
  - ARCH-00xx: AI Baseline (scope, contracts, discovery, streaming policy).
  - DATA-00xx: Redis Vector Adapter (capabilities, defaults, bootstrap).
  - WEB-00xx: AI Controllers, headers, and SSE guidance.
  - ARCH-00xy: AI Proxy/Connector, tenancy and quotas.

## Success Metrics

- Time-to-first-success < 10 minutes using TinyAI template.
- P95 latency < 100ms for Redis vector search on small corpora; stable streaming under load.
- Budget enforcement prevents >95% of overruns in tests; zero secret leaks in logs.
- Adapter swap via env only; migration utilities validated on sample.

## Timeline (indicative)

- Wave 1 (2 sprints): A1–A4, V1, R1.
- Wave 2 (2 sprints): G1, P1, V2.
- Wave 3 (1–2 sprints): T1, E1.

## Open Questions

- OpenAI-compatible route shape in proxy: how much parity do we want vs. a thin shim?
- Default model list and tokenizer accuracy across providers for cost estimation.
- Reranking strategy in RAG (local vs. provider-based); include capability flags?
