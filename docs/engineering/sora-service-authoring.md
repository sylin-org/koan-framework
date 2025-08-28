---
title: SoraService authoring — DX quick guide
description: Minimal, targeted steps to declare a service adapter with [SoraService] so DevHost can run it and exporters can generate artifacts
---

# SoraService authoring — DX quick guide

Contract (at a glance)

- Inputs: one class per service annotated with `[SoraService]` in a project that references `Sora.Orchestration.Abstractions`.
- Outputs: a unified orchestration manifest (`__SoraOrchestrationManifest.Json`) consumed by CLI/planners. Your service shows up in `sora inspect`, `sora up`, `sora export compose`.
- Error modes: invalid short code, missing container image when `DeploymentKind=Container`, duplicate ids → reported as build diagnostics.
- Success criteria: service appears with correct kind, image, tag, ports, env/volumes; `sora status` shows endpoints and health.

Prerequisites

- Reference `Sora.Orchestration.Abstractions` from your project.
- Choose a stable `shortCode` (unique within your app’s dependency set).

Authoring checklist

1. Create a small, dedicated type and apply `[SoraService]`.
2. Set `Kind`, `shortCode`, `name`, `ContainerImage`, `DefaultTag`, `DefaultPorts`.
3. Add `HealthEndpoint` for HTTP-based readiness (optional but recommended).
4. Declare `Volumes` for persistence, and `AppEnv` to surface configuration into your app.
5. Add `Provides`/`Consumes` and `Capabilities` as "key=value" entries to help planners.
6. Build and run `sora inspect` to validate.

Minimal example

```csharp
using Sora.Orchestration;
using Sora.Orchestration.Attributes;

[SoraService(
    kind: ServiceKind.Database,
    shortCode: "postgres",
    name: "PostgreSQL",
    ContainerImage = "postgres",
    DefaultTag = "16",
    DefaultPorts = new[] { 5432 },
    HealthEndpoint = null, // PostgreSQL has a TCP healthcheck; omit for HTTP-only
    Capabilities = new[] { "protocol=postgres" },
    Volumes = new[] { "./Data/postgres:/var/lib/postgresql/data" },
    AppEnv = new[] { "Sora__Data__Postgres__ConnectionString=postgres://{host}:{port}" },
    Scheme = "postgres", Host = "postgres", EndpointPort = 5432, UriPattern = "postgres://{host}:{port}",
    LocalScheme = "postgres", LocalHost = "localhost", LocalPort = 5432, LocalPattern = "postgres://{host}:{port}")]
internal sealed class PostgresService { }
```

Provider-specific example (Vault)

```csharp
using Sora.Orchestration;
using Sora.Orchestration.Attributes;

[SoraService(ServiceKind.SecretsVault, shortCode: "vault", name: "HashiCorp Vault",
    ContainerImage = "hashicorp/vault",
    DefaultTag = "1",
    DefaultPorts = new[] { 8200 },
    Capabilities = new[] { "protocol=http", "secrets=vault" },
    Volumes = new[] { "./Data/vault:/vault/data" },
    AppEnv = new[] { "Sora__Secrets__Vault__Address={scheme}://{host}:{port}" },
    HealthEndpoint = "/v1/sys/health",
    Scheme = "http", Host = "vault", EndpointPort = 8200, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 8200, LocalPattern = "http://{host}:{port}")]
internal sealed class VaultService { }
```

Known adapters in this repo (discovery snapshot)

- SecretsVault: `vault` — HashiCorp Vault (`src/Sora.Secrets.Vault/VaultService.cs`).
- Database: `postgres` — PostgreSQL (`src/Sora.Data.Postgres/PostgresAdapterFactory.cs`).
- Database: `mssql` — SQL Server (`src/Sora.Data.SqlServer/SqlServerAdapterFactory.cs`).
- Database: `mongo` — MongoDB (`src/Sora.Data.Mongo/MongoAdapterFactory.cs`).
- Cache: `redis` — Redis (`src/Sora.Data.Redis/RedisAdapterFactory.cs`).
- Vector: `weaviate` — Weaviate (`src/Sora.Data.Weaviate/WeaviateVectorAdapterFactory.cs`).
- Ai: `ollama` — Ollama (`src/Sora.Ai.Provider.Ollama/Properties/OrchestrationManifest.cs`).

Adapter patterns by kind (one-stop reference)

Data — relational databases (ServiceKind.Database)

- Purpose: Postgres, SQL Server, MySQL; expose stable scheme/port; add planner hints via Capabilities.
- DI: register your adapter factory and options under Sora:Data:<Provider>.
- Example (already in repo): see `Sora.Data.Postgres.PostgresAdapterFactory` and `[SoraService(ServiceKind.Database, shortCode: "postgres")]`.

Minimal pattern

```csharp
using Sora.Orchestration;
using Sora.Orchestration.Attributes;

[SoraService(ServiceKind.Database, shortCode: "mssql", name: "SQL Server",
    ContainerImage = "mcr.microsoft.com/mssql/server",
    DefaultTag = "2022-latest",
    DefaultPorts = new[] { 1433 },
    Capabilities = new[] { "protocol=tds", "vendor=sqlserver" },
    Volumes = new[] { "./Data/mssql:/var/opt/mssql" },
    AppEnv = new[] { "Sora__Data__SqlServer__ConnectionString=Server={host},{port};..." },
    HealthEndpoint = null,
    Scheme = "tcp", Host = "mssql", EndpointPort = 1433, UriPattern = "tcp://{host}:{port}",
    LocalScheme = "tcp", LocalHost = "localhost", LocalPort = 1433, LocalPattern = "tcp://{host}:{port}")]
internal sealed class SqlServerService { }
```

Data — non-relational (Mongo, Redis, etc.)

- Use Database or Cache kind depending on semantics.
- Example signature:

```csharp
[SoraService(ServiceKind.Database, shortCode: "mongo", name: "MongoDB",
    ContainerImage = "mongo", DefaultTag = "7", DefaultPorts = new[] { 27017 },
    Capabilities = new[] { "protocol=mongodb" },
    Volumes = new[] { "./Data/mongo:/data/db" },
    Scheme = "mongodb", Host = "mongo", EndpointPort = 27017, UriPattern = "mongodb://{host}:{port}",
    LocalScheme = "mongodb", LocalHost = "localhost", LocalPort = 27017, LocalPattern = "mongodb://{host}:{port}")]
internal sealed class MongoService { }
```

AI providers (ServiceKind.Ai)

- Purpose: model servers like Ollama; surface HTTP endpoints, health, and AppEnv hints.
- Use Capabilities to declare supported features (embeddings=true, chat=true, etc.).
- Example: `src/Sora.Ai.Provider.Ollama/Properties/OrchestrationManifest.cs`.

```csharp
[SoraService(ServiceKind.Ai, shortCode: "ollama", name: "Ollama",
    ContainerImage = "ollama/ollama", DefaultTag = "latest", DefaultPorts = new[] { 11434 },
    Capabilities = new[] { "protocol=http", "embeddings=true" },
    HealthEndpoint = "/api/tags",
    Scheme = "http", Host = "ollama", EndpointPort = 11434, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 11434, LocalPattern = "http://{host}:{port}")]
internal sealed class OllamaService { }
```

Messaging (ServiceKind.Messaging)

- Purpose: brokers (e.g., RabbitMQ, NATS). Prefer HTTP-based HealthEndpoint; otherwise rely on container health.
- Example pattern:

```csharp
[SoraService(ServiceKind.Messaging, shortCode: "rabbit", name: "RabbitMQ",
    ContainerImage = "rabbitmq", DefaultTag = "3-management", DefaultPorts = new[] { 5672, 15672 },
    Capabilities = new[] { "protocol=amqp" },
    HealthEndpoint = "/api/overview",
    Scheme = "amqp", Host = "rabbit", EndpointPort = 5672, UriPattern = "amqp://{host}:{port}",
    LocalScheme = "amqp", LocalHost = "localhost", LocalPort = 5672, LocalPattern = "amqp://{host}:{port}")]
internal sealed class RabbitMqService { }
```

Cache and Search (ServiceKind.Cache / ServiceKind.Search)

- Cache example (Redis):

```csharp
[SoraService(ServiceKind.Cache, shortCode: "redis", name: "Redis",
    ContainerImage = "redis", DefaultTag = "7", DefaultPorts = new[] { 6379 },
    Capabilities = new[] { "protocol=redis" },
    Scheme = "redis", Host = "redis", EndpointPort = 6379, UriPattern = "redis://{host}:{port}",
    LocalScheme = "redis", LocalHost = "localhost", LocalPort = 6379, LocalPattern = "redis://{host}:{port}")]
internal sealed class RedisService { }
```

- Search example (Elasticsearch/OpenSearch):

```csharp
[SoraService(ServiceKind.Search, shortCode: "opensearch", name: "OpenSearch",
    ContainerImage = "opensearchproject/opensearch", DefaultTag = "2",
    DefaultPorts = new[] { 9200 }, HealthEndpoint = "/", Capabilities = new[] { "protocol=http" },
    Scheme = "http", Host = "opensearch", EndpointPort = 9200, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 9200, LocalPattern = "http://{host}:{port}")]
internal sealed class OpenSearchService { }
```

Vector databases (ServiceKind.Vector)

- Example available in repo: `Sora.Data.Weaviate` uses `[SoraService(ServiceKind.Vector, shortCode: "weaviate")]`.
- Keep ports and HTTP health endpoints explicit; declare Capabilities such as `protocol=http`, `vectors=true`.

Object storage (ServiceKind.Storage)

- Purpose: S3-compatible, MinIO, local blob stores.
- Example (MinIO):

```csharp
[SoraService(ServiceKind.Storage, shortCode: "minio", name: "MinIO",
    ContainerImage = "minio/minio", DefaultTag = "latest", DefaultPorts = new[] { 9000, 9001 },
    Capabilities = new[] { "protocol=s3" },
    Volumes = new[] { "./Data/minio:/data" },
    AppEnv = new[] { "Sora__Storage__S3__Endpoint=http://{host}:9000" },
    HealthEndpoint = "/minio/health/live",
    Scheme = "http", Host = "minio", EndpointPort = 9000, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 9000, LocalPattern = "http://{host}:{port}")]
internal sealed class MinioService { }
```

Identity providers (ServiceKind.Auth)

- Purpose: external IdPs (Keycloak, Authentik). Use HTTP health and surface issuer/base URL via AppEnv.

```csharp
[SoraService(ServiceKind.Auth, shortCode: "keycloak", name: "Keycloak",
    ContainerImage = "quay.io/keycloak/keycloak", DefaultTag = "latest", DefaultPorts = new[] { 8080 },
    Capabilities = new[] { "protocol=http", "oidc=true" },
    AppEnv = new[] { "Sora__Auth__Oidc__Authority={scheme}://{host}:{port}/realms/master" },
    HealthEndpoint = "/realms/master/.well-known/openid-configuration",
    Scheme = "http", Host = "keycloak", EndpointPort = 8080, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 8080, LocalPattern = "http://{host}:{port}")]
internal sealed class KeycloakService { }
```

Conventions and guardrails

- Use controllers over inline endpoints (if your adapter exposes an HTTP API for dev tooling).
- Centralize constants (route names, env keys) in an Infrastructure/Constants class.
- No magic values: derive AppEnv keys from options classes and ADRs.
- Always declare Scheme/Host/EndpointPort and Local\* variants so planners can compose URIs.
- Prefer stable volumes under `./Data/<service>`.

Validation flow

- Build your solution (the generator emits the manifest and validates attributes).
- Run:

```pwsh
sora inspect
sora up --profile local --timeout 300
sora status --json
```

Edge cases

- Health checks only support HTTP endpoints; for TCP-only services, rely on container health or simple Running state.
- `DefaultPorts` control container-exposed ports; host ports are assigned by planners/profiles and surfaced in status.
- Use `Provides`/`Consumes` to express dependencies; avoid hard-coding container names across services.
- Keep `shortCode` lowercase, alphanumeric-plus-dash; avoid collisions.

See also

- Unified attributes and manifest-first discovery: ./orchestration-manifest-generator.md
- Orchestration reference: ../reference/orchestration.md
- Engineering front door: ./index.md
