# Koan Feature Catalog (Value-centered) - v0.2.18

A modular .NET framework that standardizes data, web, messaging, and AI patterns with strong governance and observability—so teams ship faster with fewer surprises, and platforms scale with consistency.

## Summary — Pillars at a glance

- Core
  - Unified runtime/config, health/readiness, secure defaults, auto-registration, boot reports, optional OpenTelemetry.
- Data
  - Adapter-agnostic persistence (relational/NoSQL/JSON), pushdown-first, Direct escape hatch, CQRS/outbox, vector module.
- Flow
  - Model-typed pipeline (ingest → standardize → key → associate → project), aggregation tags and identity map, canonical/lineage views, parked/rejections, and a DX toolkit (generic controllers, actions, monitors).
- Storage
  - Profile-based storage orchestrator with thin providers; local filesystem provider; DX helpers and model-centric API.
- Media
  - First-class media objects, HTTP bytes/HEAD endpoints with range/conditional support, cache-control, variants/derivatives, and transform pipelines integrated with Storage profiles.
- Web
  - Controllers-only HTTP (REST/GraphQL), startup pipeline wiring, Swagger by default in dev, payload transformers, capability controllers (Moderation/SoftDelete/Audit) with canonical routes and set-based scoping.
- Scheduling
  - Background job orchestrator with OnStartup tasks, timeouts, health facts, and readiness gating.
- Messaging
  - Capability-aware semantics (aliases, retry/DLQ, idempotency), RabbitMQ transport, inbox services (HTTP/Redis/InMemory).
- AI
  - Chat/embeddings, vector integration, Ollama provider, RAG building blocks, budgets and observability.
- Services & DX
  - Tiny templates, auto-registration across modules, decision docs (ADRs), container-smart defaults.
- Recipes
  - Intention-driven bootstrap bundles (health checks, telemetry, reliability, workers) that layer predictable defaults on top of referenced modules; activate via package, config, or code with dry-run and capability gating.
- Orchestration
  - DevHost CLI and adapters to bring up local dependencies (Docker/Podman) and export deterministic artifacts (Compose v2 today). Profile-aware behavior (local/ci run; staging/prod export-only), scoped readiness waits, port-conflict policy, endpoint hints, and single-binary distribution (dist/bin/Koan.exe) with publish/install scripts.

References
- Data: docs/reference/data-access.md
- Web Capabilities: docs/reference/web-capabilities.md
- Storage: docs/reference/storage.md
- Media: docs/reference/media.md
- Web: docs/reference/web.md
- Messaging: docs/reference/messaging.md
- Flow: docs/reference/flow.md
- AI: docs/reference/ai.md
- Recipes: docs/reference/recipes.md
- Orchestration: docs/reference/orchestration.md
- Koan CLI: docs/reference/Koan-cli.md

## Pillars → outcomes

- Core
  - Predictable apps by default (health/readiness, env flags, secure headers, boot reports, OpenTelemetry).
  - Auto-registration pipeline (IKoanAutoRegistrar) with a unified bootstrap report (redacted in prod) and module Describe() coverage.
  - Unified runtime/config helpers: KoanEnv snapshot + Koan.Core.Configuration.Read/ReadFirst for consistent, overridable settings.
- Data
  - Adapter-agnostic persistence with capability discovery, pushdown-first performance, safe Direct escape hatch.
  - Production-ready adapters: Postgres, SQL Server, SQLite, MongoDB, Redis, and JSON file storage.
  - Vector module split: Koan.Data.Vector (+ Abstractions) with auto-registration; default provider resolver precedence (attribute > vector defaults > source role > highest-priority data provider).
  - Vector facade on Entity<T> and Vector<TEntity> helpers (Save/Delete/Search) for a simpler usage model.
  - Weaviate adapter with schema management, KNN queries, and filter pushdown via GraphQL translator.
  - CQRS patterns with outbox support for MongoDB and event-driven architectures.
  - Relational orchestration: [RelationalStorage] attribute + EnsureCreated orchestration with attribute > options precedence.
  - Singleflight dedupe for in-flight operations across core and relational adapters.
- Flow
  - Model-typed pipeline and sets: per-model stage sets (intake/standardized/keyed/processed), task and view sets, and stable IDs ("{view}::{referenceId}").
  - Aggregation and identity: attribute-driven aggregation tags, association and identity map, collision detection with parked records and structured rejection reports.
  - Views and materialization: canonical and lineage projections with per-model view sets; dynamic transport for adapter-neutral payloads.
  - DX toolkit: generic `FlowEntityController<TModel>` under `/api/flow/{model}`, action verbs (`seed`, `report`, `ping`) via `IFlowActions` + responder, and monitor hooks (`OnProjectedAsync`) with atomic Model/Policies mutation.
  - Auto-registration: controllers/handlers wired via auto-registrar; first-class statics on entities and views for consistent data access.
- Storage
  - Storage orchestrator (Koan.Storage) with profile-based routing to thin providers; capability-aware operations (seek/range, stat, server-side copy, presign when supported).
  - Local filesystem provider (Koan.Storage.Local): safe-by-default (key sanitization, base path enforcement), atomic writes (temp+rename), range reads, lightweight Head, and server-side copy.
  - Routing defaults and fallbacks: DefaultProfile support; optional SingleProfileOnly fallback for minimal config with startup validation.
  - Developer experience:
    - Service helpers: Create/Onboard (text/json/bytes/stream/file/url), Read (full/range), Exists/Head, Transfer (Copy/Move), fluent InProfile.
    - Model-centric API: StorageEntity<T> + [StorageBinding] attribute for static creators and instance ops (Read/Head/Delete/CopyTo/MoveTo).
  - Auto-registration and centralized constants; ambient DI resolution via AppHost for terse usage.
  - Docs and ADRs: STOR-0001..0007 and Reference → Storage.
- Web
  - Controller-driven APIs (REST/GraphQL) with guardrails, content negotiation, consistent paging/filtering.
  - Startup filter auto-wires the pipeline: UseDefaultFiles/UseStaticFiles (opt-in), routing, and MapControllers; well-known health endpoints (/health, /health/live, /health/ready).
  - Built-in Swagger/OpenAPI auto-registered (dev-on by default; prod opt-in). Idempotent Add wiring; no explicit Add/Use calls required when the module is referenced.
  - HTTP payload transformers for flexible request/response shaping with auto-discovery.
  - Web capabilities (reference controllers): Moderation, SoftDelete, and Audit exposed via generic controllers with canonical, DX-friendly routes that align with `EntityController<T>` shapes. No bespoke controllers required; discovered via ApplicationPart with route prefix configuration.
  - Set-based scoping: capability routes and entity REST endpoints accept `?set=` to scope operations consistently with Data `WithSet(...)` semantics (defaults apply per controller). See ADR WEB-0046.
  - Capability authorization: layered resolution for each capability action — Entity override → Global Defaults → DefaultBehavior (Allow/Deny). Named policy helpers (KoanWebPolicyNames) and an enforcement attribute (RequireCapability) integrate with ASP.NET Authorization. See ADR WEB-0047.
  - GraphQL endpoints auto-generated from IEntity<> types with HotChocolate integration.
  - Centralized Web Authentication with pluggable IdP adapters and safe flows:
    - Provider discovery: GET /.well-known/auth/providers
    - Challenge/callback/logout controller endpoints with safe return URLs and prompt forwarding
    - Adapters: Google, Microsoft, Discord, generic OIDC, and a Dev TestProvider for local workflows
    - Production gating of discovery/challenge; cookie-based user session and single sign-out via logout
- Scheduling
  - Background job orchestrator with OnStartup tasks, per-task timeouts, health facts, and readiness gating. Auto-registered; tasks discovered via DI — no bespoke reflection.
  - Sample: S5 bootstrap task seeds local data and optional vectors on first run without gating readiness by default.
- Messaging
  - Capability-aware, cross-broker semantics (aliases, DLQ/retry, idempotency) with simple handler wiring.
  - RabbitMQ transport with resilient connection management and config-first options.
  - Redis-based inbox service for message processing and deduplication (Koan.Service.Inbox.Redis):
    - Endpoints: GET /v1/inbox/{key}, POST /v1/inbox/mark-processed
    - Config via Koan:Inbox:Redis:ConnectionString (or ConnectionStrings:InboxRedis)
    - Optional discovery announce on RabbitMQ (Koan:Messaging:Buses:rabbit:*)
  - HTTP and in-memory inbox implementations for testing and lightweight scenarios.
- AI
  - Turnkey inference (streaming chat, embeddings) with minimal config; Redis-first vector + cache; RAG defaults; observability and budgets; optional sidecar/central proxy; one-call AddKoanAI() and auto-boot discovery.
  - Ollama provider integration for local AI models with streaming support and health monitoring.
  - Weaviate vector database adapter with GraphQL query translation and KNN search capabilities.
- Media
  - MediaObject<T> static API: Upload, Get, Open, Url, Ensure, Derivatives, Ancestors/Descendants, RunTask/StreamTask.
  - HTTP endpoints (Koan.Media.Web): GET/HEAD bytes with conditional and range requests; proper Accept-Ranges, Content-Range, ETag/Last-Modified, and Cache-Control.
  - Derivatives/variants: idempotent by DerivationKey, linked via SourceMediaId + RelationshipType; variant routing and canonical signatures per ADR MEDIA-0003.
  - Transform pipeline (Koan.Media.Core): operator registry (resize, rotate, type-convert), strictness and placement options, predictable signatures.
  - Storage integration: profile-based placement (hot/cold), presigned URLs when supported, CDN-friendly headers, and safe key handling via Storage.
- Orchestration
  - DevHost CLI: export, up, down, status, logs with Docker-first then Podman fallback; JSON/verbose modes for tooling.
  - Deterministic export: emits `.Koan/compose.yml` with profile-aware mounts and safe quoting; descriptor-first planning.
  - Readiness semantics: services must be Running; if a healthcheck exists it must be Healthy. Timeout maps to exit code 4 with clear guidance.
  - Engine hygiene: pre-run `compose down -v --remove-orphans`, per-run project isolation (COMPOSE_PROJECT_NAME), `compose config` preflight.
  - Ports: conflict auto-avoid in non‑prod with optional `--base-port`; prod fails fast; status surfaces live endpoints and conflicts.
  - Distribution: single-file CLI published to `dist/bin` with a friendly alias `Koan.exe` and helper scripts to publish/install/verify.
- Services & DX
  - Fast onboarding (Tiny\* templates, meta packages), reliable test ops (Docker/AI probes), decision clarity (normalized ADRs).
  - Auto-registration across modules reduces boilerplate; templates and samples rely on controllers-only routing (no inline endpoints).
  - DevHost CLI streamlines local runs and CI exports; Windows-first scripts help publish/install/verify a single-file `Koan` binary.
  - Container-smart defaults and discovery lists for adapters and AI providers; see Guides → "Container-smart defaults" and ADR OPS-0051.
  
## Recipes

- Intention-driven bootstrap bundles that apply best-practice operational wiring on top of modules you already reference.
- Activation modes:
  - Reference = intent (add a `Koan.Recipe.*` package),
  - Config-only selection via `Koan:Recipes:Active`,
  - Code: `services.AddRecipe<T>()` or `services.AddRecipe("name")`.
- Deterministic options layering: Provider defaults < Recipe defaults < AppSettings/Env < Code overrides < Forced overrides (disabled by default; gated by `Koan:Recipes:AllowOverrides` + per-recipe `Koan:Recipes:<Name>:ForceOverrides`).
- Capability gating: recipes only apply when prereqs exist (check registered services or configured options). Avoid duplicate wiring.
- Diagnostics: stable EventIds (Applying 41000, AppliedOk 41001, SkippedNotActive 41002, SkippedShouldApplyFalse 41003, DryRun 41004, ApplyFailed 41005) and a dry-run mode via `Koan:Recipes:DryRun=true` to preview without DI mutations.
- Guardrails: infra-only wiring (no inline endpoints), controller-only HTTP surface, no magic values—use options/constants.
- Example package: `Koan.Recipe.Observability` adds health checks and resilient HttpClient policies when applicable.

## Scenarios and benefits

- Greenfield APIs/services: ship quickly on JSON/SQLite; swap to Postgres/SQL Server/Mongo without API churn.
- Enterprise data services: enforceable governance (DDL policy, naming, projection), measurable performance (pushdown), and traceability (db.\* tags).
- Entity coalescence/MDM: unify multi-source identities with aggregation tags and an identity map; expose canonical/lineage views, investigate collisions via parked/rejections, and control ingestion via actions and monitors.
- CQRS/event-driven: inbox/idempotency, batch semantics, provider-neutral retries/DLQ.
- Ops/reporting: Direct API for audited, parameterized ad-hoc access; neutral rows; limits and policy gates.
- Modern UI backends: REST + GraphQL from the same model with consistent naming and filter semantics.
- AI assist & RAG: `/ai/chat` with SSE, `/ai/embed`, and `/ai/rag/query` with citations; Redis vector and cache by default; Weaviate and pgvector support.
- Vector operations: Multi-provider vector search (Redis HNSW, Weaviate GraphQL, planned pgvector) with unified query interface.
- Local dev orchestration: use `Koan up` to start Postgres/Redis/etc., with health-gated readiness and clear endpoint hints; `Koan export compose` for CI/staging.
- Data bridge: snapshot export/import (JSONL/CSV/Parquet), CDC via Debezium/Kafka, virtualization (composed reads), scheduled materialization.
- First-run bootstrap: schedule startup tasks to seed local data and vectors, with readiness gating disabled by default (opt-in when needed).
- File/object storage:
  - Local dev and on-prem: drop-in filesystem-backed storage with safe keys, range reads, and simple profile config.
  - App content and user uploads: stream uploads with hashing (seekable streams), quick probes (Head/Exists), and hot→cold transfers via Copy/Move.
  - Model-first DX: bind a type to a storage profile and call static creators/instance ops without plumbing code.

## Strategic opportunities

- Platform standardization: common naming, controllers-only HTTP, centralized constants, ADR taxonomy.
- Progressive hardening: start permissive in dev; tighten paging caps, CSP, DDL policy, discovery gating.
- Polyglot without chaos: capability flags make differences explicit; shared policy (naming, projection, pushdown).
- Observability-first: spans/metrics + boot reports enable SLOs and faster incident response.
  - Explicit opt-in for OpenTelemetry via AddKoanObservability() to avoid surprise telemetry and double-pipeline conflicts; configurable via Koan:Observability.
- Binary storage standardization: consistent storage patterns across providers (local today; cloud adapters next) with clear capability flags and profile policy.
- On-ramp path: adapters → transfer/replication → AI-aware indexing → RAG—value compounds with low switching cost.
- Protocol interop: optional adapters for gRPC (internal), OpenAI-compatible shim, MCP (Model Context Protocol), and AI-RPC to meet teams where they are.

## Risks and guardrails

- Accidental DDL/unsafe ops in prod → magic gating, DDL policy (NoDdl), prod-off discovery, redacted boot report.
- Hidden in-memory fallbacks → pushdown MUST; `Koan-InMemory-Paging` header signals fallback.
- Adapter drift → centralized instruction constants, capability flags, relational toolkit + LINQ translator.
- AI cost/leakage → token/time budgets, prompt hashing, redaction-by-default, model allow-lists.
- Vector posture → Redis guardrails (memory/persistence/HA); pgvector parity tests; migration utilities.
- Storage posture → path traversal prevention, atomic writes, startup validation for profiles/defaults, explicit errors when presign is unsupported; range validation with clear 416-style semantics at the web edge.
- Orchestration posture → explicit profiles; no auto-mounts in prod; port-conflict fail-fast in prod; redact secrets in human-readable output; descriptor-first resolution.

## Coming soon (on-ramp and near-term)

- Cognitive coalescence (AI North Star)

  - Auto-boot orchestrator: probes providers (Ollama/OpenAI), vectors (Redis/pgvector), secrets, budgets, and protocols; single boot report with redacted status and action hints.
  - One-call DI: AddKoanAI() wires providers, tokenization/cost, budgets/moderation, SSE, telemetry, headers, and /ai/\* controllers.
  - AI profiles: Koan_AI_PROFILE = DevLocal | HostedKeyed | ProxyClient | TestHarness; sensible defaults for model, streaming, budgets, vector, cache TTLs.
  - Convention endpoints: /ai/chat, /ai/embed, /ai/rag/query, /ai/models auto-enable when Ready; OpenAI-shim and gRPC via single flags.
  - Zero-scaffold RAG example: first run indexes docs/ with defaults; Redis-first vector + cache; background index job; safe projection/redaction.
  - Vector autodiscovery: Redis HNSW index bootstrap with sane metrics; pgvector fast-follow with auto-migrate if permitted.
  - Secrets layering: prefer secrets provider (KV/Secrets Manager) with env fallback; never log values; redacted boot entries.
  - Policy presets: DevPermissive | StagingBalanced | ProdConservative for token/time budgets, moderation, model allow-lists.
  - Capabilities surface: Koan-AI-\* headers and GET /ai/capabilities expose provider/vector flags, budgets, protocol availability.
  - Probes & health: ai-probe.ps1 mirrors /health/ready details; precise remediation messages.
  - Record/replay harness: Koan_AI_TEST_RECORD to capture; replay in CI; cassettes sanitized by default.
  - DX: TinyAI template and AI transformer recipes (summarize/classify) for Web/MQ.

- On-ramp

  - Platform ramp-up: Redis core cache/session; MySQL relational adapter; optional CouchDB.
  - Data Bridge (D1): snapshot export/import (JSONL/CSV; FS/S3/Blob) with manifests and parity checks.
  - CDC (D2): Debezium/Kafka → EntityChange stream; replicators to Postgres/Mongo with idempotency.
  - AI-aware indexer (D3): embed-on-change with Redis vector; embedding versioning and invalidation.
  - Vector & RAG (V1/R1): Redis vector + cache; `/ai/chat` (SSE), `/ai/embed`, `/ai/rag/query`; ai-probe.ps1.
  - AI provider ecosystem: Ollama integration for local models with streaming and health checks; OpenAI-compatible patterns.

- Web & capabilities
  - Capability Matrix endpoint: GET /.well-known/Koan/capabilities to report registered aggregates, providers, and flags (informational; protect or disable in prod).
  - Authorization posture presets: Allow-by-default or Deny-by-default with per-entity overrides and Defaults mapping to named policies; sample wiring in S7.ContentPlatform.

- Storage
  - Cloud providers: S3/Azure Blob/GCS adapters with presigned URLs, multi-part/resumable uploads, and lifecycle policies.
  - Pipeline steps: ingest policy hooks (size/MIME validation, DLP/AV scan, quarantine) with staging strategies.
  - HTTP surface: Koan.Web.Storage controllers with correct range/ETag/caching semantics and optional presign redirects.

- Foundations
  - API schemas & SSE format; gRPC draft; tokenization/cost plan; secrets provider; Redis guardrails; pgvector fast-follow plan.
  - Formats & grounding: Parquet materialization; JSON-LD manifests and Schema.org guidance on Entity exports.
  - Protocol interop: KServe/KFServing interop guide; GGUF probe notes for local models.

## Future steps

- AI growth

  - Proxy/connector: sidecar (P1) and central (T1) with quotas, per-tenant secrets, admin APIs; OpenAI shim/gRPC GA.
  - Multi-model routing: cost/latency/quality policies; safe tool registry audited function calling at scale.
  - Evaluation: golden tests expansion and optional eval harness integration.

- Vector & knowledge

  - Weaviate adapter implemented with Redis↔Weaviate migration utility planning; pgvector parity hardening in progress.
  - Knowledge & servers: SPARQL/RDF export guide; FAISS/HNSWlib local adapter option; vLLM/TGI optional adapters; MMEPs tracking.

- Data & replication

  - Virtualization & materialization: composed reads (D4) and scheduled Parquet exports (D5) with manifests.
  - Diff & reconcile (D6): drift detection and targeted replays; connector interop guides (D8).

- Protocol interop
  - Optional adapters: MCP server role and AI-RPC mapping; compatibility matrix and CI checks.
- Protocol adapters: MCP server role (tools/resources) and AI-RPC mapping layer for chat/embeddings.

---

See also: `docs/decisions/index.md` for architectural decisions and `docs/guides/*` for topic guides.

