---
uid: reference.modules.koan.orchestration.provider.docker
title: Koan.Orchestration.Provider.Docker – Technical Reference
description: Docker engine integration for Koan orchestration flows, covering availability checks, compose lifecycle orchestration, and port discovery.
since: 0.6.3
packages: [Sylin.Koan.Orchestration.Provider.Docker]
source: src/Koan.Orchestration.Provider.Docker/
validation:
  date: 2025-09-29
  status: verified
---

## Contract

- Surface a Docker-backed `IHostingProvider` that the Koan CLI and orchestration runtimes can use for compose lifecycles.
- Proxy `docker compose` commands for `up`, `down`, `logs`, `status`, and live port discovery while providing deterministic readiness gating.
- Report engine metadata (version, context) and availability diagnostics so provider election can degrade gracefully.
- Parse JSON/NDJSON responses from the Docker CLI into `ProviderStatus` and `PortBinding` records without depending on Docker SDK packages.

## Core components

| Concern           | Types                                                                  | Notes                                                                                                                                                                      |
| ----------------- | ---------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Provider contract | `DockerProvider : IHostingProvider`                                    | Implements provider id, priority (100), and orchestration verbs. Priority keeps Docker first on Windows installs.                                                          |
| Process runner    | `Run`, `Stream` helpers                                                | Thin wrappers over `System.Diagnostics.Process` to invoke `docker` CLI with redirected IO, shared across verbs.                                                            |
| Readiness polling | `Up` + `ComposeStatusForFile`                                          | After `docker compose up`, polls `docker compose ps --format json` until every service is running and healthy (when health information is exposed) or the timeout elapses. |
| Status rendering  | `ParseComposePsJson`                                                   | Accepts both JSON arrays and NDJSON output, normalizes empty health to `null`, and returns `(service, state, health)` tuples.                                              |
| Port extraction   | `ParseComposePsPorts`, `ExtractPortsFromJsonArray`, `ParsePortsString` | Convert compose `Ports` strings into strongly-typed `PortBinding` instances (service, host, container, protocol, address).                                                 |
| Engine metadata   | `EngineInfo` helpers                                                   | Lazily fetch `docker version` (server) and `docker context show`, suppressing exceptions to keep telemetry non-blocking.                                                   |

## Provider workflow

1. **Availability check** – `IsAvailableAsync` runs `docker version --format '{{.Server.Version}}'`. Non-zero exit codes bubble the stderr message; exceptions are converted into the failure reason for UX (`doctor --json`).
2. **Up** – Executes `docker compose -f <path> up -d` (depending on `RunOptions.Detach`). Once the compose command returns, a readiness loop:
   - Calls `ComposeStatusForFile` (same compose file path) which, in turn, executes `docker compose ps --format json`.
   - The result is parsed into service tuples. When **all** services are `running` and either lack health checks or report `healthy`, the loop completes.
   - A `ReadinessTimeout` (default 60 seconds when driven by the CLI) cancels the loop and raises `TimeoutException`.
3. **Down** – Invokes `docker compose down` with `-v` when `StopOptions.RemoveVolumes` is set.
4. **Status** – Runs `docker compose ps --format json` and reuses the parser. Availability is rechecked so the returned `ProviderStatus.EngineVersion` is trustworthy even when Compose has no services yet.
5. **Logs** – Streams `docker compose logs` with optional `--follow`, `--tail`, and `--since` arguments. Output is surfaced line-by-line through an `IAsyncEnumerable<string>`.
6. **Live ports** – Reuses compose status output, extracting host/container port tuples from CLI-formatted strings (`0.0.0.0:8080->80/tcp`, `:::5432->5432/tcp`). IPv6 literals are bracketed when rendered by consumers.

## Error handling & resilience

- Docker CLI writes informational messages to stderr even on success; `Up` ignores stderr unless the exit code is non-zero.
- JSON parsing is wrapped in `try/catch` blocks; malformed records are skipped so a single bad line does not block readiness or status reporting.
- When the CLI is absent or refuses connections, `Status` and `LivePorts` degrade to empty payloads while `IsAvailableAsync` conveys the diagnostic message.
- `EngineInfo()` executes synchronously and suppresses exceptions to ensure provider enumeration in the CLI never fails.

## Edge cases

- **NDJSON output** – Some Docker versions emit newline-delimited JSON for `compose ps`; parsers handle both array and NDJSON formats.
- **IPv6 bindings** – Outputs such as `:::8080->80/tcp` are parsed correctly, and downstream formatters bracket IPv6 hosts.
- **Detached vs attached** – `RunOptions.Detach=false` keeps Compose in attached mode; readiness polling still runs until the CLI exits or services become healthy.
- **Timeouts** – Readiness relies on `CancellationTokenSource.CancelAfter`; both user cancellation and timeouts surface a `TimeoutException` to callers.
- **No services** – When `compose ps` returns an empty set (cold start), readiness waits briefly (`Task.Delay`) before retrying.

## Integration points

- Referenced by `Koan.Orchestration.Cli` via provider selection (`SelectProviderAsync`), powering `doctor`, `up`, `down`, `status`, and `logs` commands.
- `EngineInfo` contributes to CLI diagnostics (`provider: docker | engine: Docker <version>`).
- `LivePorts` feeds endpoint hint rendering with `EndpointFormatter` from the CLI project.

## Validation notes

- Reviewed `DockerProvider.cs` end-to-end on 2025-09-29 (availability probe, readiness loop, port parsing, log streaming).
- Removed obsolete `JsonElementArrayHelper.cs` placeholder to maintain clean module surface.
- Confirmed doc build (`docs:build`) after documentation updates; no module-specific warnings introduced.
