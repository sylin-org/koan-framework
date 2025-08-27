# Sora Orchestration CLI — Build and Install

This CLI provides export/doctor/up/down/status/logs for Sora orchestration (Docker/Podman).

## Prerequisites
- .NET SDK 9.0 or newer (verify with `dotnet --info`).
- One container engine if you plan to run services: Docker Desktop or Podman.

Optional environment knobs:
- `SORA_ENV` — profile: `Local` (default), `Ci`, `Staging`, `Prod`.
- `SORA_PORT_PROBE_MAX` — max increments when auto-avoiding host ports in non-prod (default 200).
- `SORA_PREFERRED_PROVIDERS` — comma-separated provider order, e.g. `docker,podman`.

## Quick run (no install)
Run the CLI from source.

```pwsh
# From repo root
dotnet run --project src/Sora.Orchestration.Cli -- --help

# Example: export compose to the default path
dotnet run --project src/Sora.Orchestration.Cli -- export compose

# Example: check provider availability in JSON
dotnet run --project src/Sora.Orchestration.Cli -- doctor --json
```

## Build
Produces `Sora.Orchestration.Cli.dll` (and exe on Windows) under `bin/`.

```pwsh
# From repo root
dotnet build src/Sora.Orchestration.Cli -c Release
```

Artifacts (Release):
- Windows: `src/Sora.Orchestration.Cli/bin/Release/net9.0/` (framework-dependent) or `publish/` (if published).
- macOS/Linux: same path, platform-specific if published.

## Publish binaries (optional)
Create a single-folder app you can copy anywhere.

Framework-dependent (smaller, requires .NET runtime on target):
```pwsh
# Windows x64
dotnet publish src/Sora.Orchestration.Cli -c Release -r win-x64 --self-contained false -o artifacts/sora-cli/win-x64

# Linux x64
dotnet publish src/Sora.Orchestration.Cli -c Release -r linux-x64 --self-contained false -o artifacts/sora-cli/linux-x64

# macOS Apple Silicon
dotnet publish src/Sora.Orchestration.Cli -c Release -r osx-arm64 --self-contained false -o artifacts/sora-cli/osx-arm64
```

Self-contained (larger, no runtime required):
```pwsh
# Windows x64
dotnet publish src/Sora.Orchestration.Cli -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/sora-cli/win-x64-sc
```

## Install (simple PATH copy)
Pick your published folder and copy the executable to a directory on your PATH.

- Windows: copy `Sora.Orchestration.Cli.exe` to a PATH folder (e.g., `%USERPROFILE%\bin`) and optionally rename to `sora.exe`.
- macOS/Linux: copy `Sora.Orchestration.Cli` to `~/bin` and optionally symlink to `sora`.

Examples:
```pwsh
# Windows
$dest = "$env:USERPROFILE\bin"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item artifacts/sora-cli/win-x64/Sora.Orchestration.Cli.exe "$dest\sora.exe" -Force
# Ensure $dest is on PATH, then:
sora doctor --json
```

```bash
# macOS/Linux
install -d "$HOME/bin"
install -m 0755 artifacts/sora-cli/linux-x64/Sora.Orchestration.Cli "$HOME/bin/sora"
export PATH="$HOME/bin:$PATH"  # add to your shell profile for persistence
sora doctor --json
```

## Verify
```pwsh
sora doctor
sora export compose --out compose.yaml
sora status --json
```

## Batched scripts (Windows PowerShell)
Convenience scripts are available under `scripts/` to streamline build → publish → install → verify:

```pwsh
# Build only
./scripts/cli-build.ps1

# Publish for common platforms (framework-dependent by default)
./scripts/cli-publish.ps1           # win-x64, linux-x64, osx-arm64
./scripts/cli-publish.ps1 -SelfContained -Rids win-x64

# Install on Windows (copies to %USERPROFILE%\bin as sora.exe)
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
- Default compose path: .sora/compose.yml
- Profile resolution: --profile > SORA_ENV env var > Local (default)

Notes
- Console outputs (doctor/logs) redact sensitive values by key pattern. JSON outputs are not redacted.
- status prints endpoint hints from the planned services and flags conflicting ports when detected.
- `up` is disabled for Staging/Prod; use `export compose` to generate artifacts instead.

## Planning and discovery (how plans are built)

Precedence (first hit wins):
- Descriptor file in project root: `sora.orchestration.yml|yaml|json`.
- Environment-driven prototype: `SORA_DATA_PROVIDER=postgres|redis` shortcuts.
- Discovery via generated manifest (preferred) or reflection of adapter attributes.
- Fallback demo plan (single Postgres).

Generated manifest: adapters annotate with attributes (ServiceId, ContainerDefaults, EndpointDefaults, AppEnvDefaults); a source generator emits `Sora.Orchestration.__SoraOrchestrationManifest.Json`. The CLI prefers this manifest over reflection for stability and speed.

Token substitution in AppEnv: `{serviceId}`, `{port}`, `{scheme}`, `{host}` are replaced from endpoint defaults. By default, Container mode values are used; see Overrides below to switch to Local mode.

## Overrides (per-project, optional)

File locations (first found wins):
- `.sora/overrides.json`
- `overrides.sora.json`

Schema (partial):
```json
{
	"Mode": "Local", // or "Container" (default)
	"Services": {
		"mongo": {
			"Image": "mongo:7",
			"Env": { "MONGO_INITDB_ROOT_USERNAME": "root" },
			"Volumes": [ "./Data/mongo:/data/db" ]
		}
	}
}
```

Behavior:
- Mode: when set to Local, token substitution in AppEnv uses Local endpoint defaults (scheme/host/port) if provided by the adapter; otherwise falls back to Container values.
- Services: per-service `Env` is merged (overrides existing keys), `Volumes` are appended, and `Image` replaces the discovered image/tag when provided.
- Overrides apply after discovery and before rendering/export.

Examples:
```pwsh
# Force Local endpoint tokens and tweak Mongo env/volume
New-Item -ItemType Directory -Force -Path .sora | Out-Null
'{"Mode":"Local","Services":{"mongo":{"Env":{"MONGO_INITDB_ROOT_USERNAME":"root"},"Volumes":["./Data/mongo:/data/db"]}}}' | \
	Set-Content .sora/overrides.json

sora export compose
```

See also
- Docs: /docs/engineering/index.md, /docs/architecture/principles.md
- Decisions: /docs/decisions/WEB-0035-entitycontroller-transformers.md, /docs/decisions/DATA-0061-data-access-pagination-and-streaming.md
