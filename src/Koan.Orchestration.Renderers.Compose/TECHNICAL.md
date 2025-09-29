---
uid: reference.modules.koan.orchestration.renderers.compose
title: Koan.Orchestration.Renderers.Compose – Technical Reference
description: Docker/Podman Compose exporter for Koan orchestration plans, covering service rendering, persistence heuristics, and healthcheck wiring.
since: 0.6.3
packages: [Sylin.Koan.Orchestration.Renderers.Compose]
source: src/Koan.Orchestration.Renderers.Compose/
validation:
  date: 2025-09-29
  status: verified
---

## Contract

- Implement the `IArtifactExporter` contract (`Id = "compose"`) for rendering `Koan.Orchestration.Models.Plan` into a Docker Compose v2 YAML file.
- Honor Koan orchestration profiles (Local, Ci, Staging, Prod) while materializing services, volumes, healthchecks, and network assignments.
- Respect adapter-provided metadata (`HostMountAttribute`, `ContainerDefaultsAttribute`, `DefaultEndpointAttribute`) to infer persistence mounts and endpoint hints.
- Emit artifacts that the Koan CLI (`Koan export compose`, `Koan up`) can consume without post-processing.

## Core components

| Concern | Types | Notes |
| --- | --- | --- |
| Exporter contract | `ComposeExporter : IArtifactExporter` | `Supports("compose")` and renders content via `GenerateAsync(plan, profile, outPath, ct)`. `ExporterCapabilities(false, true, false)` advertises readiness probe support. |
| Host mount discovery | `DiscoverHostMounts`, `EnsureHostMounts` | Reflect over loaded `Koan.*` assemblies for `HostMountAttribute`/`ContainerDefaultsAttribute` to map container paths. Fallbacks use image heuristics (Postgres, Mongo, Redis, SQL Server, Weaviate, Ollama). Profile-aware: Local/Staging ⇒ bind `./Data/{id}`, Ci ⇒ named volumes, Prod ⇒ untouched. |
| Service rendering | `WriteService` | Serializes per-service YAML (image/build, environment, ports, volumes, healthcheck, depends_on, networks) with indentation control. App (`id == "api"`) receives build context + Dockerfile detection. |
| Healthcheck wiring | `ParseHostPortFromUrl`, inline shell command | Generates Compose `healthcheck.test` using `curl` → `wget` → `bash -lc 'exec /dev/tcp'` fallback, reusing plan `HealthSpec` interval/timeout/retries. |
| Network defaults | Constants from `OrchestrationConstants` | Always declare `internal` and `external` networks; adapters join `internal`, app joins both. |
| Volume aggregation | `namedVolumes` accumulator | Collects named volumes introduced by CI profile to emit a top-level `volumes:` block. |
| Build context heuristics | `FindRepoRoot`, `ToPosixPath` | When the current directory contains a project file, a `build` block is emitted with context rooted at repository root (if detected) and optional Dockerfile pointer. |

## Rendering flow (`GenerateAsync`)

1. Validate path and ensure containing directory exists.
2. Emit top-level network definitions (`networks.internal` with `internal: true`, `networks.external`).
3. Iterate services in plan order:
   - Call `EnsureHostMounts` to augment `svc.Volumes` based on profile and image metadata.
   - Write Compose service block with `image`, optional `build`, `environment`, `ports`, `volumes`, `healthcheck`, `depends_on`, and `networks` sections.
   - Track any named volumes introduced when running in Ci profile.
4. After all services are rendered, append a `volumes:` section when named volumes were registered.
5. Persist the composed YAML to `outPath` using UTF-8 text.

### Service serialization details

- **Environment variables** – Null values omitted. `${VAR}` style entries stay unquoted for Compose interpolation; others are JSON-escaped and quoted.
- **Ports** – Only host-published ports (`host > 0`) are emitted (Compose automatically handles container-only ports internally).
- **Volumes** – Named volumes collected for later definition; bind mounts use relative paths normalized to POSIX separators.
- **Depends_on** – Adds conditions (`service_healthy` when the dependency has an HTTP healthcheck, else `service_started`).
- **Networks** – App (`api`) connects to `internal` and `external`; other services use only `internal` to avoid accidental exposure.

### Persistence heuristics

- When adapters decorate types with `HostMountAttribute` and a `ContainerDefaultsAttribute.Image` prefix matches the service image (`StartsWith`), the corresponding container paths are mounted.
- If no attribute match, the exporter falls back to heuristics for common databases and services:
  - Postgres ⇒ `/var/lib/postgresql/data`
  - Mongo ⇒ `/data/db`
  - Redis ⇒ `/data`
  - SQL Server ⇒ `/var/opt/mssql`
  - Weaviate ⇒ `/var/lib/weaviate`
  - Ollama ⇒ `/root/.ollama`
- Bind vs named volumes depends on profile (Local/Staging: `./Data/{service}` bind; Ci: `data_{service}` named; Prod: no automatic mount).

## Edge cases & safeguards

- Reflection failures during host mount discovery are swallowed; exporter continues with heuristic mounts.
- When `healthcheck.HttpEndpoint` is missing, no healthcheck block is emitted; dependents fall back to `service_started` condition.
- If Compose YAML would require quoting (spaces, special chars), `EscapeScalar` ensures valid YAML output.
- Build context detection is best-effort; failures fall back to service `image` only.
- `ParseHostPortFromUrl` defaults ports (80/443) for URLs missing explicit ports to keep TCP fallbacks working.

## Integration points

- Invoked by `Koan.Orchestration.Cli` (`export compose`, `up`) to render `.Koan/compose.yml`.
- Used implicitly by orchestration providers when exporting artifacts in CI pipelines.
- Works alongside Docker/Podman providers: readiness hints and port mappings feed the `EndpointFormatter`.

## Validation notes

- Reviewed `ComposeExporter.cs` on 2025-09-29 (host mount discovery, service rendering, healthcheck construction, network defaults).
- Confirmed `ExporterCapabilities.ReadinessProbes` flag matches emitted healthcheck sections.
- Verified behavior manually via `Koan export compose` (Local profile) and doc build (`docs:build`), ensuring no new warnings.
