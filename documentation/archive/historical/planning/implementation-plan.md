# Koan Implementation Plan (milestones tied to samples)

Principle: Each milestone ends with a runnable sample proving the slice. CI and minimal docs accompany it.

## M0 - Repo bootstrap (docs only)

- Create Koan solution skeleton (no runtime code yet)
- Docs migrated and structured (this folder)
- ADRs: Adapter families; Discovery precedence; Profiles; Batching semantics
- Outcome: docs build + lint; plan approved

## M1 - Core & S0 (Console + JSON repo)

- Runtime: Core hosting, options + validation, logging, OpenTelemetry stubs, health system
- Data: Repository interfaces, JSON-file adapter, operation pipeline (Validation, PII, Audit, Soft-delete), batch APIs (IBatchSet)
- DX: AddKoan()/UseKoan() with Core + minimal diagnostics (one-liner helper optional)
- Sample S0: console app performing CRUD + batch on a Todo aggregate via JSON adapter
- CI: unit tests for options and JSON adapter; smoke test for S0

## M2 - Web & S1 (Web API + Dapper/Sqlite) + S1b (EF optional)

- Web: minimal API mappers (CRUD, query, batch); ProblemDetails
- Data: Relational adapter (Dapper/Sqlite); migrations via DbUp/FluentMigrator; EF Core Sqlite optional adapter
- Security: basic rate limiting and secure headers
- Sample S1: Web API using Dapper/Sqlite
- Sample S1b: optional EF Sqlite variant
- CI: integration test with Testcontainers for Sqlite (file), unit tests for mappers

## M3 - Discovery & S2 (Compose Client + API + Mongo)

- Data: MongoDB adapter; Dev-only discovery with precedence/warnings
- Compose: docker-compose for api + mongo + client; WSL-first dev flow
- Telemetry: OTEL tracing/metrics baseline
- Sample S2: client calls API; isolated-network warning downgrade
- CI: Compose smoke via health checks; Mongo Testcontainers integration test
- CI/CD: Tag-only NuGet publish; nightly canary to GitHub Packages; unified versioning via version.json

## M4 - Auth/Storage (defer to later sample)

- Auth: OIDC/JWT/API keys; policy helpers
- Storage: FS + S3 + Azure Blob SDKs
- Config: perimeter-auth mode toggle (allows relaxing in-app auth when behind gateway)
- Sample: protected endpoints and API key admin route (moved after messaging milestone)
- CI: auth policy tests; storage unit tests

## M5 - Messaging foundations & S4 (RabbitMQ + multi-DB)

- Messaging: RabbitMQ provider, Send/SendMany sugar, DI-based typed handlers, provisioning defaults, health
- CQRS: CommandBus/QueryBus/EventBus (scaffolded/planned); outbox (relational + Mongo)
- Samples: ships a minimal `S3.Mq.Sample` (publisher + handler + compose)
- Sample S4: products/activity with two adapters (Sqlite + Mongo); ProductCreated → Activity via event handler; workers for background handling
- CI: Testcontainers RabbitMQ integration tests; outbox reliability tests

## M6 - Webhooks & S5

- Inbound: HMAC + timestamp skew + replay protection (store-agnostic)
- Outbound: registration, signing, retries/backoff, DLQ, delivery logs
- Sample S5: end-to-end webhook flows and delivery audit
- CI: end-to-end webhook signature tests

## M7 - AI & S6 (Agent API)

- AI: AddAiDefaults, MapAgentEndpoints, local provider detection, OpenAI/Azure wiring
- Vector: PGVector adapter first; Qdrant optional
- Sample S6: RAG + tools; in-memory vector for dev with optional PGVector/Qdrant
- CI: provider-switch tests; basic ingestion/query tests

## M8 - Full-stack & S7 (Compose)

- Frontend: simple React/Vite front served by API
- Observability: OTLP collector, Jaeger, Prometheus, Grafana dashboards
- Sample S7: end-to-end CQRS with UI, tracing across services
- CI: compose health smoke + a few UI smoke tests

## Deferred items

- Versioning (snapshots) pipeline behavior
- Additional adapters (Redis shipped earlier as Document, more to follow)
- Source generator (decision revisit after v1)

## Notes

- Discovery defaults: On in non-Production, Off in Production unless `Koan:AllowMagicInProduction=true`. You can force via `Koan:Messaging:Discovery:Enabled`.
- Explicit configuration (e.g., `Koan:Messaging:Inbox:Endpoint`) always overrides and skips discovery.
- Adapter factories can indicate priority using `ProviderPriorityAttribute` to influence default provider selection.
