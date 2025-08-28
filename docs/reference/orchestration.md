---
title: Orchestration — DevHost, hosting providers, and exporters
description: How to use Sora's DevHost CLI to bring up local dependencies and export portable artifacts with Docker/Podman providers and Compose/Helm/ACA exporters.
---

# Orchestration — DevHost, hosting providers, and exporters

Contract (at a glance)
- Inputs: generated orchestration manifest from referenced assemblies, optional descriptor overrides, profile (SORA_ENV).
- Outputs: a Plan of services (containers) and generated artifacts (Compose v2 now; Helm/ACA later).
- Error modes: no engine available, port conflicts, invalid config, readiness timeout → non-zero exit with guidance.
- Success: `sora up` brings deps to ready state; `sora status` healthy; artifacts generated predictably.

## Profiles

Set SORA_ENV to choose profile: local (default), ci, staging, prod.
- local: conservative timeouts, optional OTel exporters, seeders allowed; bind mounts enabled by default.
- ci: ephemeral named volumes (no bind mounts), deterministic ports with auto-avoid, faster fail.
- staging: export-only (DevHost does not run deps); artifacts still inject bind mounts for persistence by default.
- prod: export-only; no automatic mount injection (artifacts omit persistence mounts by default).

## CLI usage

- sora up [--engine docker|podman] [--profile local|ci|staging|prod] [--timeout <seconds>] [--base-port <n>] [--port <n>] [--expose-internals] [--no-launch-manifest] [--conflicts warn|fail] [-v|-vv|--trace|--quiet] [--explain|--dry-run]
- sora down [--engine docker|podman] [--volumes|--prune-data]
- sora status [--engine docker|podman] [--json] [--profile local|ci|staging|prod] [--base-port <n>] [--no-launch-manifest]
- sora logs [--engine docker|podman] [--service <name>] [--since 10m] [--follow] [--tail <n>]
- sora doctor [--engine docker|podman] [--json]
- sora export compose [--profile local|ci|staging|prod] [--base-port <n>] [--port <n>] [--expose-internals] [--no-launch-manifest]  # Helm/ACA vNext

 Notes
- Writes `.sora/compose.yml` by default; safe for Git ignore.
- Profile resolution precedence: `--profile` > `SORA_ENV` environment variable > `local`.
- Heavy AI (e.g., Ollama) is opt-in via profile/flag/config; SQLite is not containerized.
- Readiness: `sora up` waits for all services to be running (and healthy when a health check is present) up to `--timeout` seconds.
- Ports auto-avoid conflicts in non-prod; `--base-port` offsets host ports by a fixed amount. `sora status` prints endpoint hints and flags conflicting ports when detected.
 - Port conflicts policy: `prod` always fails fast on conflicts; non-prod defaults to warn but can be forced to fail with `--conflicts fail`.
 - Auto-avoid tuning: set `SORA_PORT_PROBE_MAX` to control the max number of upward port probes (default: 200).
 - App public port precedence: `--port` > LaunchManifest.Allocations[serviceId] > LaunchManifest.App.AssignedPublicPort > app default (SoraApp.DefaultPublicPort) > deterministic fallback (30000–50000). The chosen source is surfaced in Context Card, Up (explain), Status, and Inspect.
 - Launch Manifest: `.sora/manifest.json` persists dev-time choices with backup-on-change. Disable reads/writes with `--no-launch-manifest`.
 - Networks: compose defines `sora_internal` and `sora_external`. Adapters run on internal only; the app joins both. Ports are published only when host > 0. Use `--expose-internals` to publish adapter ports too.

### Readiness semantics and timeouts

- Providers poll container state using the specific exported compose file (`compose -f .sora/compose.yml ps`) to avoid cross-project noise.
- A service is considered ready when its state is Running; when a healthcheck exists, it must report Healthy.
- If the timeout elapses before all services meet the criteria, `sora up` exits with a non-zero code and prints a concise message. In non-critical local scenarios, containers may still be progressing toward readiness; use Status/Logs to inspect.

Exit codes (subset)
- 0: Success
- 4: Readiness timeout (containers started but didn’t meet the ready condition within the timeout)

On-timeout diagnostics (suggested)
- `sora status` — shows provider/engine and service states
- `sora logs --tail 200` — recent logs across services
- `docker compose -f .sora/compose.yml ps` and `docker compose -f .sora/compose.yml logs --tail=200` (or `podman compose ...`) for engine-native views

## Hosting providers (adapters)

Providers implement a simple contract to run/inspect stacks.
- Docker provider (Windows-first): detects Docker Desktop (npipe) and runs Compose v2.
- Podman provider: detects Podman Desktop and runs `podman compose` or uses the Docker API shim if configured.

Selection
- Auto-pick available provider (Windows default: Docker → Podman). Override with `--engine`.

## Exporters (adapters)

- Compose (v1): emits a conservative Compose v2 file (named volumes; healthchecks; ports; env; depends_on:service_healthy). Values like ${VAR} are preserved unquoted so Compose resolves from the environment/.env; literal values are safely quoted to avoid YAML coercion. When adapters declare HostMountAttribute(containerPath), the exporter injects persistence mounts by profile:
	- local/staging: bind mounts `./Data/{serviceId}:{containerPath}` (unless already present)
	- ci: named volumes `data_{serviceId}:{containerPath}` (declared under top-level volumes)
	- prod: no automatic mounts are injected

Authoring adapters
- See `docs/engineering/orchestration-adapter-authoring.md` for how to declare `DefaultEndpointAttribute` and `HostMountAttribute` in your adapter.
- Helm (vNext): generates a minimal chart with probes and env/secrets references.
- Azure Container Apps/Bicep (vNext): emits a baseline Bicep template for Container Apps with diagnostics.

Secrets
- Exporters reference external secrets by name; they do not create or embed secrets.

## Discovery rules (manifest-first)

- Manifest-only: the CLI and planners load `Sora.Orchestration.__SoraOrchestrationManifest.Json` from built assemblies and build plans from its unified fields (kind, codes, image/tag, ports, provides/consumes, capabilities). No compose/csproj/image-name scraping.
- Descriptor first: if `sora.orchestration.yml`|`yaml`|`json` exists at the repo root, it defines the plan or applies overrides (image/tag/ports) with simple pass-through shapes.
- Recipes: when active and configured, contribute explicit services; still flow through the manifest/plan pipeline.
- Packages alone do not trigger deps. Adapters must declare `[SoraService]` to appear in the manifest.

Inspect
- `sora inspect` reports app ids/ports when an app block is present in the manifest.
- Duplicate service ids across manifests are surfaced in JSON (`duplicates`) and summarized in human output.

## Verbosity and safety

- -v/-vv/--trace/--quiet and `--explain` to see the plan without side effects; `--dry-run` validates and renders without running.
- Redacts sensitive values (token/secret/password/connectionstring) in human-readable outputs (doctor/logs). JSON payloads remain unmodified.

## Examples

Local dev (Docker Desktop preferred)
1) Configure Postgres: set `Sora:Data:Provider=postgres` and a connection string.
2) Run `sora up -v` → containers start; readiness waits; status prints endpoints (live bind address, host port → container port, protocol).
3) Run `sora status --json` for machine-readable health.
4) Run `sora down` to stop (volumes preserved). Use `--volumes` (alias: `--prune-data`) to remove data volumes.

Export compose only
- `sora export compose --profile ci` → writes `.sora/compose.yml` tuned for CI (ephemeral volumes, deterministic ports).

### Project example: S5.Recs (CLI from repo root)

```pwsh
Sora doctor --json
Sora export compose --profile Local
Sora up --profile Local --timeout 300
Sora status
docker compose -f .sora/compose.yml ps   # or: podman compose -f .sora/compose.yml ps
# Quick health
Invoke-RestMethod http://127.0.0.1:5084/health/live | Format-List
Sora down --prune-data
```

## References

- ARCH-0047 — Orchestration: hosting providers and exporters as adapters
- ARCH-0048 — Endpoint resolution and persistence mounts
- Architecture Principles — docs/architecture/principles.md
- Recipes — docs/reference/recipes.md
