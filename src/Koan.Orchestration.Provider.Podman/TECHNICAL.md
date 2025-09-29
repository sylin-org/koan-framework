---
uid: reference.modules.koan.orchestration.provider.podman
title: Koan.Orchestration.Provider.Podman – Technical Reference
description: Podman integration for Koan orchestration providers, covering availability checks, compose lifecycles, and live port discovery.
since: 0.6.3
packages: [Sylin.Koan.Orchestration.Provider.Podman]
source: src/Koan.Orchestration.Provider.Podman/
validation:
  date: 2025-09-29
  status: verified
---

## Contract

- Implement the `IHostingProvider` contract for Podman so Koan tooling (CLI, orchestration hosts) can manage Compose-based environments when Docker is unavailable or deprioritized.
- Execute Podman CLI commands for `up`, `down`, `status`, `logs`, and availability without taking dependencies on Podman REST clients.
- Parse Podman `compose ps --format json` output into Koan `ProviderStatus` and `PortBinding` records, keeping readiness logic aligned with Docker parity.
- Surface engine metadata (version, default connection) and degrade gracefully when the Podman CLI is missing or unreachable.

## Core components

| Concern              | Types                                                           | Notes                                                                                                                                                                 |
| -------------------- | --------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Provider contract    | `PodmanProvider : IHostingProvider`                             | `Id="podman"`, `Priority=50` (Docker retains precedence on Windows). Implements the full verb set used by the CLI.                                                    |
| Process runner       | `Run`, `Stream`                                                 | Invoke `podman` commands with redirected output; shared by availability, status, logs, and lifecycle operations.                                                      |
| Readiness polling    | `Up` + `ComposeStatusForFile`                                   | After `podman compose up`, repeatedly calls `podman compose ps --format json` until all services report `running` and (if present) `healthy`, or the timeout expires. |
| Status/ports parsing | `ParseComposePsJson`, `ParseComposePsPorts`, `ParsePortsString` | Translate JSON arrays of service entries and `Ports` strings (e.g., `0.0.0.0:8080->80/tcp`) into structured tuples.                                                   |
| Engine metadata      | `GetVersionSafe`, `GetEndpointSafe`, `EngineInfo()`             | Extract version data from `podman version --format json` and default connection name from `podman system connection default`.                                         |

## Provider workflow

1. **Availability** – `IsAvailableAsync` runs `podman version --format json`. Exit code `0` indicates success; stderr or thrown exceptions are mapped into the failure reason for diagnostics (e.g., `doctor --engine podman --json`).
2. **Up** – Issues `podman compose -f <path> up` (with `-d` when `RunOptions.Detach` is true). A readiness loop follows:
   - `ComposeStatusForFile` executes `podman compose ps --format json` and parses the result into `(service, state, health)` tuples.
   - When all entries are `running` and either lack health data or report `healthy`, the loop exits.
   - A `RunOptions.ReadinessTimeout` (default 60 seconds in the CLI) cancels the loop and throws `TimeoutException`.
3. **Down** – Calls `podman compose -f <path> down` plus `-v` when `StopOptions.RemoveVolumes` is set.
4. **Status** – Reuses `podman compose ps --format json`, collects the service tuples, and reports the Podman version (client or server) via `ProviderStatus.EngineVersion`.
5. **Logs** – Streams `podman compose logs` with optional `--follow`, `--tail`, and `--since`; the helper yields log lines through `IAsyncEnumerable<string>`.
6. **Live ports** – Reuses compose status output, translating the `Ports` array/string into `PortBinding` instances (service, host port, container port, protocol, optional host address).

## Error handling & resilience

- Podman often writes informational messages to stderr; lifecycle methods treat non-zero exit codes as failure but otherwise ignore stderr content.
- JSON parsing is wrapped with `try/catch`; malformed entries are skipped so monitoring continues even when a single record is bad.
- When `podman` is missing or cannot execute, `Status`/`LivePorts` return empty results while `IsAvailableAsync` delivers a helpful reason string, preserving CLI UX.
- `EngineInfo()` suppresses exceptions and returns empty strings when the CLI calls fail, ensuring provider enumeration never crashes.

## Edge cases

- **JSON arrays only** – Current Podman releases emit an array for `compose ps --format json`; NDJSON is not expected. Parsers log and skip data if the shape changes.
- **Rootless sockets** – Availability failures typically include `permission denied`; callers can surface this via `Reason` and instruct users to adjust socket permissions.
- **IPv6 bindings** – Strings like `:::8443->8443/tcp` are parsed correctly, preserving IPv6 addresses for downstream formatters.
- **Timeouts** – Both user cancellation and readiness deadlines are handled via linked cancellation tokens so callers receive deterministic `TimeoutException` messages.
- **Detached vs attached** – `RunOptions.Detach=false` leaves compose in attached mode while readiness polling still waits for containers to become healthy.

## Integration points

- Consumed by `Koan.Orchestration.Cli` inside `SelectProviderAsync`. Provider order defaults to Docker first, Podman second, but can be overridden with `Koan_ORCHESTRATION_PREFERRED_PROVIDERS`.
- `EngineInfo` feeds CLI diagnostics (`provider: podman | engine: Podman <version>`).
- `LivePorts` results are formatted by `EndpointFormatter` to supply endpoint hints in CLI `status` output.

## Validation notes

- Reviewed `PodmanProvider.cs` on 2025-09-29 (availability probe, compose lifecycle, port parsing, log streaming, metadata helpers).
- Confirmed provider priority is lower than Docker, matching CLI defaults.
- Ran `docs:build` following documentation updates; no new warnings introduced for this module.
