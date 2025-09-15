# Koan Orchestration CLI — Build and Install

This CLI provides export/doctor/up/down/status/logs for Koan orchestration (Docker/Podman).

## Prerequisites
- .NET SDK 9.0 or newer (verify with `dotnet --info`).
- One container engine if you plan to run services: Docker Desktop or Podman.

Optional environment knobs:
- `Koan_ENV` — profile: `Local` (default), `Ci`, `Staging`, `Prod`.
- `Koan_PORT_PROBE_MAX` — max increments when auto-avoiding host ports in non-prod (default 200).
- `Koan_PREFERRED_PROVIDERS` — comma-separated provider order, e.g. `docker,podman`.

## Quick run (no install)
Run the CLI from source.

```pwsh
# From repo root
dotnet run --project src/Koan.Orchestration.Cli -- --help

# Example: export compose to the default path
dotnet run --project src/Koan.Orchestration.Cli -- export compose

# Example: check provider availability in JSON
dotnet run --project src/Koan.Orchestration.Cli -- doctor --json
```

## Build
Produces `Koan.Orchestration.Cli.dll` (and exe on Windows) under `bin/`.

```pwsh
# From repo root
dotnet build src/Koan.Orchestration.Cli -c Release
```

Artifacts (Release):
- Windows: `src/Koan.Orchestration.Cli/bin/Release/net9.0/` (framework-dependent) or `publish/` (if published).
- macOS/Linux: same path, platform-specific if published.

## Publish binaries (optional)
Create a single-folder app you can copy anywhere.

Framework-dependent (smaller, requires .NET runtime on target):
```pwsh
# Windows x64
dotnet publish src/Koan.Orchestration.Cli -c Release -r win-x64 --self-contained false -o artifacts/Koan-cli/win-x64

# Linux x64
dotnet publish src/Koan.Orchestration.Cli -c Release -r linux-x64 --self-contained false -o artifacts/Koan-cli/linux-x64

# macOS Apple Silicon
dotnet publish src/Koan.Orchestration.Cli -c Release -r osx-arm64 --self-contained false -o artifacts/Koan-cli/osx-arm64
```

Self-contained (larger, no runtime required):
```pwsh
# Windows x64
dotnet publish src/Koan.Orchestration.Cli -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/Koan-cli/win-x64-sc
```

## Install (simple PATH copy)
Pick your published folder and copy the executable to a directory on your PATH.

- Windows: copy `Koan.Orchestration.Cli.exe` to a PATH folder (e.g., `%USERPROFILE%\bin`) and optionally rename to `Koan.exe`.
- macOS/Linux: copy `Koan.Orchestration.Cli` to `~/bin` and optionally symlink to `Koan`.

Examples:
```pwsh
# Windows
$dest = "$env:USERPROFILE\bin"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item artifacts/Koan-cli/win-x64/Koan.Orchestration.Cli.exe "$dest\Koan.exe" -Force
# Ensure $dest is on PATH, then:
Koan doctor --json
```

```bash
# macOS/Linux
install -d "$HOME/bin"
install -m 0755 artifacts/Koan-cli/linux-x64/Koan.Orchestration.Cli "$HOME/bin/Koan"
export PATH="$HOME/bin:$PATH"  # add to your shell profile for persistence
Koan doctor --json
```

## Verify
```pwsh
Koan doctor
Koan export compose --out compose.yaml
Koan status --json
```

## Batched scripts (Windows PowerShell)
Convenience scripts are available under `scripts/` to streamline build → publish → install → verify:

```pwsh
# Build only
./scripts/cli-build.ps1

# Publish for common platforms (framework-dependent by default)
./scripts/cli-publish.ps1           # win-x64, linux-x64, osx-arm64
./scripts/cli-publish.ps1 -SelfContained -Rids win-x64

# Install on Windows (copies to %USERPROFILE%\bin as Koan.exe)
./scripts/cli-install.ps1

# Verify installed CLI
./scripts/cli-verify.ps1 -Engine docker

# Do everything: build → publish (win-x64) → install → verify
./scripts/cli-all.ps1
```

Notes
- You can change the publish RIDs or installation destination via parameters (see each script's -? help).
- Ensure the destination directory (e.g., `%USERPROFILE%\bin`) is on PATH.

## Commands (reference)
- export compose --out <path> [--profile <local|ci|staging|prod>]
- doctor [--json] [--engine <docker|podman>]
- up [--file <compose.yml>] [--engine <id>] [--profile <p>] [--timeout <seconds>] [--base-port <n>] [--explain] [--dry-run] [-v|-vv|--trace|--quiet]
- down [--file <compose.yml>] [--engine <id>] [--volumes|--prune-data]
- status [--json] [--engine <id>] [--profile <p>] [--base-port <n>]
- logs [--engine <id>] [--service <name>] [--follow] [--tail <n>] [--since <duration>]

Defaults
- Windows-first provider selection: Docker preferred.
- Default compose path: .Koan/compose.yml
- Profile resolution: --profile > Koan_ENV env var > Local (default)

Notes
- Console outputs (doctor/logs) redact sensitive values by key pattern. JSON outputs are not redacted.
- status prints endpoint hints from the planned services and flags conflicting ports when detected.
- `up` is disabled for Staging/Prod; use `export compose` to generate artifacts instead.

## Planning and discovery (how plans are built)

Precedence (first hit wins):
- Descriptor file in project root: `Koan.orchestration.yml|yaml|json`.
- Environment-driven prototype: `Koan_DATA_PROVIDER=postgres|redis` shortcuts.
- Discovery via generated manifest (preferred) or reflection of adapter attributes.
- Fallback demo plan (single Postgres).

Generated manifest: adapters annotate with attributes (ServiceId, ContainerDefaults, EndpointDefaults, AppEnvDefaults); a source generator emits `Koan.Orchestration.__KoanOrchestrationManifest.Json`. The CLI prefers this manifest over reflection for stability and speed.

Token substitution in AppEnv: `{serviceId}`, `{port}`, `{scheme}`, `{host}` are replaced from endpoint defaults. By default, Container mode values are used; see Overrides below to switch to Local mode.

## Overrides (per-project, optional)

File locations (first found wins):
- `.Koan/overrides.json`
- `overrides.Koan.json`

Schema (partial):
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

Behavior:
- Mode: when set to Local, token substitution in AppEnv uses Local endpoint defaults (scheme/host/port) if provided by the adapter; otherwise falls back to Container values.
- Services: per-service `Env` is merged (overrides existing keys), `Volumes` are appended, and `Image` replaces the discovered image/tag when provided. When `Ports` is present and non-empty, it replaces the service's container ports (e.g., `[27018]` → host mapping will follow exporter/profile rules).
- Overrides apply after discovery and before rendering/export.

Examples:
```pwsh
# Force Local endpoint tokens and tweak Mongo env/volume
New-Item -ItemType Directory -Force -Path .Koan | Out-Null
'{"Mode":"Local","Services":{"mongo":{"Env":{"MONGO_INITDB_ROOT_USERNAME":"root"},"Volumes":["./Data/mongo:/data/db"]}}}' | \
	Set-Content .Koan/overrides.json

Koan export compose
```

See also
- Docs: /docs/engineering/index.md, /docs/architecture/principles.md
- Decisions: /docs/decisions/WEB-0035-entitycontroller-transformers.md, /docs/decisions/DATA-0061-data-access-pagination-and-streaming.md
