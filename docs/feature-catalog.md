# Sora Feature Catalog (Value-centered)

A modular .NET framework that standardizes data, web, messaging, and AI patterns with strong governance and observability—so teams ship faster with fewer surprises, and platforms scale with consistency.

## Pillars → outcomes

- Core
  - Predictable apps by default (health/readiness, env flags, secure headers, boot reports, OpenTelemetry).
- Data
  - Adapter-agnostic persistence with capability discovery, pushdown-first performance, safe Direct escape hatch.
- Web
  - Controller-driven APIs (REST/GraphQL) with guardrails, content negotiation, consistent paging/filtering.
- Messaging
  - Capability-aware, cross-broker semantics (aliases, DLQ/retry, idempotency) with simple handler wiring.
- AI
  - Turnkey inference (streaming chat, embeddings) with minimal config; Redis-first vector + cache; RAG defaults; observability and budgets; optional sidecar/central proxy; one-call AddSoraAI() and auto-boot discovery.
- Services & DX
  - Fast onboarding (Tiny\* templates, meta packages), reliable test ops (Docker/AI probes), decision clarity (normalized ADRs).

## Scenarios and benefits

- Greenfield APIs/services: ship quickly on JSON/SQLite; swap to Postgres/SQL Server/Mongo without API churn.
- Enterprise data services: enforceable governance (DDL policy, naming, projection), measurable performance (pushdown), and traceability (db.\* tags).
- CQRS/event-driven: inbox/idempotency, batch semantics, provider-neutral retries/DLQ.
- Ops/reporting: Direct API for audited, parameterized ad-hoc access; neutral rows; limits and policy gates.
- Modern UI backends: REST + GraphQL from the same model with consistent naming and filter semantics.
- AI assist & RAG: `/ai/chat` with SSE, `/ai/embed`, and `/ai/rag/query` with citations; Redis vector and cache by default; pgvector fast-follow.
- Data bridge: snapshot export/import (JSONL/CSV/Parquet), CDC via Debezium/Kafka, virtualization (composed reads), scheduled materialization.

## Strategic opportunities

- Platform standardization: common naming, controllers-only HTTP, centralized constants, ADR taxonomy.
- Progressive hardening: start permissive in dev; tighten paging caps, CSP, DDL policy, discovery gating.
- Polyglot without chaos: capability flags make differences explicit; shared policy (naming, projection, pushdown).
- Observability-first: spans/metrics + boot reports enable SLOs and faster incident response.
- On-ramp path: adapters → transfer/replication → AI-aware indexing → RAG—value compounds with low switching cost.
- Protocol interop: optional adapters for gRPC (internal), OpenAI-compatible shim, MCP (Model Context Protocol), and AI-RPC to meet teams where they are.

## Risks and guardrails

- Accidental DDL/unsafe ops in prod → magic gating, DDL policy (NoDdl), prod-off discovery, redacted boot report.
- Hidden in-memory fallbacks → pushdown MUST; `Sora-InMemory-Paging` header signals fallback.
- Adapter drift → centralized instruction constants, capability flags, relational toolkit + LINQ translator.
- AI cost/leakage → token/time budgets, prompt hashing, redaction-by-default, model allow-lists.
- Vector posture → Redis guardrails (memory/persistence/HA); pgvector parity tests; migration utilities.

## Coming soon (on-ramp and near-term)

- Cognitive coalescence (AI North Star)

  - Auto-boot orchestrator: probes providers (Ollama/OpenAI), vectors (Redis/pgvector), secrets, budgets, and protocols; single boot report with redacted status and action hints.
  - One-call DI: AddSoraAI() wires providers, tokenization/cost, budgets/moderation, SSE, telemetry, headers, and /ai/\* controllers.
  - AI profiles: SORA_AI_PROFILE = DevLocal | HostedKeyed | ProxyClient | TestHarness; sensible defaults for model, streaming, budgets, vector, cache TTLs.
  - Convention endpoints: /ai/chat, /ai/embed, /ai/rag/query, /ai/models auto-enable when Ready; OpenAI-shim and gRPC via single flags.
  - Zero-scaffold RAG quickstart: first run indexes docs/ with defaults; Redis-first vector + cache; background index job; safe projection/redaction.
  - Vector autodiscovery: Redis HNSW index bootstrap with sane metrics; pgvector fast-follow with auto-migrate if permitted.
  - Secrets layering: prefer secrets provider (KV/Secrets Manager) with env fallback; never log values; redacted boot entries.
  - Policy presets: DevPermissive | StagingBalanced | ProdConservative for token/time budgets, moderation, model allow-lists.
  - Capabilities surface: Sora-AI-\* headers and GET /ai/capabilities expose provider/vector flags, budgets, protocol availability.
  - Probes & health: ai-probe.ps1 mirrors /health/ready details; precise remediation messages.
  - Record/replay harness: SORA_AI_TEST_RECORD to capture; replay in CI; cassettes sanitized by default.
  - DX: TinyAI template and AI transformer recipes (summarize/classify) for Web/MQ.

- On-ramp

  - Platform ramp-up: Redis core cache/session; MySQL relational adapter; optional CouchDB.
  - Data Bridge (D1): snapshot export/import (JSONL/CSV; FS/S3/Blob) with manifests and parity checks.
  - CDC (D2): Debezium/Kafka → EntityChange stream; replicators to Postgres/Mongo with idempotency.
  - AI-aware indexer (D3): embed-on-change with Redis vector; embedding versioning and invalidation.
  - Vector & RAG (V1/R1): Redis vector + cache; `/ai/chat` (SSE), `/ai/embed`, `/ai/rag/query`; ai-probe.ps1.
  - Vector contracts (planned): Sora.Data.Vector (IVectorSearchRepository, options, instructions) and first adapter Sora.Data.Weaviate; see ADR DATA-0054 and guides/adapters/vector-search.md.

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

  - Weaviate adapter + Redis↔Weaviate migration utility; pgvector parity hardening.
  - Knowledge & servers: SPARQL/RDF export guide; FAISS/HNSWlib local adapter option; vLLM/TGI optional adapters; MMEPs tracking.

- Data & replication

  - Virtualization & materialization: composed reads (D4) and scheduled Parquet exports (D5) with manifests.
  - Diff & reconcile (D6): drift detection and targeted replays; connector interop guides (D8).

- Protocol interop
  - Optional adapters: MCP server role and AI-RPC mapping; compatibility matrix and CI checks.
- Protocol adapters: MCP server role (tools/resources) and AI-RPC mapping layer for chat/embeddings.

---

See also: `docs/decisions/index.md` for architectural decisions and `docs/guides/*` for topic guides.
