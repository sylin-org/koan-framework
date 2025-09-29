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

### Koan.AI
- Depends on: Koan.AI.Contracts, Koan.Core
- Depended by: Koan.AI.Web
- Documentation: README ✅ · TECHNICAL ✅

### Koan.AI.Contracts
- Depends on: Koan.Core
- Depended by: Koan.AI, Koan.Ai.Provider.Ollama, Koan.AI.Web
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Ai.Provider.Ollama
- Depends on: Koan.AI.Contracts, Koan.Core, Koan.Core.Adapters, Koan.Orchestration.Abstractions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.AI.Web
- Depends on: Koan.AI, Koan.AI.Contracts
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Canon.Core
- Depends on: Koan.Core, Koan.Data.Core, Koan.Messaging.Core
- Depended by: Koan.Canon.Runtime.Dapr, Koan.Canon.Web
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Canon.Runtime.Dapr
- Depends on: Koan.Canon.Core
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Redis
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Orchestration.Abstractions, Koan.Orchestration.Aspire
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Relational
- Depends on: Koan.Data.Abstractions, Koan.Data.Core
- Depended by: Koan.Data.Postgres, Koan.Data.Sqlite, Koan.Data.SqlServer
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Sqlite
- Depends on: Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Relational
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.SqlServer
- Depends on: Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Relational, Koan.Orchestration.Abstractions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Vector
- Depends on: Koan.Data.Core, Koan.Data.Vector.Abstractions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Vector.Abstractions
- Depends on: Koan.Data.Abstractions
- Depended by: Koan.Data.Core, Koan.Data.ElasticSearch, Koan.Data.Milvus, Koan.Data.OpenSearch, Koan.Data.Vector, Koan.Data.Weaviate
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Weaviate
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
- Depended by: Koan.Canon.Core, Koan.Messaging.RabbitMq
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Messaging.RabbitMq
- Depends on: Koan.Core, Koan.Messaging.Core
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Abstractions
- Depends on: –
- Depended by: Koan.Ai.Provider.Ollama, Koan.Core, Koan.Core.Adapters, Koan.Data.Couchbase, Koan.Data.ElasticSearch, Koan.Data.Milvus, Koan.Data.Mongo, Koan.Data.OpenSearch, Koan.Data.Postgres, Koan.Data.Redis, Koan.Data.SqlServer, Koan.Data.Weaviate, Koan.Orchestration.Cli, Koan.Orchestration.Provider.Docker, Koan.Orchestration.Provider.Podman, Koan.Orchestration.Renderers.Compose, Koan.Secrets.Vault
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Aspire
- Depends on: Koan.Core
- Depended by: Koan.Data.Postgres, Koan.Data.Redis
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Cli
- Depends on: Koan.Orchestration.Abstractions, Koan.Orchestration.Provider.Docker, Koan.Orchestration.Provider.Podman, Koan.Orchestration.Renderers.Compose
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Generators
- Depends on: –
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Provider.Docker
- Depends on: Koan.Orchestration.Abstractions
- Depended by: Koan.Orchestration.Cli
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Provider.Podman
- Depends on: Koan.Orchestration.Abstractions
- Depended by: Koan.Orchestration.Cli
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Renderers.Compose
- Depends on: Koan.Orchestration.Abstractions
- Depended by: Koan.Orchestration.Cli
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Recipe.Abstractions
- Depends on: Koan.Core
- Depended by: Koan.Recipe.Observability
- Documentation: README ✅ · TECHNICAL ❌

### Koan.Recipe.Observability
- Depends on: Koan.Recipe.Abstractions, Koan.Web
- Depended by: –
- Documentation: README ✅ · TECHNICAL ❌

### Koan.Scheduling
- Depends on: Koan.Core
- Depended by: Koan.Web
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Secrets.Abstractions
- Depends on: –
- Depended by: Koan.Secrets.Core, Koan.Secrets.Vault
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Secrets.Core
- Depends on: Koan.Core, Koan.Secrets.Abstractions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Secrets.Vault
- Depends on: Koan.Core, Koan.Orchestration.Abstractions, Koan.Secrets.Abstractions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Service.Inbox.Redis
- Depends on: –
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Storage
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core
- Depended by: Koan.Data.Backup, Koan.Media.Abstractions, Koan.Media.Core, Koan.Media.Web, Koan.Storage.Local
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Storage.Local
- Depends on: Koan.Storage
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Scheduling
- Depended by: Koan.Canon.Web, Koan.Mcp, Koan.Media.Core, Koan.Recipe.Observability, Koan.Web.Backup, Koan.Web.Extensions, Koan.Web.GraphQl, Koan.Web.Swagger, Koan.Web.Transformers
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth
- Depends on: Koan.Core
- Depended by: Koan.Web.Auth.Discord, Koan.Web.Auth.Google, Koan.Web.Auth.Microsoft, Koan.Web.Auth.Oidc, Koan.Web.Auth.Services, Koan.Web.Auth.TestProvider
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Discord
- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Google
- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Microsoft
- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Oidc
- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Roles
- Depends on: Koan.Core, Koan.Data.Core, Koan.Web.Extensions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Services
- Depends on: Koan.Core, Koan.Web.Auth, Koan.Web.Auth.TestProvider
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.TestProvider
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

### Koan.Web.GraphQl
- Depends on: Koan.Core, Koan.Data.Core, Koan.Web
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Swagger
- Depends on: Koan.Web
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Transformers
- Depends on: Koan.Core, Koan.Web
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core
- Depended by: Koan.Data.Backup, Koan.Media.Abstractions, Koan.Media.Core, Koan.Media.Web, Koan.Storage.Local
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Storage.Local
- Depends on: Koan.Storage
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Scheduling
- Depended by: Koan.Canon.Web, Koan.Mcp, Koan.Media.Core, Koan.Recipe.Observability, Koan.Web.Backup, Koan.Web.Extensions, Koan.Web.GraphQl, Koan.Web.Swagger, Koan.Web.Transformers
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth
- Depends on: Koan.Core
- Depended by: Koan.Web.Auth.Discord, Koan.Web.Auth.Google, Koan.Web.Auth.Microsoft, Koan.Web.Auth.Oidc, Koan.Web.Auth.Services, Koan.Web.Auth.TestProvider
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Discord
- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Google
- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Microsoft
- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Oidc
- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Roles
- Depends on: Koan.Core, Koan.Data.Core, Koan.Web.Extensions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Services
- Depends on: Koan.Core, Koan.Web.Auth, Koan.Web.Auth.TestProvider
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.TestProvider
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

### Koan.Web.GraphQl
- Depends on: Koan.Core, Koan.Data.Core, Koan.Web
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Swagger
- Depends on: Koan.Web
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Transformers
- Depends on: Koan.Core, Koan.Web
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅
