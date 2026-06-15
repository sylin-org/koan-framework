---
type: ARCHITECTURE
domain: framework
title: "Koan Module Ledger"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-29
framework_version: v0.6.3
validation:
  date_last_tested: 2025-09-29
  status: verified
  scope: scripts/module-inventory.ps1 > artifacts/module-inventory.{json,md}
---

# Koan Module Ledger

## Contract

- **Scope**: Provide an authoritative ledger of every Koan framework module under `src/`, including dependency links and documentation coverage status.
- **Inputs**: `scripts/module-inventory.ps1`, all `.csproj` manifests, and the module README/TECHNICAL pairs located beside each project.
- **Outputs**: Structured dependency ledger, documentation gap backlog, and regeneration guidance for future audits.
- **Failure modes**: Manual edits to the ledger without rerunning the inventory script, newly added projects outside `src/`, or renamed modules that leave stale references.
- **Success criteria**: Each module appears exactly once with accurate dependency metadata, documentation coverage is called out explicitly, and maintainers can regenerate the ledger from code without guesswork.

## Edge cases to watch

- **Generated or external projects**: Only projects below `src/` are captured. Add additional roots to the script if adapters move elsewhere.
- **Cross-module renames**: Renaming a project requires rerunning the script so reverse dependencies stay aligned.
- **Non-standard docs**: Modules with consolidated documentation (e.g., a shared TECHNICAL guide) still appear as missing; curate exceptions directly in this document.
- **Large fan-out nodes**: Modules such as `Koan.Core` or `Koan.Data.Core` drive many dependents. Breaking changes must include coordinated release notes and doc updates.
- **Doc drift**: Missing TECHNICAL files are called out below. Treat the list as an active backlog and resolve before tagging a release.

## Snapshot summary

- 65 modules inventoried from `src/` (script run: 2025-09-29).
- 9 modules lack a `TECHNICAL.md`; all modules have a `README.md`.
- Top outbound dependency hubs: `Koan.Core` (41 dependents), `Koan.Data.Core` (25), `Koan.Data.Abstractions` (22), `Koan.Orchestration.Abstractions` (17), `Koan.Web` (9).
- 39 modules currently have no dependents; most are leaf adapters or providers that can adopt breaking changes with limited blast radius.
- Cross-reference: see [`Koan Capability Map`](capability-map.md) for layered adoption and pairing guidance.

> Data source: `artifacts/module-inventory.md` (generated 2025-09-29).
>
> **2026-06 agyo-tools migration:** Tagging, Secrets (Abstractions/Core/Connector.Vault), Scheduling, Recipe.Observability, Rag (Abstractions/impl), WebSockets, Web.Connector.GraphQl, Data.Connector.PGVector, and Service.Translation moved out of Koan into the `agyo-tools` sibling repo as `Sylin.Agyo.*` packages (they were never core: STACK-0001 layering — agyo depends on Koan, never the reverse). `Koan.Recipe.Abstractions` was **deleted** (superseded by the ARCH-0086 `KoanModule`), not migrated. Entries below that carry a `~~strikethrough~~` reflect this. The counts/hubs above predate the migration and will resettle on the next `scripts/module-inventory.ps1` run. See [`docs/assessment/08-agyo-reorganization.md`](../assessment/08-agyo-reorganization.md).

### Koan.AI

- Depends on: Koan.AI.Contracts, Koan.Core
- Depended by: Koan.AI.Web
- Documentation: README ✅ · TECHNICAL ✅

### Koan.AI.Contracts

- Depends on: Koan.Core
- Depended by: Koan.AI, Koan.AI.Connector.Ollama, Koan.AI.Web
- Documentation: README ✅ · TECHNICAL ✅

### Koan.AI.Connector.Ollama

- Depends on: Koan.AI.Contracts, Koan.Core, Koan.Core.Adapters, Koan.Orchestration.Abstractions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.AI.Web

- Depends on: Koan.AI, Koan.AI.Contracts
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Canon.Core

- Depends on: Koan.Core, Koan.Data.Core, Koan.Messaging.Core
- Depended by: Koan.Canon.Runtime.Connector.Dapr, Koan.Canon.Web
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Canon.Runtime.Connector.Dapr

- Depends on: Koan.Canon.Core
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Connector.Redis

- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Orchestration.Abstractions, Koan.Orchestration.Aspire
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Relational

- Depends on: Koan.Data.Abstractions, Koan.Data.Core
- Depended by: Koan.Data.Connector.Postgres, Koan.Data.Connector.Sqlite, Koan.Data.Connector.SqlServer
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Connector.Sqlite

- Depends on: Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Relational
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Connector.SqlServer

- Depends on: Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Relational, Koan.Orchestration.Abstractions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Vector

- Depends on: Koan.Data.Core, Koan.Data.Vector.Abstractions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Vector.Abstractions

- Depends on: Koan.Data.Abstractions
- Depended by: Koan.Data.Core, Koan.Data.Connector.ElasticSearch, Koan.Data.Vector.Connector.Milvus, Koan.Data.Connector.OpenSearch, Koan.Data.Vector, Koan.Data.Vector.Connector.Weaviate
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Vector.Connector.Weaviate

- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Vector.Abstractions, Koan.Orchestration.Abstractions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Mcp

- Depends on: Koan.Core, Koan.Data.Core, Koan.Web
- Depended by: –
- Documentation: README ✅ · TECHNICAL ❌

### Koan.Media.Abstractions

- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Storage
- Depended by: Koan.Media.Core, Koan.Media.Web
- Documentation: README ✅ · TECHNICAL ❌

### Koan.Media.Core

- Depends on: Koan.Data.Core, Koan.Media.Abstractions, Koan.Storage, Koan.Web
- Depended by: –
- Documentation: README ✅ · TECHNICAL ❌

### Koan.Media.Web

- Depends on: Koan.Media.Abstractions, Koan.Storage
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Messaging.Core

- Depends on: Koan.Core
- Depended by: Koan.Canon.Core, Koan.Messaging.Connector.RabbitMq
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Messaging.Connector.RabbitMq

- Depends on: Koan.Core, Koan.Messaging.Core
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Abstractions

- Depends on: –
- Depended by: Koan.AI.Connector.Ollama, Koan.Core, Koan.Core.Adapters, Koan.Data.Connector.Couchbase, Koan.Data.Connector.ElasticSearch, Koan.Data.Vector.Connector.Milvus, Koan.Data.Connector.Mongo, Koan.Data.Connector.OpenSearch, Koan.Data.Connector.Postgres, Koan.Data.Connector.Redis, Koan.Data.Connector.SqlServer, Koan.Data.Vector.Connector.Weaviate, Koan.Orchestration.Cli, Koan.Orchestration.Connector.Docker, Koan.Orchestration.Connector.Podman, Koan.Orchestration.Renderers.Connector.Compose <!-- Koan.Secrets.Connector.Vault migrated to agyo-tools 2026-06 -->
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Aspire

- Depends on: Koan.Core
- Depended by: Koan.Data.Connector.Postgres, Koan.Data.Connector.Redis
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Cli

- Depends on: Koan.Orchestration.Abstractions, Koan.Orchestration.Connector.Docker, Koan.Orchestration.Connector.Podman, Koan.Orchestration.Renderers.Connector.Compose
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Generators

- Depends on: –
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Connector.Docker

- Depends on: Koan.Orchestration.Abstractions
- Depended by: Koan.Orchestration.Cli
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Connector.Podman

- Depends on: Koan.Orchestration.Abstractions
- Depended by: Koan.Orchestration.Cli
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Renderers.Connector.Compose

- Depends on: Koan.Orchestration.Abstractions
- Depended by: Koan.Orchestration.Cli
- Documentation: README ✅ · TECHNICAL ✅

### ~~Koan.Recipe.Abstractions~~

> **Deleted 2026-06** — superseded by ARCH-0086 `KoanModule` (the AppDomain-scan bootstrap idiom it provided is no longer needed). Did **not** migrate. See [`docs/assessment/08-agyo-reorganization.md`](../assessment/08-agyo-reorganization.md).

- ~~Depends on: Koan.Core~~
- ~~Depended by: Koan.Recipe.Observability~~
- ~~Documentation: README ✅ · TECHNICAL ❌~~

### ~~Koan.Recipe.Observability~~

> **Migrated to agyo-tools 2026-06** as `Sylin.Agyo.Observability` (re-homed as a `KoanModule`, not an `IKoanRecipe`). See [`docs/assessment/08-agyo-reorganization.md`](../assessment/08-agyo-reorganization.md).

- ~~Depends on: Koan.Recipe.Abstractions, Koan.Web~~
- ~~Depended by: –~~
- ~~Documentation: README ✅ · TECHNICAL ❌~~

### ~~Koan.Scheduling~~

> **Migrated to agyo-tools 2026-06** as `Sylin.Agyo.Scheduling`. `Koan.Web` no longer hard-references it (the 1-second poll loop and `/.well-known/Koan/scheduling` endpoint left with it). See [`docs/assessment/08-agyo-reorganization.md`](../assessment/08-agyo-reorganization.md).

- ~~Depends on: Koan.Core~~
- ~~Depended by: Koan.Web~~
- ~~Documentation: README ✅ · TECHNICAL ✅~~

### ~~Koan.Secrets.Abstractions~~

> **Migrated to agyo-tools 2026-06** as `Sylin.Agyo.Secrets.Abstractions`. The fail-soft reflection probe (`TryInvokeSecretsBootstrap`) stays in `Koan.Data.Core` and no-ops when the assembly is absent. See [`docs/assessment/08-agyo-reorganization.md`](../assessment/08-agyo-reorganization.md).

- ~~Depends on: –~~
- ~~Depended by: Koan.Secrets.Core, Koan.Secrets.Connector.Vault~~
- ~~Documentation: README ✅ · TECHNICAL ✅~~

### ~~Koan.Secrets.Core~~

> **Migrated to agyo-tools 2026-06** as `Sylin.Agyo.Secrets.Core`. See [`docs/assessment/08-agyo-reorganization.md`](../assessment/08-agyo-reorganization.md).

- ~~Depends on: Koan.Core, Koan.Secrets.Abstractions~~
- ~~Depended by: –~~
- ~~Documentation: README ✅ · TECHNICAL ✅~~

### ~~Koan.Secrets.Connector.Vault~~

> **Migrated to agyo-tools 2026-06** as `Sylin.Agyo.Secrets.Connector.Vault`. See [`docs/assessment/08-agyo-reorganization.md`](../assessment/08-agyo-reorganization.md).

- ~~Depends on: Koan.Core, Koan.Orchestration.Abstractions, Koan.Secrets.Abstractions~~
- ~~Depended by: –~~
- ~~Documentation: README ✅ · TECHNICAL ✅~~

### Koan.Storage

- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core
- Depended by: Koan.Data.Backup, Koan.Media.Abstractions, Koan.Media.Core, Koan.Media.Web, Koan.Storage.Connector.Local
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Storage.Connector.Local

- Depends on: Koan.Storage
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web

- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core <!-- Koan.Scheduling migrated to agyo-tools 2026-06 -->
- Depended by: Koan.Canon.Web, Koan.Mcp, Koan.Media.Core, Koan.Web.Backup, Koan.Web.Extensions, Koan.Web.Connector.Swagger, Koan.Web.Transformers <!-- Koan.Web.Connector.GraphQl + Koan.Recipe.Observability migrated to agyo-tools 2026-06 -->
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth

- Depends on: Koan.Core
- Depended by: Koan.Web.Auth.Connector.Discord, Koan.Web.Auth.Connector.Google, Koan.Web.Auth.Connector.Microsoft, Koan.Web.Auth.Connector.Oidc, Koan.Web.Auth.Services, Koan.Web.Auth.Connector.Test
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Connector.Discord

- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Connector.Google

- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Connector.Microsoft

- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Connector.Oidc

- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Roles

- Depends on: Koan.Core, Koan.Data.Core, Koan.Web.Extensions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Services

- Depends on: Koan.Core, Koan.Web.Auth, Koan.Web.Auth.Connector.Test
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Connector.Test

- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: Koan.Web.Auth.Services
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Backup

- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Backup, Koan.Data.Core, Koan.Web
- Depended by: –
- Documentation: README ✅ · TECHNICAL ❌

### Koan.Web.Extensions

- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Web
- Depended by: Koan.Canon.Web, Koan.Web.Auth.Roles
- Documentation: README ✅ · TECHNICAL ❌

### ~~Koan.Web.Connector.GraphQl~~

> **Migrated to agyo-tools 2026-06** as `Sylin.Agyo.Web.GraphQl` — resurrected from git tag `attic/koan-web-graphql` (the HotChocolate CVE-treadmill belongs on agyo's cadence, not Koan's release train). The WEB-0068 hook pipeline it rides stays public, so no seam remains in Koan. See [`docs/assessment/08-agyo-reorganization.md`](../assessment/08-agyo-reorganization.md); original cut WEB-0041/WEB-0042.

- ~~Depends on: Koan.Core, Koan.Data.Core, Koan.Web~~
- ~~Depended by: –~~
- ~~Documentation: README ✅ · TECHNICAL ✅~~

### Koan.Web.Connector.Swagger

- Depends on: Koan.Web
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Transformers

- Depends on: Koan.Core, Koan.Web
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

