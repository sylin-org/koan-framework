---
type: ARCHITECTURE
domain: framework
title: "Koan Capability Map"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-29
framework_version: v0.6.3
validation:
  date_last_tested: 2025-09-29
  status: verified
  scope: docs/architecture/capability-map.md
---

# Koan Capability Map

## Contract
- **Scope**: Provide a guided tour of first-class Koan modules under `src/`, how they compose, and when to reach for each package.
- **Inputs**: `AddKoan()` bootstrap pipeline, auto-registrars, entity-first models, controller-first web stack, orchestration CLI, data/AI/messaging adapters, recipes, and secrets modules.
- **Outputs**: A layered understanding of Koan’s capabilities, pointing to modules and helpers developers can adopt incrementally.
- **Failure modes**: Referencing modules without aligning with their guardrails (e.g., bypassing entity statics), misordered adoption (e.g., using a provider without the underlying abstractions), or missing supporting infrastructure (container engine, telemetry endpoint).
- **Success criteria**: New contributors can map Koan’s surface area, know which package unlocks what behavior, and understand how layers enhance one another.

## Edge cases to watch
- **Adapters without options**: Data or messaging providers assume options are bound; missing configuration is caught at startup when `ValidateOnStart` is enabled.
- **Inline endpoints**: Adding `Koan.Web` but bypassing controller-first patterns will break engineering guardrails and fail docs/tests.
- **Secrets & recipes**: Pulling in `Koan.Secrets.*` without a backing provider keeps references unresolved; enable the appropriate recipe or local configuration.
- **Orchestration without engine**: `Koan.Orchestration.Cli` detects Docker/Podman; ensure at least one engine is available or run in export-only mode.
- **Large result sets**: Always couple data providers with paging or streaming helpers (`Entity.Page`, `Entity.AllStream`) to avoid memory pressure.

---

## Orientation: reference = intent

Koan embraces a simple contract: referencing a package expresses intent, and `AddKoan()` activates the capability automatically. The runtime scans for `IKoanAutoRegistrar` implementations, so you compose your application by adding packages:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan(); // discovers everything you referenced
var app = builder.Build();
app.UseKoan();
app.Run();
```

Below is a capability map that layers packages from foundational primitives through higher-level experiences.

### Layered overview

| Layer | Purpose | Key packages | Builds atop |
| --- | --- | --- | --- |
| Core runtime | Configuration, environment, boot diagnostics, adapter auto-registration | `Koan.Core`, `Koan.Core.Adapters`, `Koan.Diagnostics.Tool` | – |
| Data & storage | Entity-first persistence, provider adapters, backups, object storage | `Koan.Data.Core`, `Koan.Data.Abstractions`, `Koan.Data.*` providers, `Koan.Storage`, `Koan.Data.Backup` | Core runtime |
| Web & surface area | MVC controllers, response transformers, auth, diagnostics | `Koan.Web`, `Koan.Web.Extensions`, `Koan.Web.Transformers`, `Koan.Web.Auth.*`, `Koan.Web.Swagger`, `Koan.Web.GraphQl`, `Koan.Web.Backup` | Core runtime + Data |
| Messaging & async | Inbox/outbox patterns, background coordination, scheduling | `Koan.Messaging.Core`, `Koan.Messaging.Abstractions`, `Koan.Messaging.RabbitMq`, `Koan.Service.Inbox.Redis`, `Koan.Messaging.Inbox.*`, `Koan.Data.Cqrs.*`, `Koan.Scheduling` | Core + Data |
| AI, media & search | Prompt routing, embeddings, media pipelines | `Koan.AI`, `Koan.AI.Contracts`, `Koan.Ai.Provider.Ollama`, `Koan.Media.Abstractions`, `Koan.Media.Core`, `Koan.Media.Web`, `Koan.Data.Vector.*` | Core + Data |
| Secrets & configuration | Unified secret resolution and config overlays | `Koan.Secrets.Abstractions`, `Koan.Secrets.Core`, `Koan.Secrets.Vault` | Core runtime |
| Recipes & orchestration | Intent-driven operational wiring, container orchestration | `Koan.Recipe.Abstractions`, `Koan.Recipe.Observability`, `Koan.Orchestration.*`, `Koan.Orchestration.Cli` | Core + Secrets + Data |
| Domain pipelines | Canonical data flows, Dapr runtime, MCP integration | `Koan.Canon.*`, `Koan.Mcp` | Core + Data + Messaging |

The remainder of this document drills into each layer and highlights first-class helpers and developer outcomes.

---

## 1. Core runtime & bootstrapping

**Packages**: `Koan.Core`, `Koan.Core.Adapters`, `Koan.Diagnostics.Tool`

- `Koan.Core` exposes the primitives every module depends on: `KoanEnv` for environment snapshots, configuration helpers (`Configuration.Read`), bootstrapping utilities, and common options/validation patterns.
- `Koan.Core.Adapters` centralises adapter discovery. When you reference a provider (`Koan.Data.Postgres`, `Koan.Messaging.RabbitMq`, etc.), its registrar is loaded automatically and logged in the boot report.
- `Koan.Diagnostics.Tool` surfaces diagnostics helpers (boot reports, capability snapshots) that feed the docs build and CLI tooling.

**Developer benefit**: you write one line—`AddKoan()`—and the runtime stitches together every referenced capability with sane defaults and explicit logging.

---

## 2. Data & storage plane

**Core abstractions**: `Koan.Data.Abstractions` provides low-level contracts; `Koan.Data.Core` delivers the entity-first surface (`Entity<T>.All`, `.Query`, `.FirstPage`, `.AllStream`) plus diagnostics for paging and streaming.

**Providers**:
- Relational: `Koan.Data.Postgres`, `Koan.Data.SqlServer`, `Koan.Data.Sqlite`, `Koan.Data.Relational` (shared tooling)
- Document & key/value: `Koan.Data.Mongo`, `Koan.Data.Redis`, `Koan.Data.Couchbase`, `Koan.Data.Json`
- Search & vector: `Koan.Data.Vector`, `Koan.Data.Vector.Abstractions`, `Koan.Data.Weaviate`, `Koan.Data.Milvus`, `Koan.Data.OpenSearch`, `Koan.Data.ElasticSearch`
- Direct and composition helpers: `Koan.Data.Direct` for low-level commands, `Koan.Data.Cqrs`/`Koan.Data.Cqrs.Outbox.Mongo` for projections and outbox patterns.

**Adjacencies**:
- `Koan.Data.Backup` enables typed backup/restore workflows that work across providers.
- `Koan.Storage` (with `Koan.Storage.Local`) handles object storage with model-centric helpers.

**Developer benefit**: Define entities like `public sealed class Todo : Entity<Todo> { ... }`; the framework generates IDs, exposes CRUD statics, and streams data with provider-specific optimisations. Referencing a provider package immediately lights up the matching backend with discovery and health checks.

---

## 3. Web surfaces & APIs

**Packages**: `Koan.Web`, `Koan.Web.Extensions`, `Koan.Web.Transformers`, `Koan.Web.Swagger`, `Koan.Web.GraphQl`, `Koan.Web.Backup`, `Koan.Web.Diagnostics`

- `Koan.Web` enforces controller-first APIs using `EntityController<T>` and configures health/OpenAPI endpoints.
- `Koan.Web.Extensions` offers middleware shortcuts (error handling, JSON options), while `Koan.Web.Transformers` shapes responses to match API contracts.
- `Koan.Web.Swagger` integrates OpenAPI generation with the controller ecosystem; `Koan.Web.GraphQl` activates GraphQL endpoints backed by entities.
- `Koan.Web.Backup` surfaces backup/restore operations via HTTP for high-trust maintenance scenarios.
- `Koan.Web.Diagnostics` plugs diagnostic dashboards into the pipeline when needed.

**Authentication**: `Koan.Web.Auth` provides core auth services, with specialized packages (`.Services`, `.Roles`, `.Discord`, `.Google`, `.Microsoft`, `.Oidc`, `.TestProvider`) delivering integration-ready providers.

**Developer benefit**: Build APIs by deriving from controllers, rely on transformers for payload consistency, and bolt on auth providers by referencing the right package—no manual wiring.

---

## 4. Messaging, async, and scheduling

**Packages**: `Koan.Messaging.Abstractions`, `Koan.Messaging.Core`, `Koan.Messaging.RabbitMq`, `Koan.Messaging.Inbox.Http`, `Koan.Messaging.Inbox.InMemory`, `Koan.Service.Inbox.Redis`, `Koan.Scheduling`, `Koan.Data.Cqrs`, `Koan.Data.Cqrs.Outbox.Mongo`

- `Koan.Messaging.Core` implements bus orchestration, retry policies, and diagnostics for message pipelines. Abstractions keeps contracts separate for consumers.
- Provider packages like `Koan.Messaging.RabbitMq` plug into `Koan.Core.Adapters` to auto-register connections and health checks.
- Inbox helpers (`Koan.Messaging.Inbox.*`, `Koan.Service.Inbox.Redis`) give you exactly-once semantics and idempotent reprocessing surfaces.
- `Koan.Scheduling` introduces cron/interval-based job orchestration anchored in Koan’s background worker infrastructure.
- CQRS support (`Koan.Data.Cqrs.*`) ties messaging back into the data plane with projection tasks and outbox helpers.

**Developer benefit**: Compose event-driven flows while reusing entity statics, add reliable inbox/outbox boundaries without custom plumbing, and co-ordinate background processing with predictable options and diagnostics.

---

## 5. AI, media, and rich content

**Packages**: `Koan.AI`, `Koan.AI.Contracts`, `Koan.Ai.Provider.Ollama`, `Koan.AI.Web`, `Koan.Media.Abstractions`, `Koan.Media.Core`, `Koan.Media.Web`

- `Koan.AI` routes prompts and embeddings across registered adapters. Contracts define provider capabilities (completion, embedding, moderation).
- `Koan.Ai.Provider.Ollama` adds a local-first large language model provider with discovery, health probes, and profile-aware configuration.
- `Koan.AI.Web` offers controller helpers for AI endpoints (chat, embeddings) following Koan web guardrails.
- The media stack (`Koan.Media.*`) handles ingestion, transformation, and delivery of media assets, with optional web surfaces for upload and streaming.
- Vector database packages (under data) pair naturally with AI modules to deliver hybrid search.

**Developer benefit**: Add intelligent features by referencing an adapter; the runtime provides typed clients, request routers, and optional HTTP endpoints without bespoke glue.

---

## 6. Secrets, configuration, and security posture

**Packages**: `Koan.Secrets.Abstractions`, `Koan.Secrets.Core`, `Koan.Secrets.Vault`

- Core library augments configuration with secret references (`secret://scope/name`) resolved lazily, ensuring rotation-friendly behavior.
- Provider packages (Vault today) extend the resolver chain; additional providers can be dropped in later with zero code changes.
- Works hand-in-hand with recipes and orchestrators to propagate secret references into generated manifests or container exports.

**Developer benefit**: One abstraction for secrets across dev/prod, consistent logging/redaction, and declarative references inside standard configuration files.

---

## 7. Recipes, orchestration, and operational layers

**Packages**: `Koan.Recipe.Abstractions`, `Koan.Recipe.Observability`, `Koan.Orchestration.Abstractions`, `Koan.Orchestration.Cli`, `Koan.Orchestration.Generators`, `Koan.Orchestration.Provider.Docker`, `Koan.Orchestration.Provider.Podman`, `Koan.Orchestration.Renderers.Compose`, `Koan.Orchestration.Aspire`

- Recipes encode opt-in operational bundles. `Koan.Recipe.Observability`, for example, wires OpenTelemetry, health checks, and resilient HTTP policies in one call.
- The orchestration toolchain reads manifests generated at build time (`Koan.Orchestration.Generators`) to plan container stacks, export Compose manifests, and run local clusters.
- Providers (`Docker`, `Podman`) and renderers (`Compose`) are modular; the CLI selects what is available, and `Koan.Orchestration.Aspire` integrates with .NET Aspire when required.

**Developer benefit**: Move from source to runnable local/CI stack with a single CLI command, export artifacts for ops teams, and apply best-practice wiring (telemetry, retries) consistently via recipes.

---

## 8. Domain pipelines & canonical data flows

**Packages**: `Koan.Canon.Core`, `Koan.Canon.Web`, `Koan.Canon.Runtime.Dapr`, `Koan.Canon.RabbitMq`, `Koan.Mcp`

- Canon (Canonical data pipeline) modules standardize how domain models flow through stages, projections, and lineage tracking. The runtime builds on messaging, data, and storage primitives to deliver end-to-end, auditable pipelines.
- `Koan.Canon.Runtime.Dapr` lights up Dapr-based execution models, and `Koan.Canon.Web` exposes surface APIs for pipeline control.
- `Koan.Canon.RabbitMq` connects Canon flows to the messaging layer.
- `Koan.Mcp` provides Model Context Protocol integration if you need MCP-based agent connectivity.

**Developer benefit**: When your domain needs complex pipelines or projections, the Canon stack gives you a tested blueprint built on top of the core Koan patterns.

---

## 9. How the capabilities work together

1. **Start with Core + Web + Data**: Most services begin by referencing `Koan.Core`, `Koan.Data.Core`, `Koan.Web`, and a storage provider (e.g., `Koan.Data.Postgres`). This combination yields CRUD-ready APIs with no custom scaffolding.
2. **Add async & scheduling** when you need eventual consistency or background work. `Koan.Messaging.*` and `Koan.Scheduling` reuse the data abstractions for persistence and the core runtime for configuration and logging.
3. **Introduce AI or media** to augment your domain. Use the same entity-first approach to persist metadata while AI adapters handle external intelligence.
4. **Secure & operationalise** by layering secrets (`Koan.Secrets.*`), recipes (`Koan.Recipe.*`), and orchestration (`Koan.Orchestration.*`). These modules read the same configuration sources and emit manifest metadata consumed by the CLI.
5. **scale the domain** with Canon or Flow once you need canonical projections, multi-stage pipelines, or Dapr-hosted workers.

The map is intentionally composable: everything hinges on the core runtime’s auto-registration, and every module exposes typed options and constants to align with Koan’s engineering guardrails.

---

## Suggested adoption path for new teams

1. **Bootstrap**: `Koan.Core`, `Koan.Web`, `Koan.Data.Core`, plus one data provider (`Koan.Data.Postgres` or `Koan.Data.Mongo`). Use entity statics and controllers to ship value quickly.
2. **Observability & resilience**: Reference `Koan.Recipe.Observability` and run the CLI’s `doctor` command to confirm telemetry endpoints, or export Compose manifests for local stacks.
3. **Security posture**: Add `Koan.Secrets.Core` (and a provider like Vault) before production, ensuring configuration remains declarative.
4. **Extended capabilities**: Pull in `Koan.Messaging.*`, `Koan.Scheduling`, and `Koan.Storage` as your domain evolves. Each package maintains a README/TECHNICAL pair for deep dives.
5. **Advanced scenarios**: When AI, media, or canonical pipelines become relevant, reference the specialised packages—the runtime will announce new capabilities in the boot report.

---

## References
- Engineering guardrails: [`docs/engineering/index.md`](../engineering/index.md)
- Data access decisions: [`docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`](../decisions/DATA-0061-data-access-pagination-and-streaming.md)
- Web controller posture: [`docs/decisions/WEB-0035-entitycontroller-transformers.md`](../decisions/WEB-0035-entitycontroller-transformers.md)
- Module inventory: [`docs/architecture/module-ledger.md`](module-ledger.md)
- Packaging policy: [`docs/engineering/packaging.md`](../engineering/packaging.md)
- Orchestration roadmap: [`docs/decisions/ARCH-0047-orchestration-hosting-and-exporters-as-pluggable-adapters.md`](../decisions/ARCH-0047-orchestration-hosting-and-exporters-as-pluggable-adapters.md)
