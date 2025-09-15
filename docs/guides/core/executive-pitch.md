# Koan Framework — Executive Pitch

## Executive summary
Koan is a modern .NET framework that makes building reliable, cloud‑ready services fast and predictable without hidden magic. It preserves a zero‑scaffolding developer experience while enforcing clean composition, strong observability, and modular, opt‑in capabilities (Data, Messaging, Webhooks, AI). The result: production‑grade services that are easy to understand, implement, and run.

Why it matters: teams ship consistent services quickly, integrate with enterprise systems safely, and adopt AI and event‑driven patterns at their own pace—without re‑architecting.

## Benefits at a glance
- Faster delivery: AddDefaults + opinionated profiles accelerate a working service in minutes.
- Operational confidence: built‑in health, metrics, tracing, graceful degradation, and deterministic configuration.
- Optional everything: adopt only what you need—no runtime weight or complexity unless enabled.
- Data flexibility: relational (Dapper by default, EF optional), document (Mongo, Redis), vector (PGVector/Qdrant) via focused adapters.
- Enterprise integration: messaging (RabbitMQ/Azure SB), outbox, idempotency, retries/DLQ, and robust webhooks.
- AI‑ready: one‑liner enablement, local/provider auto‑selection, minimal endpoints, safe defaults.
- Cost control: lightweight default paths, predictable I/O, and minimal per‑request allocations.

## Strategic opportunities
- Standardized platform layer: a consistent way to build services across teams without stifling choice.
- Event‑driven adoption: CQRS and messaging when ready; not mandated up front.
- AI augmentation: add agent endpoints and vector memory to existing services with minimal churn.
- Multi‑cloud posture: adapters for AWS/Azure/GCP services and open‑source backends; avoid lock‑in.
- Compliance by construction: PII hygiene, audit hooks, and policy‑driven behaviors in the data pipeline.

---

## For Enterprise Architecture
Koan creates a predictable substrate that reduces divergence and long‑term maintenance risk.

Key specifics
- Deterministic composition: explicit DI; discovery is opt‑in and Development‑only; explicit config always wins.
- Profiles (preset bundles): Lite (Core+Web), Standard (+Data), Extended (+Auth/Storage), Distributed (+Messaging/CQRS/Webhooks) for fast, consistent setups.
- Clear guardrails: SoC, KISS, YAGNI, DRY; no hidden magic; small, legible types; async with CancellationToken across I/O.
- Reliability as a policy: graceful degradation for non‑critical modules (cache, webhooks, background workers); fail‑fast for critical dependencies (primary data, HTTP host, required auth). Health surfaces Degraded vs Unhealthy clearly.
- Observability first: OpenTelemetry tracing/metrics, structured logs, health/readiness/liveness wired from day one.
- Security posture: production‑safe defaults, config‑driven auth requirements (supports perimeter‑auth), secret management hooks; warnings when dev discovery uses insecure backends.
- Extensibility: mid‑level adapter families (Relational/Document/Vector) plus optional Search/Graph/Time‑series; Redis included in v1.
- Runtime portability: WSL‑first for local Linux containers, Kubernetes‑friendly lifecycles, and cloud‑aware defaults.

Benefits
- Consistency across services and teams
- Easier audits and risk reviews
- Smooth path to event‑driven and AI capabilities
- Lower TCO via shared tooling, patterns, and docs

## For Enterprise Integration
Koan focuses on reliable, traceable connectivity in and out of the enterprise.

Key specifics
- Messaging & CQRS: IBus providers (RabbitMQ first, Azure Service Bus next), Command/Query/Event buses, outbox, idempotency keys, retries/backoff, DLQ, consumer groups with round‑robin dispatch.
- Webhooks: inbound HMAC + timestamp + replay protection; outbound signing, retries, DLQ, delivery logs; optional CloudEvents.
- Schema & contracts: JSON by default; optional Avro/Protobuf; structured error/ProblemDetails.
- Data adapters: Dapper/ADO.NET (default), EF optional (relational‑only), Mongo, Redis; vector: PGVector/Qdrant; Cosmos via native SDK; clear capability boundaries.
- Discovery with guardrails: dev‑only, explicit override precedence, logs/metrics for each probe, and isolation‑aware warnings.
- Health & diagnostics: module health contributors expose readiness; consistent correlation/trace propagation across boundaries.

Benefits
- Reliable flows with strong delivery semantics
- Easier third‑party integration via robust webhook model
- Reduced incidents thanks to retries, DLQ, and observability
- Future‑proofed data connectivity (relational, document, vector)

## For Development Teams
Koan emphasizes a legible, low‑ceremony workflow that scales with needs.

Key specifics
- Zero‑scaffolding experience: AddDefaults/UseDefaults; minimal API mappers for CRUD/query/batch; explicit modules when you need more.
- Clear contracts: IDataRepository with batch (IBatchSet), repository operation pipeline (validation, PII, audit, soft‑delete), IMessageBatch for messaging.
- Samples as milestones: S0 console (JSON), S1 Web (Dapper/Sqlite), S2 Compose (Mongo), through S7 Full‑stack with observability—each sample is runnable and tested.
- Friendly dev loop: WSL + Docker Compose; Testcontainers for integration tests; dev discovery that “just works” (with warnings) while staying safe.
- Minimal magic: no static global state, minimal scoped lifetimes, explicit context objects instead of ambient state.
- Optional EF and generators: EF is available for relational scenarios; source generators deferred (optional later) to keep builds fast and code obvious.

Benefits
- Fast “Hello, Production”—working service in minutes, not weeks
- Code you can reason about and debug easily
- Opt‑in complexity only when you need it
- Strong tests and samples to copy from

---

## What ships in v1 (condensed)
- Core runtime + AddDefaults/UseDefaults
- Data: Dapper/ADO.NET (default), EF optional (relational‑only), Mongo, Redis, vector (PGVector; Qdrant optional)
- Web: minimal APIs, ProblemDetails, health/OTEL
- Messaging: RabbitMQ provider, CQRS buses, outbox, idempotency, retries/DLQ, consumer groups
- Webhooks: inbound verification + outbound delivery with logs
- Security: config‑driven auth requirements; perimeter‑auth mode supported
- DX: WSL‑first dev, Testcontainers, templates, S0–S7 samples

## Call to action
- Pilot Koan on a new service using the Standard profile (Core+Web+Data) and S1/S2 samples as a blueprint.
- Expand to messaging/webhooks/AI as use‑cases demand; the path is prepared.
- Institutionalize Koan with shared adapters and policies so teams move faster, safer, and with less rework.
