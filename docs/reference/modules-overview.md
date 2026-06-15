---
type: REF
domain: platform
title: "Koan Modules Reference"
audience: [developers, architects]
status: draft
last_updated: 2025-10-06
framework_version: v0.6.3
validation:
  status: verified
  date_last_tested: 2025-10-07
  scope: docs/reference/modules-overview.md
---

# Koan Modules Reference


**Test Coverage:**

- All pillar test specs (Web, AI, Jobs, Storage, Media, Canon) are implemented and validated as of 2025-10-07.
- All major connectors (Sqlite, Postgres, SqlServer, Mongo, Redis, Json, Couchbase, OpenSearch, Vector, AI, Web, Messaging, Storage) are covered by automated test suites.
- See `tests/Suites/` for per-pillar and per-connector test implementations.

Use this reference when you plan a Koan solution and need to understand which modules to include. Modules are grouped by pillar. For each module you'll find:

- **Purpose** – what the package delivers.
- **How to use** – typical project references or configuration.
- **When to use** – scenarios where the module adds value.

> Tip: all packages live on NuGet under the `Sylin.Koan.*` namespace.

## 1. Core Platform

### Koan.Core

- **Purpose**: foundational runtime (`KoanEnv`, configuration helpers, DI bootstrap).
- **How to use**: reference `Koan.Core`; call `builder.Services.AddKoan()` in `Program.cs`.
- **When to use**: _always_ – every Koan app depends on this bootstrap layer.

### Koan.Core.Adapters

- **Purpose**: shared abstractions for auto-registration and adapter discovery.
- **How to use**: reference when building custom providers or advanced multi-source apps; exposes `IKoanAutoRegistrar`.
- **When to use**: when you author reusable modules or adapters that should plug into Koan discovery.

### Koan.Canon.Core & Koan.Canon.Web

- **Purpose**: canon (entity pipeline) runtime plus ASP.NET helpers.
- **How to use**: add `Koan.Canon.Core`; optionally `Koan.Canon.Web` for web-specific endpoints. Register canon flows via auto-registrars.
- **When to use**: when you need canonical projections, lineage tracking, or flow-style processing on top of entities.

### Koan.Flow.Core

- **Purpose**: fluent pipeline orchestrator for streaming workloads (`Flow.Pipeline`).
- **How to use**: reference `Koan.Flow.Core`; compose pipelines in application services.
- **When to use**: when you process large datasets or need background pipelines (e.g., enrichment jobs in S5.Recs).

### Koan.Jobs.Core

- **Purpose**: background job contracts, scheduling helpers, and hosting glue.
- **How to use**: reference package, register jobs via DI (`IJob` implementations) and configure job runner.
- **When to use**: durable operations (long transfers, nightly maintenance) that require retry/checkpoint semantics.

### ~~Koan.Scheduling~~ — migrated to agyo-tools 2026-06

- **Purpose**: cron/interval scheduling helpers (lightweight in-proc).
- **Status**: **Migrated to the `agyo-tools` sibling repo** as `Sylin.Agyo.Scheduling` (it was an opt-in helper, not core). For durable recurring work, prefer the Jobs ledger (`[JobAction(Schedule = ...)]`, JOBS-0005). See [`docs/assessment/08-agyo-reorganization.md`](../assessment/08-agyo-reorganization.md).

### Koan.Messaging.Core

- **Purpose**: event bus abstractions for message-driven integrations.
- **How to use**: reference for publish/subscribe interfaces; pair with messaging connectors (RabbitMQ, etc.).
- **When to use**: building asynchronous integrations, CQRS, or domain event propagation.

## 2. Data Pillar

### Koan.Data.Abstractions

- **Purpose**: `IEntity<TKey>`, repository interfaces, annotations (`[Identifier]`, `[Index]`).
- **How to use**: always include when modeling entities.
- **When to use**: anytime you define entity classes.

### Koan.Data.Core

- **Purpose**: `Entity<T>` base class, static helpers, lifecycle pipeline, new transfer DSL (`Copy/Move/Mirror`).
- **How to use**: inherit `Entity<T>`; call static helpers; leverage `EntityContext` for routing.
- **When to use**: core entity-first scenarios; most apps rely on it.

### Koan.Data.Relational

- **Purpose**: relational-friendly extensions (schema guard, migrations helpers).
- **How to use**: pair with SQL connectors (Postgres, SqlServer, Sqlite). Enables advanced indexing rules.
- **When to use**: when targeting relational databases and you need schema diagnostics.

### Koan.Data.Direct

- **Purpose**: low-level command execution (`DirectSession`) for raw SQL.
- **How to use**: reference the package, obtain `DirectSession` via DI, run parameterized commands.
- **When to use**: surgically executing SQL for maintenance or migration tasks without leaving Koan.

### ~~Koan.Data.Cqrs~~ _(removed 2026-06)_

- ~~**Purpose**: CQRS outbox/inbox helpers.~~ Removed 2026-06 — zero consumers/tests; superseded by the Jobs ledger outbox (JOBS-0005). See DATA-0019 (Superseded).

### Koan.Data.Backup & Koan.Web.Backup

- **Purpose**: backup orchestration for data + web surfaces.
- **How to use**: reference `Koan.Data.Backup` for data export jobs; `Koan.Web.Backup` exposes HTTP endpoints.
- **When to use**: compliance or DR pipelines needing database snapshots through Koan primitives.

### Koan.Cache

- **Purpose**: fluent caching client, policy registry, and adapter system (memory, Redis) aligned with `Entity<TEntity>` patterns.
- **How to use**: reference `Koan.Cache`; its auto-registrar invokes `AddKoanCache()` for you. Add provider packages (for example `Koan.Cache.Adapter.Redis`) and configure `Cache:Provider` plus adapter settings. Use `Cache.WithJson(...)`, `Cache.Tags(...)`, and `Entity<TEntity,TKey>.Cache` for policy-driven invalidation.
- **When to use**: memoizing expensive queries, broadcasting cache invalidations across web nodes, or coordinating stale-while-revalidate flows in background jobs.

### Koan.Data.Vector & Koan.Data.Vector.Abstractions

- **Purpose**: vector store integration (semantic search, embedding storage).
- **How to use**: reference abstractions (for embedding-rich entities) + connector (Weaviate, etc.).
- **When to use**: AI-powered workloads like S5.Recs vector exports.

## 3. Data Connectors

| Connector                                          | Purpose                     | When to use                     |
| -------------------------------------------------- | --------------------------- | ------------------------------- |
| `Koan.Data.Connector.Sqlite`                       | In-process relational store | Local dev, prototyping          |
| `Koan.Data.Connector.Postgres`                     | PostgreSQL adapter          | Production relational workloads |
| `Koan.Data.Connector.SqlServer`                    | SQL Server adapter          | Enterprise SQL deployments      |
| `Koan.Data.Connector.Mongo`                        | MongoDB adapter             | Document storage                |
| `Koan.Data.Connector.Redis`                        | Redis adapter               | Hot cache, session storage      |
| `Koan.Data.Connector.Json`                         | File-based JSON store       | Offline demos, tests            |
| `Koan.Data.Connector.Couchbase`                    | Couchbase adapter           | Distributed key/value           |
| `Koan.Data.Connector.OpenSearch` / `ElasticSearch` | Search index adapter        | Analytics, search pipelines     |
| `Koan.Data.Connector.Vector.*`                     | Vector backends             | Semantic search, embeddings     |

> All connectors auto-register through `IKoanAutoRegistrar` once referenced.

## 4. AI Pillar

### Koan.AI & Koan.AI.Contracts

- **Purpose**: AI orchestration utilities, prompts, shared contracts.
- **How to use**: reference in services that call LLMs; use abstractions for prompt templates.
- **When to use**: building AI-assisted features or orchestrating AI providers.

### Koan.AI.Web

- **Purpose**: ASP.NET integration for AI endpoints (controllers, SSE helpers).
- **How to use**: add package to web host exposing AI experiences.
- **When to use**: streaming AI responses into UI (chat, summarizers).

### AI Connectors (e.g., `Connectors/AI/Ollama`)

- **Purpose**: provider-specific clients.
- **How to use**: reference connector, configure required credentials/host.
- **When to use**: interacting with a specific AI runtime.

## 5. Flow, Jobs & Canon Integrations

- **Koan.Flow.Core** (pipelines) – see section 1.
- **Koan.Jobs.Core** (durable jobs) – see section 1.
- **Koan.Canon.Core/Web** (canonical pipelines) – use for advanced transformation chains.
- **Connectors**: e.g., `Connectors/Data/Mongo` for data provider integration. _(The former `Connectors/Service/Inbox` Redis inbox microservice was removed 2026-06 — its client API (`HttpInboxStore`) no longer existed in src and its only consumer was the archived S15 sample.)_

**Scenario**: S5.Recs uses Flow for embedding enrichment, Jobs for scheduled crawls, and Canon to maintain projection lineage.

## 6. Web & API Surface

### Koan.Web

- **Purpose**: core ASP.NET helpers (controller conventions, minimal API extensions, middleware).
- **How to use**: reference in web apps; calling `builder.Services.AddKoan();` automatically wires the web pipeline when `Koan.Web` is referenced.
- **When to use**: any web-facing Koan project.

### Koan.Web.Auth / Auth.Roles / Auth.Services

- **Purpose**: opinionated authentication, role management, auth services bridging into Koan.
- **How to use**: reference relevant packages, configure identity providers.
- **When to use**: building secured APIs or admin portals.

### Koan.Web.Extensions & Koan.Web.Transformers

- **Purpose**: utility filters, extension methods, payload transformers for HTTP responses.
- **How to use**: add package when customizing serialization or response shaping.
- **When to use**: API gateway scenarios, custom content negotiation.

### Web Connectors (Swagger, Auth)

- **Purpose**: plug-in support for Swagger docs and external auth providers. _(The GraphQL connector `Koan.Web.Connector.GraphQl` migrated to agyo-tools 2026-06 as `Sylin.Agyo.Web.GraphQl` — the HotChocolate CVE-treadmill belongs on agyo's cadence, not Koan's release train. See [`docs/assessment/08-agyo-reorganization.md`](../assessment/08-agyo-reorganization.md).)_
- **How to use**: reference connector, configure in `Program.cs`.
- **When to use**: bridging Koan web stack with specific UI technologies or documentation pipelines.

## 7. Orchestration & DevOps

### Koan.Orchestration.Abstractions / Aspire / Cli / Generators

- **Purpose**: infrastructure orchestration (templates, CLI commands, Aspire integration).
- **How to use**: reference `Koan.Orchestration.Abstractions` for hosting APIs; use Aspire package for `.NET Aspire` manifests; CLI package for command-line tooling via `koan` command.
- **When to use**: spinning up dev environments, managing deployments, generating scaffolding.

### Orchestration Connectors (Docker, Podman, Renderers)

- **Purpose**: provider-specific orchestrators.
- **How to use**: add the connector and run orchestration workflows.
- **When to use**: automated container orchestration or local dev environment spin-up.

## 8. Messaging

### Koan.Messaging.Core

- **Purpose**: base for event bus (publish/subscribe) – already covered.

### Messaging Connectors (RabbitMQ, etc.)

- **Purpose**: implementation of message transport.
- **How to use**: reference connector, configure broker endpoints.
- **When to use**: event-driven architectures requiring reliable transport.

## 9. Secrets & Storage

> **Secrets migrated to agyo-tools 2026-06.** The whole secrets stack now ships from the `agyo-tools` sibling repo as `Sylin.Agyo.Secrets.*` (it was consumed-but-peripheral, not core). Koan retains only a fail-soft reflection probe (`TryInvokeSecretsBootstrap` in `Koan.Data.Core`) that no-ops when the assembly is absent. See [`docs/assessment/08-agyo-reorganization.md`](../assessment/08-agyo-reorganization.md).

### ~~Koan.Secrets.Abstractions / Koan.Secrets.Core~~ → `Sylin.Agyo.Secrets.*`

- **Purpose**: secrets resolution pipeline, providers, caching.
- **How to use**: reference `Sylin.Agyo.Secrets.Abstractions` + `Sylin.Agyo.Secrets.Core` from agyo-tools; configure secret sources.
- **When to use**: centralizing secret retrieval (Vault, KMS, etc.).

### ~~Secrets Connectors (Vault)~~ → `Sylin.Agyo.Secrets.Connector.Vault`

- **Purpose**: provider-specific secret fetch.
- **How to use**: reference the agyo-tools connector, set `Koan:Secrets` configuration.
- **When to use**: retrieving secrets from Hashicorp Vault or similar stores.

### Koan.Storage & Storage Connectors (Local, etc.)

- **Purpose**: storage abstractions and provider implementations.
- **How to use**: reference core package plus connectors for local or cloud storage.
- **When to use**: file/blob storage needs inside Koan apps.

## 10. Media & MCP

### Koan.Media.Abstractions / Koan.Media.Core / Koan.Media.Web

- **Purpose**: image/video processing pipelines and web endpoints.
- **How to use**: include these when handling media uploads, transformations, or CDN flows.
- **When to use**: portals requiring media manipulation.

### Koan.Mcp

- **Purpose**: Model Context Protocol integration for AI ecosystems.
- **How to use**: reference package when your app exposes MCP-compatible endpoints.
- **When to use**: bridging Koan apps with MCP clients/tools.

## 11. Observability (recipes removed 2026-06)

> **`Koan.Recipe.Abstractions` was deleted** — superseded by the ARCH-0086 `KoanModule` primitive (its AppDomain-scan bootstrap idiom is no longer needed; it did **not** migrate). The **observability bundle migrated to agyo-tools** as `Sylin.Agyo.Observability`, re-homed as a `KoanModule` rather than an `IKoanRecipe`. See [`docs/assessment/08-agyo-reorganization.md`](../assessment/08-agyo-reorganization.md).

### ~~Koan.Recipe.Abstractions~~ (deleted) / ~~Koan.Recipe.Observability~~ → `Sylin.Agyo.Observability`

- **Purpose**: observability instrumentation (health checks, resilient HTTP, optional OpenTelemetry).
- **How to use**: reference `Sylin.Agyo.Observability` from agyo-tools; it self-wires as a `KoanModule`.
- **When to use**: standardizing logging/metrics/health across services. _(Authoring composable bootstrap bundles is now the job of `KoanModule` in `Koan.Core`, not a recipe abstraction.)_

## 12. Connector Index

For quick lookup, connectors live under `src/Connectors`. Use this table to identify package prefixes:

| Category      | Example packages                 | Notes                            |
| ------------- | -------------------------------- | -------------------------------- |
| Data          | `Koan.Data.Connector.*`          | Databases, caches, vector stores |
| AI            | `Koan.AI.Connector.*`            | LLM / AI runtimes                |
| Messaging     | `Koan.Messaging.Connector.*`     | Brokers like RabbitMQ            |
| Orchestration | `Koan.Orchestration.Connector.*` | Docker, Podman, renderers        |
| Secrets       | `Sylin.Agyo.Secrets.Connector.*` | Vault, cloud secret stores _(moved to agyo-tools 2026-06)_ |
| Storage       | `Koan.Storage.Connector.*`       | Local, S3-compatible storage     |
| Web           | `Koan.Web.Connector.*`           | Swagger, Auth providers          |

Every connector package auto-registers via `IKoanAutoRegistrar` once referenced.

## Choosing Modules (Checklist)

1. **Start with core** (`Koan.Core`, `Koan.Data.Core`) to get entity-first APIs.
2. **Add pillars** needed for your workload (Data Vector, Flow, Jobs, AI, Web, Messaging).
3. **Select connectors** that match infrastructure targets (database, broker, storage).
4. **Incorporate tooling** (Orchestration, Recipes) to support deployment and observability.
5. **Validate** using BootReport and health checks before shipping.

This reference should help you assemble the right Koan stack for each solution while understanding when each module shines.
