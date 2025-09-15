---
id: ARCH-0043
slug: lightweight-parity-roadmap
domain: Architecture
status: Draft
date: 2025-08-25
title: Lightweight parity roadmap — Observability, Resilience, Recipes, Dev Profiles, Hybrid Search
---

## Context

Koan aims to stay library-first and lightweight while offering a competitive developer and operations experience comparable to .NET Aspire/Dapr. External analysis positions Koan strong on agility, AI integration, and portability, with gaps in turnkey observability, orchestration/dev-hosting, and “enterprise fit” guardrails (reliability patterns, presets).

We need a focused, low-friction path to raise parity without becoming a heavy platform.

Constraints and guardrails
- Additive and opt-in via small NuGet packages + extension methods.
- MVC controllers for HTTP surfaces; avoid inline endpoints (ref: WEB-0035-entitycontroller-transformers.md).
- Prefer first-class model statics for data access (ref: DATA-0061-data-access-pagination-and-streaming.md).
- No magic values; use constants/options (ref: ARCH-0040-config-and-constants-naming.md).
- Interop with existing ecosystems (OTEL, Polly, Compose/Aspire) rather than reinvent.

## Decision (prioritized next steps)

Phase 1 — high value / low complexity (target immediate sprints)
1) Observability preset
   - Deliver AddKoanTelemetry() with sane defaults for OpenTelemetry across HTTP, data adapters, messaging, and AI.
   - Options expose exporters (console/OTLP) and sampling; Aspire-aware to avoid duplicate exporters (ref: ARCH-0033-opentelemetry-integration.md).
2) Resilience preset
   - AddKoanResilience() wiring Polly-style retry, timeout, and circuit breaker policies across HTTP clients, messaging dispatchers, and data adapters.
   - Policy names/keys centralized via Constants; toggles via IOptions.
3) Integration recipes
   - Koan.Recipes.* micro-packages for Postgres, Mongo, Redis, RabbitMQ, Vector DBs (Weaviate/Qdrant).
   - Each recipe composes: health checks, telemetry, resilience, and minimal migrations/ensure-created.
4) Dev Profiles + compose samples
   - Conventional dev/ci/prod profiles and sample docker-compose files per recipe; kept out of runtime packages.
5) Hybrid search helper
   - A thin helper that blends structured filters with vector similarity and optional rescoring (ref: DATA-0054, DATA-0056).

Phase 2 — high value / medium complexity
6) Inbox/Outbox turnkey
   - Promote existing patterns to first-class, storage-backed adapters (SQL/Mongo/Redis) with defaults (ref: DATA-0019, DATA-0020).
7) Projection builder + worker glue
   - AddProjection<TAgg,TRead>() plus hosted processing for read models; controller samples stay attribute-routed.
8) AI embedding orchestration
   - [Embeddable] attribute and an EmbedderWorker that maintains vector indexes on save; backoff + metrics (ref: AI-0001, AI-0008).
9) Compose generator (optional)
   - Scan referenced Koan adapters to emit compose.Koan.yml for local up/down.

Phase 3 — medium value / higher complexity
10) Diagnostics Console (secured)
    - Minimal, self-hosted view for traces/health/AI usage. Dev-only by default.
11) Multitenancy primitives
    - ITenantAccessor and tenant-aware options/data partitioning helpers.
12) Pluggable event store abstraction
    - Multiple backends (SQL/Mongo/EventStoreDB) behind a single IEventStore, used by projections and ES consumers.
13) CLI (Koan dev …)
    - Thin wrapper for dev up/down/logs/status; export compose/helm as artifacts.

## Contract (for Phase 1 packages)
Inputs
- ServiceCollection + optional configuration section.
- Environment profile (Development/CI/Production).

Outputs
- Registered telemetry exporters and instrumentations; policy handlers; health checks.

Error modes
- Misconfiguration → startup validation error with actionable messages.
- Exporter/endpoint unreachable → degraded mode with warnings; app remains functional.

Success criteria
- Traces/metrics/logs available in <5 minutes locally.
- Resilience policies active by default in dev; observable via logs/OTEL attributes.
- Recipes: single call to AddKoan<Postgres|Mongo|Redis|RabbitMq|Weaviate>() yields a runnable sample with health and OTEL.

## Scope

In-scope
- New micro-packages (Diagnostics/Resilience/Recipes) and docs.
- Samples and templates; no breaking changes to existing APIs.

Out-of-scope (for Phase 1)
- Full CLI tooling, UI consoles, and multitenancy.

## Consequences

Positive
- Faster time-to-observability and local setup; clearer defaults and posture.
- Enterprise-fit guardrails without heavyweight runtime dependencies.

Trade-offs
- Slight increase in package surface area; mitigated by small, focused modules.
- Aspire/Dapr users must ensure exporters are not duplicated; addressed via detection toggles.

## Implementation notes

- Packaging
  - Koan.Diagnostics (AddKoanTelemetry), Koan.Resilience (AddKoanResilience), Koan.Recipes.* (one per adapter).
- Options and constants
  - Centralize policy names/headers/route segments under Infrastructure/Constants per ARCH-0040.
- Web and data
  - Keep controllers-only HTTP; examples use first-class model statics (All/Query/Stream/Page) per DATA-0061.
- Docs
  - Per-project companion docs for new packages (ARCH-0042). Add reference pages with short, production-safe snippets.

## Follow-ups

- Draft detailed contracts for AddKoanTelemetry and AddKoanResilience (options, defaults, example wiring).
- Define the minimal set of adapters covered by initial Recipes: Postgres, Redis, RabbitMQ, Weaviate.
- Create Dev Profiles guidance and sample compose files under docs/support/ or samples/.

## References

- ARCH-0040-config-and-constants-naming.md
- ARCH-0041-docs-posture-instructions-over-tutorials.md
- ARCH-0042-per-project-companion-docs.md
- ARCH-0033-opentelemetry-integration.md
- WEB-0035-entitycontroller-transformers.md
- DATA-0061-data-access-pagination-and-streaming.md
- DATA-0054-vector-search-capability-and-contracts.md; DATA-0056-vector-filter-ast-and-translators.md
- DATA-0019-outbox-helper-and-defaults.md; DATA-0020-outbox-provider-discovery-and-priority.md
- AI-0001-ai-baseline.md; AI-0008-adapters-and-registry.md
