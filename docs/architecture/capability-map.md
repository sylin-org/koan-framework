---
type: ARCHITECTURE
domain: framework
title: "Koan Capability Map"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: pre-1.0
validation:
  date_last_tested: 2025-09-29
  status: verified
  scope: docs/architecture/capability-map.md
---

# Koan Capability Map

## Contract

- **Scope**: Provide a guided tour of first-class Koan modules under `src/`, how they compose, and when to reach for each package.
- **Inputs**: `AddKoan()` bootstrap pipeline, auto-registrars, entity-first models, controller-first web stack, orchestration CLI, and data/AI/messaging adapters. _(Recipes and secrets moved to agyo-tools 2026-06 — see migration note below.)_
- **Outputs**: A layered understanding of Koan’s capabilities, pointing to modules and helpers developers can adopt incrementally.
- **Failure modes**: Referencing modules without aligning with their guardrails (e.g., bypassing entity statics), misordered adoption (e.g., using a provider without the underlying abstractions), or missing supporting infrastructure (container engine, telemetry endpoint).
- **Success criteria**: New contributors can map Koan’s surface area, know which package unlocks what behavior, and understand how layers enhance one another.

## Edge cases to watch

- **Adapters without options**: Data or messaging providers assume options are bound; missing configuration is caught at startup when `ValidateOnStart` is enabled.
- **Inline endpoints**: Adding `Koan.Web` but bypassing controller-first patterns will break engineering guardrails and fail docs/tests.
- **Secrets (now agyo-tools)**: Pulling in `Sylin.Agyo.Secrets.*` without a backing provider keeps references unresolved; configure a provider (e.g. Vault) or local configuration. _(These packages live in the `agyo-tools` sibling repo as of 2026-06.)_
- **Orchestration without engine**: `Koan.Orchestration.Cli` detects Docker/Podman; ensure at least one engine is available or run in export-only mode.
- **Large result sets**: Use `Entity.AllStream` only when the selected adapter advertises
  `ProviderBoundedPaging`; otherwise choose explicit `Entity.Page`/materialization or another adapter.

---

## Orientation: reference = intent

Koan embraces a simple contract: referencing a package expresses intent, and `AddKoan()` activates its generated `KoanModule` descriptor automatically. You compose an application by adding packages:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan(); // discovers everything you referenced
var app = builder.Build();
app.UseKoan();
app.Run();
```

Below is a capability map that layers packages from foundational primitives through higher-level experiences.

### Layered overview

| Layer                   | Purpose                                                                 | Key packages                                                                                                                                                                 | Builds atop             |
| ----------------------- | ----------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------- |
| Core runtime            | Configuration, environment, boot diagnostics, adapter auto-registration | `Koan.Core`, `Koan.Core.Adapters`, `Koan.Diagnostics.Tool`                                                                                                                   | –                       |
| Data & storage          | Entity-first persistence, provider adapters, backups, object storage    | `Koan.Data.Core`, `Koan.Data.Abstractions`, `Koan.Data.*` providers, `Koan.Storage`, `Koan.Data.Backup`                                                                      | Core runtime            |
| Web & surface area      | MVC controllers, response transformers, auth, diagnostics               | `Koan.Web`, `Koan.Web.Extensions`, `Koan.Web.Transformers`, `Koan.Web.Auth.*`, `Koan.Web.Connector.Swagger`, `Koan.Web.Backup`                                     | Core runtime + Data     |
| Messaging & async       | Inbox/outbox patterns, background coordination                          | `Koan.Messaging.Core`, `Koan.Messaging.Abstractions`, `Koan.Messaging.Connector.RabbitMq`, `Koan.Messaging.Inbox.*` _(scheduling moved to agyo-tools as `Sylin.Agyo.Scheduling`, 2026-06)_ | Core + Data             |
| AI, media & search      | Prompt routing, embeddings, media pipelines                             | `Koan.AI`, `Koan.AI.Contracts`, `Koan.AI.Connector.Ollama`, `Koan.Media.Abstractions`, `Koan.Media.Core`, `Koan.Media.Web`, `Koan.Data.Vector.*`                              | Core + Data             |
| Orchestration           | Container orchestration, manifest generation                            | `Koan.Orchestration.*`, `Koan.Orchestration.Cli` _(secrets, recipes/observability moved to agyo-tools as `Sylin.Agyo.*`, 2026-06; `Koan.Recipe.Abstractions` deleted)_       | Core + Data             |
| Domain pipelines        | Canonical data flows, Dapr runtime, MCP integration                     | `Koan.Canon.*`, `Koan.Mcp`                                                                                                                                                   | Core + Data + Messaging |

The remainder of this document drills into each layer and highlights first-class helpers and developer outcomes.

---

## 1. Core runtime & bootstrapping

**Packages**: `Koan.Core`, `Koan.Core.Adapters`, `Koan.Diagnostics.Tool`

- `Koan.Core` exposes the primitives every module depends on: `KoanEnv` for environment snapshots, configuration helpers (`Configuration.Read`), bootstrapping utilities, and common options/validation patterns.
- `Koan.Core.Adapters` centralises adapter discovery. When you reference a provider (`Koan.Data.Connector.Postgres`, `Koan.Messaging.Connector.RabbitMq`, etc.), its registrar is loaded automatically and logged in the boot report.
- `Koan.Diagnostics.Tool` surfaces diagnostics helpers (boot reports, capability snapshots) that feed the docs build and CLI tooling.

**Developer benefit**: you write one line—`AddKoan()`—and the runtime stitches together every referenced capability with sane defaults and explicit logging.

---

## 2. Data & storage plane

**Core abstractions**: `Koan.Data.Abstractions` provides low-level contracts; `Koan.Data.Core` delivers the entity-first surface (`Entity<T>.All`, `.Query`, `.FirstPage`, `.AllStream`) plus diagnostics for paging and streaming.

**Providers**:

- Relational: `Koan.Data.Connector.Postgres`, `Koan.Data.Connector.SqlServer`, `Koan.Data.Connector.Sqlite`, `Koan.Data.Relational` (shared tooling)
- Document & key/value: `Koan.Data.Connector.Mongo`, `Koan.Data.Connector.Redis`, `Koan.Data.Connector.Couchbase`, `Koan.Data.Connector.Json`
- Search & vector: `Koan.Data.Vector`, `Koan.Data.Vector.Abstractions`, `Koan.Data.Vector.Connector.Weaviate`, `Koan.Data.Vector.Connector.Milvus`, `Koan.Data.Connector.OpenSearch`, `Koan.Data.Connector.ElasticSearch`
- Direct and composition helpers: `IDataService.Direct(...)` in `Koan.Data.Core` for low-level commands (folded in from the former `Koan.Data.Direct` package 2026-06, ARCH-0090 §1). _(The former `Koan.Data.Cqrs` / `Koan.Data.Cqrs.Outbox.Connector.Mongo` outbox helpers were removed 2026-06 — superseded by the Jobs ledger outbox, JOBS-0005.)_

**Adjacencies**:

- `Koan.Data.Backup` enables typed backup/restore workflows that work across providers.
- `Koan.Storage` (with `Koan.Storage.Connector.Local`) handles object storage with model-centric helpers.

**Developer benefit**: Define entities like `public sealed class Todo : Entity<Todo> { ... }`; the framework generates IDs, exposes CRUD statics, and streams data with provider-specific optimisations. Referencing a provider package immediately lights up the matching backend with discovery and health checks.

---

## 3. Web surfaces & APIs

**Packages**: `Koan.Web`, `Koan.Web.Extensions`, `Koan.Web.Transformers`, `Koan.Web.Connector.Swagger`, `Koan.Web.Backup`, `Koan.Web.Diagnostics`

- `Koan.Web` enforces controller-first APIs using `EntityController<T>` and configures health/OpenAPI endpoints.
- `Koan.Web.Extensions` offers middleware shortcuts (error handling, JSON options), while `Koan.Web.Transformers` shapes responses to match API contracts.
- `Koan.Web.Connector.Swagger` integrates OpenAPI generation with the controller ecosystem. _(`Koan.Web.Connector.GraphQl` migrated to agyo-tools 2026-06 as `Sylin.Agyo.Web.GraphQl` — the HotChocolate CVE-treadmill belongs on agyo's cadence. See [`docs/assessment/08-agyo-reorganization.md`](../assessment/08-agyo-reorganization.md).)_
- `Koan.Web.Backup` surfaces backup/restore operations via HTTP for high-trust maintenance scenarios.
- `Koan.Web.Diagnostics` plugs diagnostic dashboards into the pipeline when needed.

**Authentication**: `Koan.Web.Auth` provides core auth services, with specialized packages (`.Services`, `.Roles`, `.Discord`, `.Google`, `.Microsoft`, `.Oidc`, `.TestProvider`) delivering integration-ready providers.

**Developer benefit**: Build APIs by deriving from controllers, rely on transformers for payload consistency, and bolt on auth providers by referencing the right package—no manual wiring.

---

## 4. Messaging and async

**Packages**: `Koan.Messaging.Abstractions`, `Koan.Messaging.Core`, `Koan.Messaging.Connector.RabbitMq`, `Koan.Messaging.Inbox.Connector.Http`, `Koan.Messaging.Inbox.Connector.InMemory`

- `Koan.Messaging.Core` implements bus orchestration, retry policies, and diagnostics for message pipelines. Abstractions keeps contracts separate for consumers.
- Provider packages like `Koan.Messaging.Connector.RabbitMq` plug into `Koan.Core.Adapters` to auto-register connections and health checks.
- Inbox helpers (`Koan.Messaging.Inbox.*`) give you exactly-once semantics and idempotent reprocessing surfaces. _(The `Koan.Service.Inbox.Connector.Redis` microservice was removed 2026-06 — its client API (`HttpInboxStore`) no longer existed in src and its only consumer was the archived S15 sample; MESS-0025/MESS-0026 are Retired.)_
- _(Cron/interval scheduling — formerly `Koan.Scheduling` — **migrated to agyo-tools 2026-06** as `Sylin.Agyo.Scheduling`; it was an opt-in in-proc helper, not core. For durable recurring work use the Jobs ledger (`[JobAction(Schedule = ...)]`, JOBS-0005). See [`docs/assessment/08-agyo-reorganization.md`](../assessment/08-agyo-reorganization.md).)_
- ~~CQRS support (`Koan.Data.Cqrs.*`) ties messaging back into the data plane with projection tasks and outbox helpers.~~ _(Removed 2026-06 — superseded by the Jobs ledger outbox, JOBS-0005.)_

**Developer benefit**: Compose event-driven flows while reusing entity statics, add reliable inbox/outbox boundaries without custom plumbing, and co-ordinate background processing with predictable options and diagnostics.

---

## 5. AI, media, and rich content

**Packages**: `Koan.AI`, `Koan.AI.Contracts`, `Koan.AI.Connector.Ollama`, `Koan.AI.Web`, `Koan.Media.Abstractions`, `Koan.Media.Core`, `Koan.Media.Web`

- `Koan.AI` routes prompts and embeddings across registered adapters. Contracts define provider capabilities (completion, embedding, moderation).
- `Koan.AI.Connector.Ollama` adds a local-first large language model provider with discovery, health probes, and profile-aware configuration.
- `Koan.AI.Web` offers controller helpers for AI endpoints (chat, embeddings) following Koan web guardrails.
- The media stack (`Koan.Media.*`) handles ingestion, transformation, and delivery of media assets, with optional web surfaces for upload and streaming.
- Vector database packages (under data) pair naturally with AI modules to deliver hybrid search.

**Developer benefit**: Add intelligent features by referencing an adapter; the runtime provides typed clients, request routers, and optional HTTP endpoints without bespoke glue.

---

## 6. Secrets, configuration, and security posture

> **Migrated to agyo-tools 2026-06.** The secrets stack — formerly `Koan.Secrets.Abstractions` / `Koan.Secrets.Core` / `Koan.Secrets.Connector.Vault` — now ships from the `agyo-tools` sibling repo as `Sylin.Agyo.Secrets.*`. It was a consumed-but-peripheral helper, not core (STACK-0001 layering). Koan keeps only a fail-soft reflection probe (`TryInvokeSecretsBootstrap` in `Koan.Data.Core`) that resolves when the assembly is present downstream and no-ops when absent. See [`docs/assessment/08-agyo-reorganization.md`](../assessment/08-agyo-reorganization.md).

**Packages** (now `Sylin.Agyo.Secrets.*` in agyo-tools): `Secrets.Abstractions`, `Secrets.Core`, `Secrets.Connector.Vault`

- Core library augments configuration with secret references (`secret://scope/name`) resolved lazily, ensuring rotation-friendly behavior.
- Provider packages (Vault today) extend the resolver chain; additional providers can be dropped in later with zero code changes.
- Works hand-in-hand with orchestrators to propagate secret references into generated manifests or container exports.

**Developer benefit**: One abstraction for secrets across dev/prod, consistent logging/redaction, and declarative references inside standard configuration files.

---

## 7. Orchestration and operational layers

> **Recipes left Koan 2026-06.** `Koan.Recipe.Abstractions` was **deleted** — superseded by the ARCH-0086 `KoanModule` primitive (its AppDomain-scan bootstrap idiom is no longer needed). The observability bundle — formerly `Koan.Recipe.Observability` — **migrated to agyo-tools** as `Sylin.Agyo.Observability`, re-homed as a `KoanModule` rather than an `IKoanRecipe`. The orchestration toolchain below stays in Koan core. See [`docs/assessment/08-agyo-reorganization.md`](../assessment/08-agyo-reorganization.md).

**Packages**: `Koan.Orchestration.Abstractions`, `Koan.Orchestration.Cli`, `Koan.Orchestration.Generators`, `Koan.Orchestration.Connector.Docker`, `Koan.Orchestration.Connector.Podman`, `Koan.Orchestration.Renderers.Connector.Compose`, `Koan.Orchestration.Aspire`

- Operational bundles (health checks, resilient HTTP, optional OpenTelemetry) are now an opt-in `KoanModule` shipped from agyo-tools (`Sylin.Agyo.Observability`).
- The orchestration toolchain reads manifests generated at build time (`Koan.Orchestration.Generators`) to plan container stacks, export Compose manifests, and run local clusters.
- Providers (`Docker`, `Podman`) and renderers (`Compose`) are modular; the CLI selects what is available, and `Koan.Orchestration.Aspire` integrates with .NET Aspire when required.

**Developer benefit**: Move from source to runnable local/CI stack with a single CLI command, export artifacts for ops teams, and (via the agyo-tools observability module) apply best-practice wiring (telemetry, retries) consistently.

---

## 8. Canonical entities

**Packages**: `Koan.Canon.Contracts`, `Koan.Canon`, `Koan.Canon.Web`

- `Koan.Canon.Contracts` is inert vocabulary for models, metadata, contributors, persistence, and audit.
- `Koan.Canon` activates the runtime through `AddKoan()`, discovers contributors, and compiles one
  deterministic pipeline per canonical Entity and host.
- `Koan.Canon.Web` adds generated HTTP and inspection surfaces for discovered models.

**Developer benefit**: Define canonical identity and business rules; Koan owns composition, convergence,
persistence, and optional Web projection without controllers or registrars.

---

## 9. How the capabilities work together

1. **Start with Core + Web + Data**: Most services begin by referencing `Koan.Core`, `Koan.Data.Core`, `Koan.Web`, and a storage provider (e.g., `Koan.Data.Connector.Postgres`). This combination yields CRUD-ready APIs with no custom scaffolding.
2. **Add async & background work** when you need eventual consistency. `Koan.Messaging.*` and the Jobs ledger (JOBS-0005) reuse the data abstractions for persistence and the core runtime for configuration and logging. _(For lightweight in-proc cron/interval scheduling, reach for `Sylin.Agyo.Scheduling` from agyo-tools.)_
3. **Introduce AI or media** to augment your domain. Use the same entity-first approach to persist metadata while AI adapters handle external intelligence.
4. **Secure & operationalise** by layering orchestration (`Koan.Orchestration.*`), plus the agyo-tools helpers where needed — secrets (`Sylin.Agyo.Secrets.*`) and the observability module (`Sylin.Agyo.Observability`). These read the same configuration sources and emit manifest metadata consumed by the CLI.
5. **Add Canon** when imperfect or duplicate arrivals must converge into trusted Entities through explicit
   validation, aggregation, policy, projection, and distribution phases.

The map is intentionally composable: everything hinges on the core runtime’s auto-registration, and every module exposes typed options and constants to align with Koan’s engineering guardrails.

---

## Suggested adoption path for new teams

1. **Bootstrap**: `Koan.Core`, `Koan.Web`, `Koan.Data.Core`, plus one data provider (`Koan.Data.Connector.Postgres` or `Koan.Data.Connector.Mongo`). Use entity statics and controllers to ship value quickly.
2. **Observability & resilience**: Reference `Sylin.Agyo.Observability` (from agyo-tools) and run the CLI’s `doctor` command to confirm telemetry endpoints, or export Compose manifests for local stacks.
3. **Security posture**: Add `Sylin.Agyo.Secrets.Core` (and a provider like Vault) from agyo-tools before production, ensuring configuration remains declarative.
4. **Extended capabilities**: Pull in `Koan.Messaging.*`, the Jobs ledger (`Koan.Jobs`), and `Koan.Storage` as your domain evolves (and `Sylin.Agyo.Scheduling` for in-proc schedules). Each package maintains a README/TECHNICAL pair for deep dives.
5. **Advanced scenarios**: When AI, media, or canonical pipelines become relevant, reference the specialised packages—the runtime will announce new capabilities in the boot report.

---

## References

- Engineering guardrails: [`docs/engineering/index.md`](../engineering/index.md)
- Data access decisions: [`docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`](../decisions/DATA-0061-data-access-pagination-and-streaming.md)
- Web controller posture: [`docs/decisions/WEB-0035-entitycontroller-transformers.md`](../decisions/WEB-0035-entitycontroller-transformers.md)
- Module inventory: [`docs/architecture/module-ledger.md`](module-ledger.md)
- Packaging policy: [`docs/engineering/packaging.md`](../engineering/packaging.md)
- Orchestration roadmap: [`docs/decisions/ARCH-0047-orchestration-hosting-and-exporters-as-pluggable-adapters.md`](../decisions/ARCH-0047-orchestration-hosting-and-exporters-as-pluggable-adapters.md)

