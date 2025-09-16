---
title: Orchestration CLI and SPI — hosting providers and exporters
description: Engineering contract for Koan DevHost — pluggable hosting providers (Docker/Podman) and artifact exporters (Compose/Helm/ACA), with selection rules, logging, and tests.
---

# Orchestration CLI and SPI — hosting providers and exporters

Purpose
- Define the small, stable SPI for DevHost orchestration.
- Keep DX simple and Windows-first while staying environment-agnostic.
- Allow adapters to declare endpoint and persistence metadata via attributes.

Scope
- CLI behaviors (Koan up/down/status/logs/doctor/export) and profiles.
- Provider SPI (run/stop/inspect stacks) and Exporter SPI (generate artifacts).
- Descriptor model contributed by adapters/recipes.

## Profiles (runtime model)

- Koan_ENV: local (default), ci, staging, prod.
- Local: conservative readiness; optional OTel exporters; seeders allowed.
- CI: ephemeral volumes; deterministic ports; faster fail.
- Staging/Prod: export-only; no DevHost-run deps.

## Descriptors — declare intended services

Contract: IDevServiceDescriptor
- ShouldApply(IConfiguration cfg, IHostEnvironment env) → (bool active, string? reason)
- Describe(Profile profile, IConfiguration cfg) → ServiceSpec

ServiceSpec (shape)
- id: string (stable, dns-safe)
- image: string (name:tag)
- env: IDictionary<string,string?> (values redacted in logs for secret-like keys)
- ports: IEnumerable<(int host, int container)>
- volumes: IEnumerable<(string source, string target, bool named)>
- health: { endpoint (http), interval, timeout, retries }
- depends_on: IEnumerable<string> (ids)

Guidance
- Gate on actual config/capability (do not start containers on package presence alone).
- Prefer named volumes over bind mounts by default.
- SQLite/file-backed providers: no container.

## Hosting providers — run/inspect stacks

Contract: IHostingProvider
- Id, Priority
- IsAvailableAsync(CancellationToken) → (bool ok, string? reason)
- Up(string composePath, Profile profile, RunOptions opts, CancellationToken)
- Down(string composePath, StopOptions opts, CancellationToken)
- Logs(LogsOptions opts, CancellationToken) → stream
- Status(StatusOptions opts, CancellationToken) → ProviderStatus
- LivePorts(CancellationToken) → IReadOnlyList<PortBinding>
- EngineInfo() → { name, version, endpoint }

Notes
- Windows-first: Docker Desktop via npipe (preferred), Podman Desktop supported.
- Implementation MAY use CLIs (docker/podman) for portability; capture stderr/stdout for --trace.

## Exporters — generate artifacts

Contract: IArtifactExporter
- Id
- Supports(string format) // e.g., "compose", "helm", "aca"
- GenerateAsync(Plan plan, Profile profile, string outPath, CancellationToken)
- Capabilities: { secretsRefOnly, readinessProbes, tlsHints }

Composer (v1)
- Emit Compose v2 with: services, env (redacted in logs), ports, named volumes, healthcheck, depends_on: service_healthy.

## Planner and selection rules

Activation order
1) Explicit config under Koan:* enables a dependency.
2) Active recipes that see configured deps enable orchestration for those deps.
3) Package presence alone is only a hint; require minimal config.

Provider selection
- Default precedence on Windows: Docker → Podman.
- Override with --engine. Configurable via Koan:Orchestration:PreferredProviders.

Explainability
- --explain prints the plan (active/inactive with reasons), selected provider, ports, readiness gates.
- --dry-run renders artifacts and validates without running.

## Logging, verbosity, and safety

Verbosity flags
- Default = Info; -v = Verbose; -vv = Debug; --trace = Trace; --quiet.
- --json for status/doctor outputs; disable colors when not TTY.

Event IDs (reserve range 48000–48049)
- 48000 PlanBuildStart, 48001 PlanBuildOk, 48002 PlanBuildWarn, 48003 PlanBuildError
- 48010 ProviderSelectStart, 48011 ProviderSelected, 48012 ProviderUnavailable
- 48020 UpStart, 48021 UpReady, 48022 ReadinessTimeout
- 48030 DownStart, 48031 DownOk
- 48040 ExportStart, 48041 ExportOk, 48042 ExportError

Redaction
- Redact values when key contains: token, secret, password, pwd, key, connectionstring.

## Adapter authoring checklist

- Provide IDevServiceDescriptor in your adapter/recipe package.
- Implement ShouldApply by checking options/services; avoid raw package presence.
- Emit conservative ServiceSpec (named volumes; host ports only when necessary).
- Add health endpoints and readiness timeouts appropriate to the service.
- Include minimal docs: required config keys; example env; readiness characteristics.

## Testing checklist

- Descriptor tests: activation matrix (enabled/disabled reasons), ServiceSpec shape.
- Exporter golden tests: stable Compose output; redaction verified.
- Provider tests: availability detection mocked; command lines formed correctly; error surfaces mapped to exit codes.
- Port parsing: LivePorts parses compose ps output into PortBinding records (host/container/protocol/address).
- E2E (Windows): docker provider + compose happy path; doctor failure cases.

## Breaking change policy

- Keep SPI source-compatible; add new optional members behind capabilities.
- New exporters/providers MUST not change default behavior on existing commands.
- Document changes here and reference ADR ARCH-0047.

## References

- ADR: decisions/ARCH-0047-orchestration-hosting-and-exporters-as-pluggable-adapters.md
- ADR: decisions/ARCH-0048-endpoint-resolution-and-persistence-mounts.md
- Reference: ../reference/orchestration.md
- Principles: ../architecture/principles.md
