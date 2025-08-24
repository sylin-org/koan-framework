# Engineering Guide

Audience: humans and agentic code LLMs. This is the front door for building in Sora. It curates the most important rules and points to canonical references.

## Read first

- Engineering Guardrails (deep dive): ../guides/core/engineering-guardrails.md
- Architecture Principles (curated ADRs): ../architecture/principles.md
- Data access semantics (contract): ../guides/data/all-query-streaming-and-pager.md and decisions/DATA-0061-data-access-pagination-and-streaming.md
- Web transformers and controllers: decisions/WEB-0035-entitycontroller-transformers.md
- Constants and configuration naming: decisions/ARCH-0040-config-and-constants-naming.md

## Top directives (short)

1) Prefer first-class static model methods for data access
- MyModel.All(ct), Query(...), AllStream(...), QueryStream(...), FirstPage(...), Page(...)
- Generic facades (Data<TEntity,TKey>) are second-class; use only when a model static isn’t available.
- All/Query without paging must fully materialize the result; for large sets, use streaming or explicit paging.

2) Controllers, not inline endpoints
- Attribute-routed MVC controllers only. No MapGet/MapPost in startup or module initializers.

3) No stubs, no placeholders, no scattered literals
- Remove empty artifacts. Hoist stable values into a project-scoped Constants class. Use typed Options for tunables.

4) Simple composition, explicit options, deterministic behavior
- DI-first extension methods (AddXyz/UseXyz). Discovery is opt-in and Dev-only. Explicit config always wins.

5) Service lifetimes and scope
- Prefer Singleton for clients/factories; Transient for small stateless helpers; Scoped only when truly required.

6) Observability and safety
- CancellationToken on I/O, structured logs, metrics, ProblemDetails for errors. Guardrails on paging and streaming.

## Quick-reference by area

- Data
  - Semantics: All/Query materialize; use Stream or Pager for large sets.
  - Adapters: lean defaults (Dapper/ADO.NET for relational; Mongo/Redis SDKs; EF optional).
  - See: ../guides/data/index.md, ../guides/data/all-query-streaming-and-pager.md

- Web
  - Controllers only, secure headers defaults, transformers for entity payload shaping.
  - See: ../guides/web/index.md, decisions/WEB-0035-entitycontroller-transformers.md
  - Auth: centralized challenge/callback/logout and provider discovery — see: ../reference/web-auth.md and ../api/web-http-api.md (challenge supports `return` and optional `prompt=login`; Dev TestProvider cookie cleared by central logout)

- Messaging
  - IBus + IMessageBatch; explicit idempotency/retry options.
  - See: ../guides/messaging/index.md, decisions/MESS-0021-messaging-capabilities-and-negotiation.md

- AI
  - AddAiDefaults + MapAgentEndpoints; local-first providers; safety filters on in Dev.
  - See: ../guides/ai/index.md

- Config & constants
  - Use Sora.Core.Configuration helpers; centralize names; avoid ad-hoc cfg["..."] lookups.
  - See: decisions/ARCH-0040-config-and-constants-naming.md

## PR checklist (operational)

- Readability and simplicity over cleverness; small, legible types.
- Routes live in controllers; no inline endpoints.
- No empty classes or commented scaffolds.
- Constants centralized; options validated on start.
- Data access uses model statics; large sets use streaming or explicit paging.
- Logs/metrics present for important operations; no secrets/PII in logs.

## Docs style & checklist
- See: ./docs-style-and-checklist.md
