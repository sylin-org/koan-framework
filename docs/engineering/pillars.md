# Koan pillars — quick reference

Purpose
- A short, instruction-first overview of Koan’s pillars for quick orientation and cross-linking. Use this when socializing the framework or picking the right module for a job.

Core
- What: Unified runtime/config, health/readiness, secure defaults, auto-registration pipeline, boot reports, optional OpenTelemetry.
- Why: Predictable apps by default and low-friction module onboarding.
- References: architecture/principles.md

Data
- What: Adapter-agnostic persistence (Postgres/SQL Server/SQLite/MongoDB/Redis/JSON), pushdown-first performance, safe Direct escape hatch, CQRS/outbox, vector split.
- Why: Consistent data patterns with capability flags and performance you can reason about.
- References: reference/data-access.md, decisions/DATA-0061-data-access-pagination-and-streaming.md

Storage
- What: Profile-based storage orchestrator with thin providers; local filesystem provider; DX helpers and a model-centric API.
- Why: Simple, safe file/object storage with clean routing and developer ergonomics.
- References: reference/storage.md, decisions/STOR-0001-storage-module-and-contracts.md

Media
- What: First-class media handling (upload, variants, derivatives, pipelines/tasks) with ancestry and DX-first model statics.
- Why: Enterprise-grade media with low cognitive load; safe on-demand transforms; CDN-friendly delivery.
- References: reference/media.md, decisions/MEDIA-0001-media-pillar-baseline-and-storage-integration.md

Web
- What: Controllers-only HTTP (REST/GraphQL), startup pipeline wiring, Swagger in dev, payload transformers.
- Why: Clear, testable web surfaces with consistent semantics.
- References: reference/web.md, decisions/WEB-0035-entitycontroller-transformers.md

Scheduling
- What: Background job orchestrator with OnStartup tasks, per-task timeouts, health facts, readiness gating.
- Why: Reliable boot-time work and background processing without bespoke scaffolding.
- References: guides/scheduling-and-bootstrap.md

Messaging
- What: Capability-aware semantics (aliases, retry/DLQ, idempotency), RabbitMQ transport, inbox services (HTTP/Redis/InMemory).
- Why: Portable messaging with predictable behavior across transports.
- References: reference/messaging.md

AI
- What: Chat/embeddings with vector integration, Ollama provider, RAG building blocks, budgets and observability.
- Why: Practical AI features with safe defaults and costs in mind.
- References: reference/ai.md

Services & DX
- What: Tiny templates, auto-registration across modules, decision docs (ADRs), container-smart defaults.
- Why: Fast onboarding and consistent engineering ergonomics across teams.
- References: engineering/index.md, decisions/index.md

Notes
- Controllers only (no inline endpoints) and centralized constants are default guardrails.
- Prefer first-class model statics for data access and storage DX helpers for content operations.