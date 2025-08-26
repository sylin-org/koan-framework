# Sora CLI — orchestration usage

This page summarizes how to use the Sora CLI to validate your environment, export Compose, and start/stop apps locally.

## Contract

- Inputs
  - Your repository with Sora services and descriptors (attributes drive endpoints and persistence).
  - Optional flags: profile, engine, base port, verbosity, JSON.
- Outputs
  - Compose v2 artifacts for local runs.
  - Engine actions: up/down/status/logs via Docker or Podman.
- Error modes
  - Engine missing/not running; port conflicts; invalid profile for gated actions.
  - Readiness timeout: containers are up but not all services satisfied the ready criteria within the timeout.
- Success
  - CLI emits a plan, exports Compose, and can start/stop services with clear status.

## Requirements

- Container engine: Docker Desktop or Podman installed and running.
- Windows PowerShell 5.1+ or PowerShell 7+.
- PATH includes the published CLI folder (dist/bin) when invoking as `Sora`.

Notes
- Engine auto-selection is supported; override with `--engine docker|podman`.
- Non‑prod profiles auto-avoid occupied ports; tune with `SORA_PORT_PROBE_MAX`.
- Profile policy (mounts): Local/Staging = bind; CI = named; Prod = none.
- Conflict policy: non‑prod warns; Prod fails on conflicts.
- “Up” is gated for Staging/Prod.
 - Exit codes: 0 = success; 4 = readiness timeout.

## Install the CLI

- Build, publish, add to PATH, and verify in one step:

```pwsh
./scripts/cli-all.ps1
```

- Or publish only (single-file, self-contained) into `dist/bin`:

```pwsh
./scripts/cli-publish.ps1 -Runtime win-x64 -Configuration Release
```

After install, a friendly executable `Sora.exe` is available in `dist/bin` (add that folder to PATH to call `Sora` globally).

## Common scenarios

Validate environment
```pwsh
Sora doctor --json
```

Export Compose (v2)
```pwsh
Sora export compose
```

Start app (Local profile)
```pwsh
Sora up --profile Local
```

If startup is pulling large images on first run, consider raising the timeout:
```pwsh
Sora up --profile Local --timeout 300
```

Stop app and prune data
```pwsh
Sora down --prune-data
```

Check status and view logs
```pwsh
Sora status
Sora logs
```

Engine selection
```pwsh
Sora up --engine docker
```

Base port (auto-avoid starts here in non‑prod)
```pwsh
Sora export compose --base-port 5000
```

Verbosity and explain plan
```pwsh
Sora export compose -v --explain
```

JSON output (where supported)
```pwsh
Sora doctor --json
```

## Deeper walkthrough: start S5.Recs locally

This example uses the `samples/S5.Recs` sample and runs everything through the Sora CLI. Two paths are shown: from code and from prebuilt binaries.

### Path A — From code (recommended during development)

1) Verify engine and environment
```pwsh
Sora doctor --json
```

2) Export Compose (v2) at the repo root
```pwsh
Sora export compose --profile Local
```

3) Bring services up (Local profile)
```pwsh
Sora up --profile Local
```

4) Inspect status and discover live ports
```pwsh
Sora status
```

Tips
- If you need a predictable starting port, set a base: `Sora export compose --base-port 7000`.
- Prefer a specific engine: `Sora up --engine docker`.

5) View logs (follow)
```pwsh
Sora logs
```

6) Tear down (optionally prune volumes)
```pwsh
Sora down --prune-data
```

### Project example: S5.Recs (quick op sheet)

From the repo root, this sequence validates the engine, exports Compose, brings the stack up, checks status and endpoints, then tears down.

```pwsh
# 1) Validate your container engine
Sora doctor --json

# 2) Export Compose (Local profile)
Sora export compose --profile Local

# 3) Up with a generous timeout (first run pulls images)
Sora up --profile Local --timeout 300

# 4) Status and quick checks
Sora status
docker compose -f .sora/compose.yml ps   # or: podman compose -f .sora/compose.yml ps

# API health (PowerShell)
Invoke-RestMethod http://127.0.0.1:5084/health/live | Format-List

# DBs (PowerShell)
Test-NetConnection 127.0.0.1 -Port 5081  # Mongo
Invoke-RestMethod http://127.0.0.1:5082/v1/ | Out-Null  # Weaviate

# 5) Tear down and prune data
Sora down --prune-data
```

Notes
- If `Sora up` exits with code 4 (readiness timeout), services may still be progressing. Run `Sora status` and `Sora logs --tail 200`, or use engine-native `compose ps/logs` as above.
- You can force the engine via `--engine docker|podman` if both are installed.

### Path B — From binaries (no build)

1) Ensure `Sora.exe` is available (publish or download), then run from the repo root:
```pwsh
Sora doctor --json
Sora export compose --profile Local
Sora up --profile Local
Sora status
Sora logs
```

2) Tear down when done:
```pwsh
Sora down --prune-data
```

### Alternative: Use Docker Compose directly

If you prefer to manage containers yourself, export with Sora, then use Docker Compose:
```pwsh
Sora export compose
docker compose -f .sora/compose.yml up -d
docker compose -f .sora/compose.yml ps
docker compose -f .sora/compose.yml logs -f --tail=200
docker compose -f .sora/compose.yml down -v
```

### Alternative: Sample’s local script

The S5 sample also includes a convenience script you can invoke directly:
```pwsh
cd samples/S5.Recs
./start.bat
```

This bypasses Sora’s planner/policies; prefer Sora for consistent profiles, port allocation, and conflict handling.

## Profiles and safety

- Local (default): developer-friendly; bind mounts; auto-avoid ports.
- Staging: bind mounts; “up” gated.
- CI: named volumes (no bind mounts).
- Prod: no bind mounts; strict conflict policy; “up” gated.

## Troubleshooting

- Engine not detected: ensure Docker/Podman is installed and running; try `--engine`.
- Port conflicts: on non‑prod, CLI bumps to free ports; set a different `--base-port` if needed.
- Readiness timeout (exit code 4): services may still be starting. Run `Sora status` and `Sora logs --tail 200` to inspect. You can also use engine-native commands:
  - `docker compose -f .sora/compose.yml ps` and `docker compose -f .sora/compose.yml logs --tail=200`
  - or `podman compose -f .sora/compose.yml ps` / `... logs`
- PATH issues: run `./scripts/cli-install.ps1` to add `dist/bin` to PATH, then open a new shell.
- Compose not found: ensure you ran `Sora export compose` at the repo root; Compose is written under `.sora/`.

## See also

- Reference: `docs/reference/orchestration.md`
- Decisions: conflict policy, mounts and profiles (`/docs/decisions`)
- Engineering front door: `docs/engineering/index.md`
