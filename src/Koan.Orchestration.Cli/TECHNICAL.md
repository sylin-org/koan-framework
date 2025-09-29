---
uid: reference.modules.koan.orchestration.cli
title: Koan.Orchestration.Cli – Technical Reference
description: Command-line orchestration tooling for planning, exporting, and supervising Koan module environments.
since: 0.6.3
packages: [Sylin.Koan.Orchestration.Cli]
source: src/Koan.Orchestration.Cli/
validation:
  date: 2025-09-29
  status: verified
---

## Contract

- Provide a DX-first CLI (`Koan`) that discovers Koan services, builds orchestration plans, and executes compose providers.
- Support workflows for describing environments (`inspect`), exporting compose bundles, running/stopping stacks, checking health, and tailing logs.
- Enforce safe defaults: prod profiles never start containers, host port conflicts are detected and handled deterministically, and outputs redact sensitive values.
- Persist local planning decisions (launch manifest, overrides) so repeated runs are stable across profiles and hosts.

## Command surface

| Command                  | Purpose                                                     | Key inputs                                                                                                                            | Output / side-effects                                                                                                                           |
| ------------------------ | ----------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| `inspect [--json]`       | Detect the current project and show discovery summary.      | `--profile`, `--engine`, `--base-port`, `--port`, `--expose-internals`, `Koan_NO_INSPECT`.                                            | Project metadata, detected services, provider availability. Returns JSON when `--json` is present.                                              |
| `export compose [--out]` | Render Docker Compose bundles from the active plan.         | `--profile`, `--base-port`, `--port`, `--expose-internals`, `--no-launch-manifest`.                                                   | Writes compose YAML (default `.Koan/compose.yml`), prints port conflicts.                                                                       |
| `doctor`                 | Check provider availability and report resolve order.       | `--engine`, `--json`, `Koan_ORCHESTRATION_PREFERRED_PROVIDERS`.                                                                       | Provider readiness, engine version, JSON payloads. Exit code `3` when no providers are available.                                               |
| `up`                     | Generate compose and start the stack for non-prod profiles. | `--file`, `--profile`, `--engine`, `--timeout`, `--base-port`, `--port`, `--expose-internals`, `--no-launch-manifest`, `--conflicts`. | Writes compose file, starts provider, prints diagnostics, optionally skips services with conflicting host ports. Disabled for `Staging`/`Prod`. |
| `down`                   | Stop containers and optionally prune volumes.               | `--file`, `--engine`, `--volumes`/`--prune-data`.                                                                                     | Delegates to selected provider; exit code `0` on success.                                                                                       |
| `status`                 | Show provider status alongside plan-derived endpoint hints. | `--json`, `--engine`, `--profile`, `--base-port`, `--port`, `--expose-internals`, `--no-launch-manifest`.                             | Prints live container state, host endpoints, and conflicts.                                                                                     |
| `logs`                   | Stream provider logs with filters.                          | `--engine`, `--service`, `--follow`, `--tail`, `--since`.                                                                             | Streams logs through provider adapters.                                                                                                         |

### Shared options

- Verbosity flags `-v`, `-vv`, `--trace`, `--quiet` combine to control `up` diagnostics.
- Profiles resolve as `--profile` > `Koan_ENV` environment variable > `Local`.
- Provider precedence is controlled with `Koan_ORCHESTRATION_PREFERRED_PROVIDERS` (comma-separated). When none are available, Docker is returned as a fallback so dry-runs continue to work.

## Planning pipeline

1. `Planner.Build(profile)` orchestrates discovery:
   - `TryLoadDescriptor` loads `Koan.orchestration.(yml|yaml|json)` when present (highest precedence).
   - `ProjectDependencyAnalyzer.DiscoverDraft(profile)` reads the generated manifest `__KoanOrchestrationManifest` to capture service requirements, adapters, and defaults. Reflection-based fallbacks have been removed.
   - `Overrides.Load()` merges JSON overrides from `.Koan/overrides.json` or `overrides.Koan.json` after discovery.
   - When no sources are found, an empty plan is returned (no demo plan fallback).
2. The resulting `Plan` is transformed with profile-specific adjustments:
   - `AssignAppPublicPort` chooses the app host port using CLI flags, launch manifest (`.Koan/manifest.json`), code defaults, or deterministic hashing. Internals are unpublished unless `--expose-internals` or the overrides manifest request otherwise.
   - `PortAllocator.AutoAvoidPorts` probes host ports (guard controlled by `Koan_PORT_PROBE_MAX`; default 200 probes) and increments to avoid conflicts for non-prod plans.
   - `Planner.ApplyPortConflictSkip` (non-prod) optionally removes services whose host ports are already bound, adjusts `api` env tokens to `localhost`, and drops `depends_on` edges. `--conflicts fail` elevates conflicts to errors; otherwise warnings are emitted and skips recorded for UX.

### Launch manifest persistence

`LaunchManifest.Save` writes `.Koan/manifest.json`, preserving:

- App identity, friendly name, default/assigned public port.
- Per-service allocations (`allocations[serviceId].assignedPublicPort`).
- Options (last profile, provider hint, expose-internals state).

Existing manifests are backed up with a timestamped `.old` suffix. `.Koan/.gitignore` is maintained automatically to keep local files out of source control.

## Provider selection and execution

- Providers implement `IHostingProvider` (Docker/Podman in-box). `SelectProviderAsync` orders providers with `OrderByPreference` and returns the first available instance (via `IsAvailableAsync`). When none are reachable, Docker is returned to allow dry-run flows.
- `DoctorAsync`, `StatusAsync`, `UpAsync`, and `DownAsync` call provider-specific helpers for engine metadata, container lifecycle, and log streaming.
- Provider `RunOptions` set `Detach=true`. `UpAsync` respects a `--timeout` (default 60 seconds) and surfaces readiness failures with exit code `4`.
- `StatusAsync` merges live provider ports (`provider.LivePorts`) with plan-derived hints using `EndpointFormatter.GetPlanHint`, enabling users to copy stable URLs regardless of provider state.

## Endpoint formatting

`RegisterSchemeResolver` inspects loaded assemblies for `DefaultEndpointAttribute` annotations to map `(imagePrefix, containerPort)` pairs to schemes and optional URI patterns. `EndpointFormatter` uses this resolver to:

- Render live endpoints (`tcp://`, `http://`, or pattern-based URIs) with host redaction rules.
- Provide plan hints (`scheme://localhost:{port}`) that align with discovery defaults.
- Default to `tcp` when no resolver is present, removing older heuristic fallbacks (per ADR ARCH-0049).

## Project discovery helpers

`ProjectDependencyAnalyzer` exposes:

- `DiscoverServicesFromManifest`/`DiscoverManifestServiceDetails` for `inspect --json` payloads.
- `ManifestIdDuplicates` and `ManifestAuthProviders` (when present) to highlight manifest issues.
- `DiscoverKoanReferences` to report Koan assemblies referenced by the project for diagnostics.

These helpers rely on `MetadataLoadContext` with a probe path that covers the current project, repo `src/**/bin` directories, and BCL assemblies. All errors are swallowed to keep inspection non-blocking.

## Overrides and profiles

- Overrides (`Overrides.Service`) allow swapping images, merging environment variables, adding volumes, and replacing container ports.
- Mode switching (`Overrides.Mode`) controls whether endpoint token substitution prefers local or container defaults.
- Profiles affect behavior:
  - `Local`/`Ci`: full stack allowed; auto port avoidance, skipping, and launch manifest persistence enabled.
  - `Staging`/`Prod`: `up` is disabled. Only `export`/`doctor`/`status` run; compose bundles are still generated.

## Edge cases & safeguards

- No project detected (`inspect`): returns exit code 0 but marks `detected=false` in JSON. Human mode prints a helpful message and exits early.
- Missing compose or manifest files: discovery returns an empty plan; commands that require services handle the empty set gracefully.
- Host port conflicts in prod: cause exit code `4` with `port conflicts detected (prod)` message.
- Provider probes throw exceptions: `DoctorAsync` propagates availability warnings but continues with remaining providers.
- Overrides with invalid JSON: ignored silently to keep CLI resilient during edits.
- `Koan_NO_INSPECT=1` suppresses auto-inspection post-command (useful in scripts).

## Validation notes

- Reviewed `Program.cs`, `Planning/Planner.cs`, `Planning/PortAllocator.cs`, `Planning/LaunchManifest.cs`, `Planning/ProjectDependencyAnalyzer.cs`, `Infrastructure/Constants.cs`, and `Formatting/EndpointFormatter.cs` on 2025-09-29.
- Verified port avoidance (`PortAllocator.AutoAvoidPorts`) uses TCP probes with a configurable guard and does not reserve port `0`.
- Confirmed `up` disables execution for `Staging`/`Prod` profiles and that conflict skipping redacts adapter env tokens.
- Validated env variable names match implementation: `Koan_ENV`, `Koan_ORCHESTRATION_PREFERRED_PROVIDERS`, `Koan_PORT_PROBE_MAX`, `Koan_NO_INSPECT`.
- `docs:build` executed post-update; build succeeds with pre-existing warnings unrelated to this module.
