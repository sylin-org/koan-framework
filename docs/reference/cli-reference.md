# Koan CLI — orchestration usage

This page summarizes how to use the Koan CLI to validate your environment, export Compose, inspect a project, and start/stop apps locally.

## Contract

- Inputs
  - Your repository with Koan services and descriptors (attributes drive endpoints and persistence).
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
- PATH includes the published CLI folder (dist/bin) when invoking as `Koan`.

Notes
- Engine auto-selection is supported; override with `--engine docker|podman`.
- Non‑prod profiles auto-avoid occupied ports; tune with `Koan_PORT_PROBE_MAX`.
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

After install, a friendly executable `Koan.exe` is available in `dist/bin` (add that folder to PATH to call `Koan` globally).

## Common scenarios

No-args context card (in a Koan-compatible project folder)
```pwsh
Koan
# First line prints: "Use -h for help"
# Then a concise Context Card with:
# - Providers (Docker/Podman availability)
# - Project and key files (compose, csproj)
# - Services (planned ports/health)
# - App port selection with source (flag | launch-alloc | launch-app | code-default | deterministic)
# - Networks (external + internal); adapters on internal; app on both
# - Compose services (discovered from compose.yml)
# - Dependencies (database, vector, AI, auth)
# - Suggested one-liners
```

Inspect (explicit)
```pwsh
Koan inspect
Koan inspect --json
```

Inspect JSON (contract — selected fields)
- providers: [{ name, ok, version }]
- project: { name, path, files: [] }
- services: [{ id, image, ports: ["HOST:CONTAINER"], health: true|false }]
- app: {
    ids: ["api", ...],
    ports: [{ id: "api", host: 5084, container: 8080, source: "flag|launch-alloc|launch-app|code-default|deterministic|unknown" }],
    networks: { external: "Koan_external", internal: "Koan_internal" }
  }
- composeServices: ["api", "db", ...]
- dependencies: { database?: "mongodb|postgres|redis|sqlite|none", vector?: "weaviate|qdrant|pinecone|none", ai?: "ollama|openai|none", auth?: "enabled|disabled" }

Notes
- Dependency detection prefers concrete configuration over heuristics: compose.yml > csproj references > planned service images.
- In samples (e.g., S5.Recs), you’ll typically see db: mongodb, vector: weaviate, ai: ollama, auth: enabled.

Validate environment
```pwsh
Koan doctor --json
```

Export Compose (v2)
```pwsh
Koan export compose
# Options:
#   --profile Local|CI|Staging|Prod
#   --base-port <n>
#   --port <n>                 # force app public port (overrides persisted/default)
#   --expose-internals         # publish internal services on host ports too
#   --no-launch-manifest       # don’t read/write .Koan/manifest.json
```

Start app (Local profile)
```pwsh
Koan up --profile Local
# Optional overrides
#   --port 5084               # force app public port for this run
#   --expose-internals        # publish non-app services on host
#   --no-launch-manifest      # don’t persist/read allocations for this command
```

If startup is pulling large images on first run, consider raising the timeout:
```pwsh
Koan up --profile Local --timeout 300
```

Stop app and prune data
```pwsh
Koan down --prune-data
```

Check status and view logs
```pwsh
Koan status
Koan logs
```

Engine selection
```pwsh
Koan up --engine docker
```

Base port (auto-avoid starts here in non‑prod)
```pwsh
Koan export compose --base-port 5000
```

Verbosity and explain plan
```pwsh
Koan export compose -v --explain
```

JSON output (where supported)
```pwsh
Koan doctor --json
Koan inspect --json
```

## Overrides (.Koan/overrides.json)

You can customize images, env, volumes, and container ports per service without changing code.

- File candidates (first match wins):
  - `.Koan/overrides.json`
  - `overrides.Koan.json` (repo root)

Schema (subset)
```json
{
  "Mode": "Local", // or "Container" (default)
  "Services": {
    "mongo": {
      "Image": "mongo:7",
      "Env": { "MONGO_INITDB_ROOT_USERNAME": "root" },
      "Volumes": [ "./Data/mongo:/data/db" ],
      "Ports": [ 27018 ]
    }
  }
}
```

Behavior
- Mode: When `Local`, token substitution for app env prefers local endpoint hints from adapters (scheme/host/port). Otherwise, container endpoints are used.
- Services:
  - `Image` replaces the discovered image/tag when provided.
  - `Env` is merged, overriding existing keys.
  - `Volumes` are appended (profile policy controls bind vs named vs none).
  - `Ports` replaces the service’s container ports. Exporters map host:container as `p:p` per profile/flags; app/internal publishing still follows exposure rules (use `--expose-internals` to publish non‑app services).

## Launch Manifest (.Koan/manifest.json)

Koan persists safe, dev-time choices (like the app’s public port) in `.Koan/manifest.json`.
- Purpose: Keep your chosen app port stable across runs without hardcoding it in code.
- Precedence for app public port:
  1) CLI `--port <n>`
  2) LaunchManifest.Allocations[serviceId]
  3) LaunchManifest.App.AssignedPublicPort
  4) App default (from code or attribute)
  5) Deterministic fallback (seeded into 30000–50000)
- Source is surfaced in Context Card, Status, Up (explain), and Inspect (human + JSON).
- Backup-on-change: updates create a timestamped backup under `.Koan/`.
- Opt out anytime with `--no-launch-manifest` (applies to export/up/status/inspect).

## Networks and exposure

- Two networks are always defined: `Koan_internal` and `Koan_external`.
- Adapters (e.g., databases, vector, AI providers) attach to the internal network only by default.
- The app attaches to both networks; ports are published on the host only when the selected host port > 0.
- Use `--expose-internals` to also publish internal services on host ports during local runs.

## Deeper walkthrough: start S5.Recs locally

This example uses the `samples/S5.Recs` sample and runs everything through the Koan CLI. Two paths are shown: from code and from prebuilt binaries.

### Path A — From code (recommended during development)

1) Verify engine and environment
```pwsh
Koan doctor --json
```

2) Export Compose (v2) at the repo root
```pwsh
Koan export compose --profile Local
```

3) Bring services up (Local profile)
```pwsh
Koan up --profile Local
```

4) Inspect status and discover live ports
```pwsh
Koan status
```

Tips
- If you need a predictable starting port, set a base: `Koan export compose --base-port 7000`.
- Prefer a specific engine: `Koan up --engine docker`.

5) View logs (follow)
```pwsh
Koan logs
```

6) Tear down (optionally prune volumes)
```pwsh
Koan down --prune-data
```

### Project example: S5.Recs (quick op sheet)

From the repo root, this sequence validates the engine, exports Compose, brings the stack up, checks status and endpoints, then tears down.

```pwsh
# 1) Validate your container engine
Koan doctor --json

# 2) Export Compose (Local profile)
Koan export compose --profile Local

# 3) Up with a generous timeout (first run pulls images)
Koan up --profile Local --timeout 300

# 4) Status and quick checks
Koan status
docker compose -f .Koan/compose.yml ps   # or: podman compose -f .Koan/compose.yml ps

# API health (PowerShell)
Invoke-RestMethod http://127.0.0.1:5084/health/live | Format-List

# DBs (PowerShell)
Test-NetConnection 127.0.0.1 -Port 5081  # Mongo
Invoke-RestMethod http://127.0.0.1:5082/v1/ | Out-Null  # Weaviate

# 5) Tear down and prune data
Koan down --prune-data
```

Notes
- If `Koan up` exits with code 4 (readiness timeout), services may still be progressing. Run `Koan status` and `Koan logs --tail 200`, or use engine-native `compose ps/logs` as above.
- You can force the engine via `--engine docker|podman` if both are installed.

### Path B — From binaries (no build)

1) Ensure `Koan.exe` is available (publish or download), then run from the repo root:
```pwsh
Koan doctor --json
Koan export compose --profile Local
Koan up --profile Local
Koan status
Koan logs
```

2) Tear down when done:
```pwsh
Koan down --prune-data
```

### Alternative: Use Docker Compose directly

If you prefer to manage containers yourself, export with Koan, then use Docker Compose:
```pwsh
Koan export compose
docker compose -f .Koan/compose.yml up -d
docker compose -f .Koan/compose.yml ps
docker compose -f .Koan/compose.yml logs -f --tail=200
docker compose -f .Koan/compose.yml down -v
```

### Alternative: Sample’s local script

The S5 sample also includes a convenience script you can invoke directly:
```pwsh
cd samples/S5.Recs
./start.bat
```

This bypasses Koan’s planner/policies; prefer Koan for consistent profiles, port allocation, and conflict handling.

## Profiles and safety

- Local (default): developer-friendly; bind mounts; auto-avoid ports.
- Staging: bind mounts; “up” gated.
- CI: named volumes (no bind mounts).
- Prod: no bind mounts; strict conflict policy; “up” gated.

## Troubleshooting

- Engine not detected: ensure Docker/Podman is installed and running; try `--engine`.
- Port conflicts: on non‑prod, CLI bumps to free ports; set a different `--base-port` if needed.
- Readiness timeout (exit code 4): services may still be starting. Run `Koan status` and `Koan logs --tail 200` to inspect. You can also use engine-native commands:
  - `docker compose -f .Koan/compose.yml ps` and `docker compose -f .Koan/compose.yml logs --tail=200`
  - or `podman compose -f .Koan/compose.yml ps` / `... logs`
- PATH issues: run `./scripts/cli-install.ps1` to add `dist/bin` to PATH, then open a new shell.
- Compose not found: ensure you ran `Koan export compose` at the repo root; Compose is written under `.Koan/`.
- No project detected (no-args/inspect): the CLI prints the help hint then `No Koan project detected here.` and exits with code 2.

## See also

- Reference: `docs/reference/orchestration.md`
- Decisions: conflict policy, mounts and profiles (`/docs/decisions`)
- Engineering front door: `docs/engineering/index.md`
