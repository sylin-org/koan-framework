# Defensive Publication: Compile-Time Service Orchestration Manifest from Code Attributes

## 1. Header

| Field | Value |
|---|---|
| **Title** | Compile-Time Service Orchestration Manifest Generation from Declarative Code Attributes with Diagnostic Enforcement and Multi-Format Deployment Artifact Export |
| **Inventor** | Leo Botinelly (Leonardo Milson Botinelly Soares) |
| **Publication Date** | 2026-03-24 |
| **Framework** | Koan Framework v0.6.3 (.NET, target net10.0) |
| **Repository** | github.com/koan-framework (private; source excerpts included below) |
| **ADR Reference** | ARCH-0049 -- Unified Service Attribute and Orchestration Manifest Generator |
| **Classification** | Software Architecture -- DevOps Automation -- Compile-Time Code Generation -- Container Orchestration |
| **Status** | PUBLISHED -- This document is a defensive publication intended to constitute prior art and prevent patenting of the described techniques. |

---

## 2. Problem Statement

Deploying multi-service applications (an application service plus its infrastructure dependencies such as databases, caches, message brokers, AI inference engines, and vector stores) requires maintaining deployment manifests -- Docker Compose files, Helm charts, Kubernetes YAML, or platform-specific configuration -- that describe each service's container image, ports, environment variables, volumes, health checks, and inter-service dependencies. Developers face the following concrete problems:

1. **Manifest-code drift.** Docker Compose files, Helm charts, and other deployment descriptors are maintained as separate YAML/JSON artifacts that have no structural link to the service implementation code. When a database adapter changes its default port, adds a required environment variable, or updates its container image version, the corresponding deployment manifest must be manually updated. This disconnect is the primary source of "works on my machine" failures in team environments.

2. **No single source of truth.** Service metadata -- identity, container image, default ports, health endpoints, environment variables, volume mounts, capability declarations, and dependency relationships -- is scattered across multiple locations: NuGet package metadata, README files, Docker Hub documentation, adapter source code comments, and hand-written YAML. There is no mechanism to declare this metadata once, at the service implementation site, and derive all downstream artifacts from it.

3. **Late discovery of misconfiguration.** Errors in deployment manifests (invalid service identifiers, duplicate short codes, missing container images for containerized services, use of unstable `latest` tags) are discovered only at container runtime. There is no compile-time enforcement of orchestration metadata correctness.

4. **Profile-specific complexity.** Different deployment profiles (local development, CI, staging, production) require different volume strategies (bind mounts vs. named volumes vs. no mounts), different port allocation strategies (static vs. auto-avoidance of conflicts), and different network configurations. Maintaining per-profile variants of deployment manifests multiplies the maintenance burden.

5. **Disconnected tooling.** Existing infrastructure-as-code tools (Terraform, Pulumi, AWS CDK) operate at the infrastructure layer and are disconnected from application-level service metadata. Platform-specific tools (.NET Aspire) require explicit resource declarations in a separate AppHost project rather than deriving configuration from the service implementations themselves.

No existing framework or tool derives deployment manifests directly from attributes declared on the service adapter implementation classes, enforces correctness via compile-time Roslyn diagnostics, and generates deployment artifacts for multiple target formats from a single intermediate manifest.

---

## 3. Prior Art Survey

### 3.1 Container Orchestration Tools

| Tool | Approach | Gap |
|---|---|---|
| **Docker Compose** | Standalone YAML files (`docker-compose.yml`) describing services, networks, volumes | Manually maintained; no link to application code; no compile-time validation; no multi-profile generation from code |
| **Kubernetes / Helm** | YAML manifests or templated Helm charts | Separate artifact repository; significant boilerplate; no derivation from service code; no compile-time diagnostics |
| **Podman Compose** | Compatible with Docker Compose YAML format | Same limitations as Docker Compose |

### 3.2 Infrastructure-as-Code Frameworks

| Tool | Language | Approach | Gap |
|---|---|---|---|
| **Terraform** | HCL | Declarative infrastructure definitions | Infrastructure-layer only; no application-service metadata; no compile-time integration with .NET |
| **Pulumi** | TypeScript/Python/Go/C# | Imperative infrastructure code | Separate from service implementation; does not derive configuration from service adapter attributes |
| **AWS CDK** | TypeScript/Python/Java/C# | Construct-based infrastructure | AWS-specific; separate project; no attribute-driven metadata extraction |
| **Bicep** | Domain-specific | Azure Resource Manager templates | Azure-specific; infrastructure-layer only |

### 3.3 Application Platform Tools

| Tool | Approach | Gap |
|---|---|---|
| **.NET Aspire** | Code-based resource declarations in `AppHost` project (`builder.AddPostgres()`, `builder.AddProject()`) | Requires explicit per-resource declarations in a separate orchestrator project; does not derive from attributes on the service adapter class; no compile-time diagnostics for service metadata correctness; no offline manifest generation |
| **Dapr** | Sidecar-based runtime with component YAML files | Runtime configuration; separate YAML components; no compile-time manifest generation |
| **Project Tye** (archived) | Convention-based service discovery from `tye.yaml` | Separate YAML; project archived; no compile-time attribute extraction |

### 3.4 Source Generator Ecosystem

| Tool | Approach | Gap |
|---|---|---|
| **Roslyn Source Generators** (general) | Compile-time code generation from syntax/semantic analysis | General mechanism; no existing generator targets orchestration manifest generation from service attributes |
| **System.Text.Json generators** | Source-generated JSON serializers | JSON serialization only; no orchestration domain knowledge |
| **Swagger/OpenAPI generators** | Source-generated API documentation from controller attributes | API documentation only; no deployment manifest generation |

### 3.5 Key Differentiators of This Invention

No surveyed system provides all of the following in combination:

- **Attribute-at-implementation-site**: Service orchestration metadata (image, ports, health, env, volumes, dependencies, capabilities) declared as C# attributes on the service adapter class itself
- **Compile-time Roslyn source generator** that extracts attribute metadata into an embedded JSON manifest constant
- **Seven compile-time diagnostics** (Koan0049A through Koan0049G) enforcing metadata correctness during compilation
- **CLI-driven artifact generation** producing deployment files (Docker Compose, with extensibility to Helm and others) from the embedded manifest
- **Profile-aware volume and port strategies** (local bind mounts, CI named volumes, production no-mounts; port conflict auto-avoidance)
- **Host mount discovery via reflection** cross-referencing `HostMountAttribute` and `ContainerDefaultsAttribute` to inject persistence volumes automatically
- **Dependency ordering with health-aware conditions** (`service_healthy` vs. `service_started` based on health check presence)

---

## 4. Detailed Description

### 4.1 Architecture Overview

The system comprises four layers operating in a pipeline from source code to deployment artifacts:

```
    +--------------------------------------------------+
    |  Service Adapter Classes                          |
    |  [KoanService(Database, "postgres", "PostgreSQL", |
    |     ContainerImage = "postgres",                  |
    |     DefaultTag = "17",                            |
    |     DefaultPorts = [5432],                        |
    |     HealthEndpoint = "/health",                   |
    |     Env = ["POSTGRES_USER=koan", ...],            |
    |     Volumes = ["/var/lib/postgresql/data"],        |
    |     Provides = ["relational", "sql"],             |
    |     Consumes = [])]                               |
    +-------------------------+------------------------+
                              |  Compile time
    +-------------------------v------------------------+
    |  OrchestrationManifestGenerator                  |
    |  (Roslyn ISourceGenerator)                       |
    |  - Scans all class declarations                  |
    |  - Extracts KoanService + legacy attributes      |
    |  - Emits diagnostics (Koan0049A-G)               |
    |  - Builds JSON manifest                          |
    |  - Emits __KoanOrchestrationManifest.g.cs        |
    |    with embedded JSON constant                   |
    +-------------------------+------------------------+
                              |  Embedded in assembly
    +-------------------------v------------------------+
    |  Orchestration CLI / Planner                     |
    |  - Reads manifest from assembly constant         |
    |  - Resolves profiles (Local, CI, Prod)           |
    |  - Allocates ports (PortAllocator)               |
    |  - Builds Plan (ServiceSpec list)                |
    |  - Persists LaunchManifest (JSON with versions)  |
    +-------------------------+------------------------+
                              |
        +----------+----------+----------+
        |          |          |          |
    +---v---+  +---v---+  +--v----+  +--v----+
    |Compose|  | Helm  |  |Podman |  | Other |
    |Export |  | Export |  |Export |  |Export |
    +-------+  +-------+  +------+  +------+
```

### 4.2 KoanServiceAttribute -- The Unified Declaration Point

The `KoanServiceAttribute` is a single attribute applied to service adapter classes that encodes the complete orchestration metadata for a service. It replaces what would traditionally require four to six separate files (Dockerfile, docker-compose.yml fragment, Helm values, environment files, health check scripts, and documentation).

**Attribute structure:**

```
KoanServiceAttribute(ServiceKind kind, string shortCode, string name)
    Positional Parameters:
        kind:       ServiceKind enum (App, Database, Vector, Ai, Auth,
                    Messaging, Storage, Cache, Search, Other, SecretsVault)
        shortCode:  Unique identifier (2-32 chars, lowercase a-z0-9-,
                    starts with letter, no trailing hyphen)
        name:       Human-readable display name

    Named Properties (all optional, init-only):
        QualifiedCode:          Dot-separated hierarchy (e.g., "koan.db.relational.postgres")
        Subtype:                Finer classification within ServiceKind
        Description:            Human-readable description
        DeploymentKind:         Container (default), External, InProcess
        ContainerImage:         Base image name (e.g., "postgres")
        DefaultTag:             Image tag (e.g., "17")
        DefaultPorts:           int[] of container ports
        HealthEndpoint:         HTTP health check path
        HealthIntervalSeconds:  Health check interval
        HealthTimeoutSeconds:   Health check timeout
        HealthRetries:          Health check retry count
        Capabilities:           string[] of "key=value" capability declarations
        Provides:               string[] of capabilities this service provides
        Consumes:               string[] of capabilities this service requires
        Env:                    string[] of "KEY=VALUE" environment variables
        Volumes:                string[] of container volume paths
        AppEnv:                 string[] of env vars to inject into the App service
        Scheme/Host/EndpointPort/UriPattern:       Container endpoint defaults
        LocalScheme/LocalHost/LocalPort/LocalPattern: Local dev endpoint defaults
        Version:                Manifest schema version (default 1)
```

**Companion enumerations:**

- `ServiceKind`: `App`, `Database`, `Vector`, `Ai`, `Auth`, `Messaging`, `Storage`, `Cache`, `Search`, `Other`, `SecretsVault`
- `DeploymentKind`: `Container`, `External`, `InProcess`

**Design rationale:**

- All metadata lives on the class that implements the service adapter, establishing a single source of truth.
- Init-only properties with defaults mean minimal ceremony for simple services while allowing full specification for complex ones.
- The `Provides`/`Consumes` arrays enable dependency graph construction without requiring explicit `depends_on` declarations -- the planner can infer that a service consuming "relational" depends on any service providing "relational".
- `AppEnv` with token substitution (`{serviceId}`, `{port}`) enables automatic connection string injection into the application service.

### 4.3 OrchestrationManifestGenerator -- Compile-Time Extraction

The `OrchestrationManifestGenerator` is a Roslyn `ISourceGenerator` that runs during compilation and performs the following:

**Phase 1 -- Assembly-level attribute collection:**
- Scans for `OrchestrationServiceManifestAttribute` (assembly-level) to capture service type overrides
- Scans for `AuthProviderDescriptorAttribute` (assembly-level) to capture authentication provider metadata

**Phase 2 -- Class-level attribute extraction:**
For each non-abstract class declaration in the compilation:
- Checks for `KoanServiceAttribute` (unified, ARCH-0049)
- Falls back to legacy attributes: `ServiceIdAttribute`, `ContainerDefaultsAttribute`, `EndpointDefaultsAttribute`, `AppEnvDefaultsAttribute`, `HealthEndpointDefaultsAttribute`
- Extracts `KoanAppAttribute` from classes implementing `IKoanManifest` to capture application-level metadata

**Phase 3 -- Diagnostic enforcement:**
Seven compile-time diagnostics are emitted:

| ID | Severity | Trigger |
|---|---|---|
| **Koan0049A** | Info | `[KoanService]` applied to a class not implementing `IServiceAdapter` |
| **Koan0049B** | Error | Invalid `shortCode` (must be 2-32 chars, lowercase `[a-z0-9-]`, start with letter, no trailing hyphen) |
| **Koan0049C** | Error | Reserved `shortCode` (system-reserved identifiers) |
| **Koan0049D** | Warning | `qualifiedCode` does not match dot-separated lowercase pattern |
| **Koan0049E** | Warning | `DeploymentKind=Container` but no container image provided |
| **Koan0049F** | Info | `DefaultTag` is `latest` (unstable for reproducible dev) |
| **Koan0049G** | Error | Duplicate `shortCode` within the same compilation unit |

These diagnostics enforce metadata correctness at the earliest possible point -- during compilation, before any container or deployment artifact is generated.

**Phase 4 -- JSON manifest emission:**
The generator constructs a JSON string containing:
- Schema version (`schemaVersion: 1`)
- Application metadata (`app` object with code, name, description, default public port)
- Authentication providers array
- Services array, where each entry contains all extracted metadata (id, image, ports, env, volumes, appEnv, endpoint defaults, health settings, kind, capabilities, provides, consumes, qualified code, subtype, deployment kind)

This JSON is embedded as a string constant in a generated class:

```csharp
// Generated: __KoanOrchestrationManifest.g.cs
namespace Koan.Orchestration
{
    public static class __KoanOrchestrationManifest
    {
        public const string Json = "{\"schemaVersion\":1,\"services\":[...]}";
    }
}
```

The constant is available at runtime without file I/O, reflection, or deserialization overhead beyond the initial parse.

### 4.4 RegistrySourceGenerator -- Service Registration Code Generation

A second Roslyn `IIncrementalGenerator` (`RegistrySourceGenerator`) scans all class declarations for framework-recognized interfaces and attributes, generating `[ModuleInitializer]`-annotated code that automatically registers:

- `IKoanInitializer` implementations (boot-time initialization)
- `IKoanAutoRegistrar` implementations (DI service registration)
- `IKoanBackgroundService` implementations (with full metadata: enabled, configuration section, lifetime, priority, environment-specific run flags, periodic/startup/pokable/health-contributor capabilities)
- `IServiceDiscoveryAdapter` implementations (service discovery)
- `[Embedding]`-annotated entity types (vector embedding registration)

The generated code calls `KoanRegistry.RegisterInitializers(...)`, `KoanRegistry.RegisterAutoRegistrars(...)`, etc., with pre-built descriptor arrays. This eliminates runtime assembly scanning and ensures all service registrations are determined at compile time.

**Design rationale:**
- `[ModuleInitializer]` ensures registration occurs when the assembly is loaded, before any DI container build.
- The `BackgroundServiceDescriptor` record captures eight metadata fields per service, enabling environment-aware service activation without runtime reflection.
- Assembly name sanitization handles special characters and numeric prefixes to produce valid C# identifiers for the generated module class.

### 4.5 ComposeExporter -- Profile-Aware Artifact Generation

The `ComposeExporter` implements `IArtifactExporter` and generates a Docker Compose YAML file from a `Plan` (profile + service list):

**Network topology:**
- Two networks: an internal network (inter-service communication) and an external network (public access)
- Application services attach to both networks; infrastructure services attach only to the internal network

**Health check generation:**
- For services with HTTP health endpoints, generates multi-strategy health checks: `curl`, `wget` fallback, and `bash /dev/tcp` probe
- Health check interval, timeout, and retry count are derived from attribute metadata

**Dependency ordering:**
- `depends_on` conditions use `service_healthy` when the dependency has an HTTP health check, and `service_started` otherwise
- This prevents services from starting before their dependencies are ready

**Profile-specific volume strategies:**

| Profile | Volume Strategy |
|---|---|
| Local / Staging | Bind mounts (`./Data/{service}:/container/path`) for visible data persistence |
| CI | Named volumes (`data_{service}:/container/path`) for host filesystem isolation |
| Prod | No automatic mount injection (operator-managed) |

**Host mount discovery:**
The exporter uses runtime reflection to discover `HostMountAttribute` and `ContainerDefaultsAttribute` across loaded Koan assemblies. This cross-references container image prefixes with declared mount paths to automatically inject the correct volume mappings for each service, even when the service's adapter is defined in a different assembly than the one being composed.

**Fallback heuristics:**
When no attribute-driven mount mapping exists, image-name-based heuristics inject standard data paths:
- `postgres` / `postgresql` -> `/var/lib/postgresql/data`
- `mongo` -> `/data/db`
- `redis` -> `/data`
- `sqlserver` -> `/var/opt/mssql`
- `weaviate` -> `/var/lib/weaviate`
- `ollama` -> `/root/.ollama`

**Port conflict auto-avoidance:**
The `PortAllocator` probes each requested host port via `TcpListener` binding. If a port is occupied, it increments and retries (up to a configurable probe guard, default 200 attempts). Assigned ports are tracked across services to prevent intra-plan conflicts. The probe guard is configurable via the `Koan_PORT_PROBE_MAX` environment variable.

**Build context resolution:**
For the application service (convention id "api"), the exporter detects if a `.csproj` file exists in the working directory and emits a `build:` block with `context` pointing to the repository root (detected by walking parent directories for `.sln` or `src/` folder presence) and `dockerfile` pointing to a local `Dockerfile` if present.

### 4.6 LaunchManifest -- Persistent Allocation State

The system persists a `LaunchManifest` JSON file in the `.Koan/` convention directory. This file records:
- Application identity (id, name, code, default and assigned public ports)
- Orchestration options (expose internals flag, provider, last profile)
- Per-service port allocations (assigned public ports)

On subsequent runs, the planner reads the manifest to maintain port stability across restarts. The manifest includes a backup-before-overwrite mechanism (timestamped `.old.{yyyyMMdd-HHmmss-fff}` copies) and auto-generates a `.gitignore` to prevent accidental commits of runtime state while preserving the generated `compose.yml`.

### 4.7 CLI Integration

The orchestration CLI exposes commands that consume the manifest pipeline:

| Command | Purpose |
|---|---|
| `koan up` | Generate manifest, resolve ports, start containers |
| `koan down` | Stop containers |
| `koan export [format]` | Generate deployment artifact without starting (default: compose) |
| `koan inspect` | Display resolved service topology |
| `koan doctor` | Validate environment readiness |
| `koan status` | Show running service status |
| `koan logs` | Aggregate container logs |

The `export` command accepts flags: `--out` (output path), `--profile` (Local/CI/Prod), `--base-port` (port range start), `--port` (override), `--expose-internals` (attach infra to external network), `--no-launch-manifest` (skip persistence).

### 4.8 End-to-End Flow

1. Developer adds a NuGet package reference (e.g., `Koan.Data.Connector.Mongo`) containing a class decorated with `[KoanService(Database, "mongo", "MongoDB", ContainerImage = "mongo", DefaultTag = "7", DefaultPorts = [27017], ...)]`
2. At compile time, `OrchestrationManifestGenerator` extracts the attribute metadata, validates it (emitting diagnostics if needed), and embeds a JSON manifest constant in the output assembly
3. Simultaneously, `RegistrySourceGenerator` generates `[ModuleInitializer]` code to register the adapter's auto-registrar, background services, and discovery adapter
4. At `koan up` or `koan export compose`, the CLI reads the embedded manifest, builds a `Plan` with the target profile, allocates ports, and passes the plan to `ComposeExporter`
5. `ComposeExporter` generates a complete `docker-compose.yml` with services, networks, health checks, volumes, environment variables, dependencies, and build context
6. The LaunchManifest is persisted for port stability on next run

The developer never writes or maintains a Docker Compose file. Adding or removing a service adapter package automatically updates the generated deployment artifacts.

---

## 5. Claims

The following claims describe the novel aspects of this invention. They are published defensively to establish prior art and prevent others from obtaining patent protection on these techniques.

**Claim 1.** A method for deriving container orchestration deployment manifests from attributes declared on service implementation classes in a compiled language, wherein a source code generator operating at compile time: (a) scans class declarations for a service attribute encoding identity, container image, ports, health endpoints, environment variables, volume mounts, capabilities, and inter-service dependencies; (b) validates the attribute metadata via compile-time diagnostics; and (c) emits an intermediate JSON manifest as an embedded string constant in the compiled assembly.

**Claim 2.** A compile-time diagnostic enforcement system for service orchestration metadata comprising seven or more diagnostic rules that validate, during compilation: (a) that the service attribute is applied to classes implementing a required interface (Koan0049A); (b) that the service short code conforms to a syntactic pattern of 2-32 lowercase alphanumeric characters with hyphens (Koan0049B); (c) that the short code is not a reserved system identifier (Koan0049C); (d) that a qualified code follows dot-separated lowercase conventions (Koan0049D); (e) that container-deployed services declare a container image (Koan0049E); (f) that default tags are not the unstable `latest` value (Koan0049F); and (g) that short codes are unique within a compilation unit (Koan0049G).

**Claim 3.** A system wherein a single attribute on a service adapter class simultaneously encodes: container deployment metadata (image, tag, ports, volumes, environment variables), health check configuration (HTTP endpoint, interval, timeout, retries), endpoint defaults for both container and local-development modes (scheme, host, port, URI pattern), capability declarations (provides/consumes arrays), and service classification (kind, subtype, qualified code), such that this single declaration site serves as the source of truth for container orchestration, service discovery, endpoint resolution, and dependency graph construction.

**Claim 4.** A method for generating Docker Compose YAML files from an intermediate service manifest, wherein the generator: (a) creates a dual-network topology (internal for inter-service communication, external for public access) with services assigned to networks based on their role; (b) generates multi-strategy health checks combining `curl`, `wget` fallback, and TCP socket probe for resilience across base images; (c) selects dependency conditions (`service_healthy` vs. `service_started`) based on whether each dependency declares an HTTP health endpoint; and (d) applies profile-specific volume strategies (bind mounts for local development, named volumes for CI, no automatic mounts for production).

**Claim 5.** A method for automatic host volume injection in generated deployment manifests, wherein the system: (a) uses runtime reflection to discover `HostMountAttribute` and `ContainerDefaultsAttribute` across loaded framework assemblies; (b) cross-references container image prefixes with declared mount paths to resolve the correct volume mappings; and (c) applies image-name-based fallback heuristics (mapping known database images to their standard data directories) when no attribute-driven mapping is available, such that service adapters defined in separate assemblies automatically receive correct persistence volume configuration in generated deployment files.

**Claim 6.** A port conflict auto-avoidance system for multi-service deployment manifests, wherein: (a) a port allocator probes each requested host port by attempting a TCP listener bind on the loopback interface; (b) if the port is occupied, the allocator increments the port number and retries up to a configurable probe guard limit; (c) assigned ports are tracked across all services in the plan to prevent intra-plan conflicts; and (d) assigned port allocations are persisted in a launch manifest file with backup-before-overwrite semantics to maintain port stability across restarts.

**Claim 7.** A compile-time code generation system that uses `[ModuleInitializer]`-annotated methods to register service descriptors (initializers, auto-registrars, background services with environment-specific run flags, service discovery adapters, and embedding entity types) at assembly load time from pre-built descriptor arrays, eliminating runtime assembly scanning while providing a Roslyn incremental generator that is triggered by interface implementation and attribute presence on class declarations.

**Claim 8.** A method for converting service adapter package references into deployment infrastructure, wherein: (a) adding a NuGet package reference containing a class with a `[KoanService]` attribute causes the compile-time generator to include that service in the embedded manifest; (b) removing the package reference causes the service to disappear from subsequent manifest generations; and (c) no manual editing of deployment YAML, Helm charts, or infrastructure-as-code files is required, establishing a "reference equals intent" model where the dependency graph of the application source code determines the deployment topology.

**Claim 9.** A method for generating application-service connection configuration from infrastructure-service attributes, wherein: (a) each infrastructure service attribute declares `AppEnv` entries with token placeholders (`{serviceId}`, `{port}`); (b) the manifest generator and planner resolve these tokens at generation time; and (c) the resulting environment variables are injected into the application service's container configuration, such that the application service receives correctly configured connection strings for all infrastructure services without manual environment variable declaration.

**Claim 10.** A deployment artifact export system with a pluggable exporter interface (`IArtifactExporter`) that: (a) accepts a `Plan` record (immutable, comprising a profile and a read-only list of service specifications) and generates format-specific output; (b) supports multiple export formats (Docker Compose, with extensibility to Helm, Kubernetes YAML, and others) from the same intermediate plan; and (c) is invokable both programmatically and via CLI commands with flags for output path, profile selection, port override, internal service exposure, and manifest persistence control.

---

## 6. Implementation Evidence

The described invention is fully implemented and operational in Koan Framework v0.6.3. The following source files constitute the reference implementation:

### 6.1 Attribute Definitions (Koan.Orchestration.Abstractions assembly)

| File | Purpose |
|---|---|
| `src/Koan.Orchestration.Abstractions/Attributes/KoanServiceAttribute.cs` | Unified service attribute (59 lines) with all orchestration metadata properties |
| `src/Koan.Orchestration.Abstractions/Attributes/OrchestrationServiceManifestAttribute.cs` | Assembly-level manifest attribute for backward-compatible service declarations |
| `src/Koan.Orchestration.Abstractions/ServiceKind.cs` | Enum: App, Database, Vector, Ai, Auth, Messaging, Storage, Cache, Search, Other, SecretsVault |
| `src/Koan.Orchestration.Abstractions/DeploymentKind.cs` | Enum: Container, External, InProcess |
| `src/Koan.Orchestration.Abstractions/Models/Plan.cs` | Immutable record: Profile + IReadOnlyList of ServiceSpec |
| `src/Koan.Orchestration.Abstractions/Models/ServiceSpec.cs` | Immutable record: Id, Image, Env, Ports, Volumes, Health, Type, DependsOn |

### 6.2 Source Generators

| File | Purpose |
|---|---|
| `src/Koan.Orchestration.Generators/OrchestrationManifestGenerator.cs` | Roslyn ISourceGenerator: attribute extraction, 7 diagnostics (Koan0049A-G), JSON manifest emission |
| `src/Koan.Core.Registry.Generators/RegistrySourceGenerator.cs` | Roslyn IIncrementalGenerator (431 lines): ModuleInitializer code generation for service registration |

### 6.3 Deployment Artifact Exporters

| File | Purpose |
|---|---|
| `src/Connectors/Orchestration/Renderers/Compose/ComposeExporter.cs` | Docker Compose YAML generation (413 lines): networks, health checks, volumes, dependencies, profiles |
| `src/Koan.Orchestration.Cli.Core/Planning/PortAllocator.cs` | TCP port conflict auto-avoidance with configurable probe guard |
| `src/Koan.Orchestration.Cli.Core/Planning/LaunchManifest.cs` | Persistent JSON manifest for port allocation stability |

### 6.4 CLI Commands

| File | Purpose |
|---|---|
| `src/Koan.Orchestration.Cli/Commands/ExportCliCommand.cs` | CLI command: format, output, profile, port override flags |
| `src/Koan.Orchestration.Cli/Commands/UpCliCommand.cs` | CLI command: generate + start containers |
| `src/Koan.Orchestration.Cli/Commands/InspectCliCommand.cs` | CLI command: display resolved topology |
| `src/Koan.Orchestration.Cli/Runtime/CommandRuntime.cs` | Orchestration command execution runtime |

### 6.5 Example Service Adapter Manifests

| File | Purpose |
|---|---|
| `src/Connectors/Data/Mongo/Properties/OrchestrationManifest.cs` | MongoDB adapter orchestration metadata |
| `src/Connectors/Data/Vector/Weaviate/Properties/OrchestrationManifest.cs` | Weaviate adapter orchestration metadata |

### 6.6 Framework Version and Build Target

- Framework version: v0.6.3
- Build target: net10.0
- All source files are compiled and tested as part of the standard CI pipeline.

---

## 7. Publication Notice

This document is a **defensive publication**. It is published to establish prior art and to prevent any party -- including the inventor, the inventor's employer, or any third party -- from obtaining patent protection on the techniques described herein.

**Intent:** The sole purpose of this publication is to ensure that the described techniques remain freely available for use by the public. This publication does not grant any patent rights, nor does it restrict anyone from implementing the described techniques.

**Scope:** This publication covers the specific combination of: (a) a declarative attribute on service implementation classes encoding orchestration metadata; (b) a compile-time Roslyn source generator that extracts this metadata into an embedded JSON manifest with diagnostic enforcement; (c) a pluggable artifact exporter system generating deployment files from the manifest; (d) profile-aware volume and port allocation strategies; (e) automatic host mount discovery via cross-assembly reflection; (f) ModuleInitializer-based service registration code generation; and (g) a "reference equals intent" model where NuGet package references determine deployment topology. Individual components may have prior art in other domains; the novelty lies in their combination for attribute-driven deployment manifest generation.

**Date of first implementation:** 2025 (ARCH-0049 ADR acceptance and initial code merge).

**Date of this publication:** 2026-03-24.

**Inventor acknowledgment:** I, Leo Botinelly (Leonardo Milson Botinelly Soares), confirm that the techniques described in this document are my original work, implemented within the Koan Framework, and are published here defensively to prevent patenting.

---

## Appendix A: Antagonist Cycle Review

The following adversarial review was conducted to stress-test the claims and identify weaknesses.

### A.1 Challenge: "This is just code generation from annotations, which Java Spring and others have done for years"

**Response:** Java Spring annotations (e.g., `@SpringBootApplication`, `@Service`) drive runtime dependency injection and bean registration, not deployment manifest generation. Spring does not generate Docker Compose files, Helm charts, or Kubernetes YAML from annotations on service classes. Spring Cloud has service discovery (Eureka, Consul) but these are runtime systems, not compile-time manifest generators. The described invention operates at compile time via Roslyn source generators and produces deployment artifacts, which is a fundamentally different pipeline stage and output category.

### A.2 Challenge: ".NET Aspire already generates Docker Compose from code"

**Response:** .NET Aspire requires developers to write explicit resource declarations in a separate `AppHost` project: `builder.AddPostgres("pg")`, `builder.AddProject<MyApi>("api")`, etc. The resource configuration is not derived from attributes on the service adapter class. In the described invention, adding a package reference (e.g., to a Postgres adapter with `[KoanService]`) automatically includes that service in the generated manifest -- no explicit declaration in a separate project is needed. Furthermore, Aspire does not emit compile-time diagnostics for service metadata correctness (no equivalent of Koan0049A-G), does not persist port allocations for stability, and does not support the profile-aware volume strategy (bind mounts vs. named volumes vs. no mounts) described in Claim 4.

### A.3 Challenge: "Dockerfiles and docker-compose.yml generation from code exists in tools like docker init"

**Response:** `docker init` generates a one-time Dockerfile and compose file scaffold based on the detected project type. It does not maintain an ongoing link between service adapter code and deployment configuration. When a new database adapter is added to the project, `docker init` does not detect the change and update the compose file. The described invention maintains this link through compile-time attribute extraction: every build regenerates the manifest from the current attribute state, ensuring the deployment configuration always reflects the current code.

### A.4 Challenge: "The seven diagnostics are just lint rules -- any linter could check YAML"

**Response:** YAML lint rules operate on the deployment artifact (the compose file) after it has been written, and they can only check syntactic correctness, not semantic validity relative to the service implementation. The described diagnostics operate at the source code level during compilation: they validate that the metadata declared on the service adapter class is correct (valid short code format, no reserved words, no duplicates, image provided for container-deployed services, stable tags). This is fundamentally earlier in the pipeline and catches a category of errors (metadata declared on the wrong class, duplicate identifiers across adapters, missing images) that no deployment-artifact linter can detect because the linter lacks the code-level context.

### A.5 Challenge: "Port auto-avoidance is trivial -- just pick a random available port"

**Response:** Random port selection breaks reproducibility. The described system has three properties that distinguish it: (a) deterministic incrementing from the requested port rather than random selection, preserving locality; (b) cross-service conflict tracking within a single plan generation, preventing two services from receiving the same port; (c) persistent allocation in a LaunchManifest with backup-before-overwrite, so that subsequent runs reuse the same ports when available. This combination provides both conflict avoidance and stability, which random selection does not.

### A.6 Challenge: "Host mount discovery via reflection is fragile and non-deterministic"

**Response:** The publication acknowledges this: the `DiscoverHostMounts` method is wrapped in a catch-all that returns an empty dictionary on failure, and the system falls back to image-name heuristics when attribute-driven discovery fails. This defense-in-depth approach (attribute discovery -> image heuristic -> no mount) is part of the design. The reflection is best-effort and additive: it only adds volume mappings, never removes them, and the fallback path ensures basic functionality even when reflection encounters missing dependencies.

### A.7 Challenge: "Embedding JSON in a string constant is inefficient and limits manifest size"

**Response:** The JSON manifest is a compile-time constant, which means it occupies space in the assembly's metadata tables but requires zero runtime allocation until first accessed. For the typical service count in a Koan application (3-15 services), the JSON is well under 10KB. The constant is parsed once at CLI startup. This is materially more efficient than the alternative (runtime reflection scanning all loaded assemblies for attributes), which is exactly what the system is designed to eliminate.

### A.8 Challenge: "Could someone claim the 'reference equals intent' model is just transitive dependency resolution, which all package managers do?"

**Response:** Package managers resolve transitive dependencies for compilation and runtime linking. The described invention extends this to deployment topology: adding a package reference that contains a `[KoanService]`-decorated class causes a new service to appear in the Docker Compose output. No existing package manager produces deployment manifests from package references. The mapping from NuGet reference -> compile-time attribute extraction -> manifest embedding -> deployment artifact generation is a novel pipeline that uses the package graph as an input to infrastructure generation, not just to code compilation.

### A.9 Challenge: "The multi-strategy health check (curl, wget, TCP probe) is just defensive scripting"

**Response:** While each individual strategy is known, the specific combination in a generated health check command -- with ordered fallbacks and a unified exit-code contract -- addresses a real problem in container orchestration: base images vary in available tools (Alpine has neither curl nor wget by default, Debian has wget but not always curl, distroless images have neither). The generated `CMD-SHELL` test `(curl -fsS URL || wget -q -O- URL || bash /dev/tcp/HOST/PORT) >/dev/null 2>&1 || exit 1` maximizes compatibility across base images without requiring the service adapter author to know what tools are available. This is generated automatically from the `HealthEndpoint` attribute property.

### A.10 Challenge: "The claim scope is too broad -- individual pieces are not novel"

**Response:** This publication explicitly acknowledges that individual techniques (attributes, source generators, YAML generation, port allocation, health checks) have prior art. The novelty lies in the specific integrated pipeline: attribute-on-implementation-class -> compile-time extraction with diagnostics -> embedded manifest -> profile-aware artifact generation with automatic volume/port/dependency resolution. This end-to-end flow, where a developer adds a package reference and receives a working deployment topology without writing any infrastructure configuration, has not been demonstrated in any surveyed prior art. This publication ensures the integrated approach remains in the public domain.
