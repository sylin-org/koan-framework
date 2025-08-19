# Sora Feature Catalog (Value-centered)

A modular .NET framework that standardizes data, web, and messaging patterns with strong governance and observability—so teams ship faster with fewer surprises, and platforms scale with consistency.

## Pillars → outcomes

- Core
  - Predictable apps by default (health/readiness, env flags, secure headers, boot reports, OpenTelemetry).
- Data
  - Adapter-agnostic persistence with capability discovery, pushdown-first performance, safe Direct escape hatch.
- Web
  - Controller-driven APIs (REST/GraphQL) with guardrails, content negotiation, consistent paging/filtering.
- Messaging
  - Capability-aware, cross-broker semantics (aliases, DLQ/retry, idempotency) with simple handler wiring.
- Services & DX
  - Fast onboarding (Tiny* templates, meta packages), reliable test ops (Docker probe), decision clarity (normalized ADRs).

## Scenarios and benefits

- Greenfield APIs/services: ship quickly on JSON/SQLite; swap to Postgres/SQL Server/Mongo without API churn.
- Enterprise data services: enforceable governance (DDL policy, naming, projection), measurable performance (pushdown), and traceability (db.* tags).
- CQRS/event-driven: inbox/idempotency, batch semantics, provider-neutral retries/DLQ.
- Ops/reporting: Direct API for audited, parameterized ad-hoc access; neutral rows; limits and policy gates.
- Modern UI backends: REST + GraphQL from the same model with consistent naming and filter semantics.

## Strategic opportunities

- Platform standardization: common naming, controllers-only HTTP, centralized constants, ADR taxonomy.
- Progressive hardening: start permissive in dev; tighten paging caps, CSP, DDL policy, discovery gating.
- Polyglot without chaos: capability flags make differences explicit; shared policy (naming, projection, pushdown).
- Observability-first: spans/metrics + boot reports enable SLOs and faster incident response.

## Risks and guardrails

- Accidental DDL/unsafe ops in prod → magic gating, DDL policy (NoDdl), prod-off discovery, redacted boot report.
- Hidden in-memory fallbacks → pushdown MUST; `Sora-InMemory-Paging` header signals fallback.
- Adapter drift → centralized instruction constants, capability flags, relational toolkit + LINQ translator.

## Next steps (proposals)

- Adapters: add MySQL; add Azure Service Bus and Kafka providers; keep capability negotiation.
- Direct API: add streaming (IAsyncEnumerable) and multi-result support; provide production read-only presets.
- GraphQL: depth/complexity guards and persisted queries using storage-based naming.
- CI/quality gates: ADR linter (front-matter + taxonomy prefix), paging pushdown checks, tracing assertions.
- Docs/DX: keep this catalog updated; link to ADRs and samples for each feature.

---

See also: `docs/decisions/index.md` for architectural decisions and `docs/guides/*` for topic guides.
