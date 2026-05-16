# How to Install and Operate the Modern Koan Orchestration CLI

**Audience:** Koan platform engineers and solution owners who need the refactored orchestration CLI for daily workflows, CI jobs, or one-shot automation.

**Prerequisites:**

- Koan repository synced at .NET 10 with the A4 CLI split (`Koan.Orchestration.Cli` + `Koan.Orchestration.Cli.Core`).
- `dotnet` SDK 10.0 preview (matching `global.json`).
- Access to PowerShell (Windows) or Bash/Zsh/Fish (Linux/macOS) for shell integration.
- Ability to publish packages into a local feed or install global tools in the current environment.

**Inputs:**

- CLI solution projects (`src/Koan.Orchestration.Cli`, `src/Koan.Orchestration.Cli.Core`).
- Local NuGet feed (e.g., `./artifacts/packages`) or the default NuGet cache.
- Desired orchestration profile (`local`, `ci`, `staging`, `prod`).

**Outputs:**

- Installed `koan` dotnet tool (portable) or RID-specific executable.
- Command completions registered for the target shell.
- Repeatable script segment for CI (`dotnet tool exec koan <command>`).

**Success Criteria:**

- `dotnet pack` emits tool packages under `artifacts/packages` with `ToolCommandName=koan`.
- `dotnet tool run koan doctor --json` executes without rebuilding the repo inside CI.
- Help descriptors list each verb with usage metadata and `--help` short-circuits execution.

**Failure Modes / Diagnostics:**

- Missing pack artifacts: rerun `dotnet pack src/Koan.Orchestration.Cli/Koan.Orchestration.Cli.csproj -c Release -o artifacts/packages`.
- Tool install not found: verify `dotnet tool install --global koan --add-source ./artifacts/packages` ran with matching RID packages.
- Provider discovery failures: inspect `koan doctor --json` output, confirm Docker/Podman connectors are referenced by the host project.
- Help output missing commands: ensure `CliApplication` built successfully and `dotnet build Koan.sln` executed after code changes.

---

## Step 1. Pack the Tool from Source

Generate portable and RID-specific packages from the repo root:

```pwsh
# From repo root
pwsh ./scripts/cli-pack.ps1
# or manually
 dotnet pack src/Koan.Orchestration.Cli/Koan.Orchestration.Cli.csproj `
  -c Release `
  -o ./artifacts/packages
```

`cli-pack.ps1` wraps the same `dotnet pack` invocation and enables package pruning per ADR ARCH-0040. Confirm `artifacts/packages` contains `koan.<version>.nupkg` plus RID-specific variants (`koan.<version>-win-x64.nupkg`, etc.).

## Step 2. Install the Tool

Install the portable tool into a manifest or globally. For global installation with a local feed:

```pwsh
 dotnet tool update --global koan \
  --add-source ./artifacts/packages \
  --prerelease
```

For repo-scoped manifests (preferred for CI):

```pwsh
 dotnet new tool-manifest --force
 dotnet tool install koan --add-source ./artifacts/packages --prerelease
```

Commit the manifest (`.config/dotnet-tools.json`) when sharing with the team. The manifest keeps CI and local workflows aligned.

## Step 3. Validate Core Commands

Confirm the runtime host and command dispatcher operate as expected:

```pwsh
 dotnet tool run koan --help
 dotnet tool run koan doctor --json
 dotnet tool run koan inspect --profile local
 dotnet tool run koan up --dry-run --file .koan/compose.yml
```

- `--help` should list `export`, `doctor`, `up`, `down`, `status`, `logs`, `inspect` with contract-aligned descriptions.
- `koan doctor --json` returns provider availability payloads (Docker/Podman).
- `koan inspect` prints project detection results and plan hints using the new structured runtime.

## Step 4. Enable Shell Completions

The new dispatcher surfaces rich metadata, enabling shell completion. The `completion` verb will be added once Workstream M5 lands; until then, generate completions with the built-in `System.CommandLine` tooling:

```pwsh
 dotnet-suggest.exe register
 dotnet-suggest.exe list # confirm koan registered
```

For Bash/Zsh:

```bash
dotnet-suggest register bash
source ~/.dotnet/tools/suggest.sh
```

Ensure `DOTNET_TOOLS_PATH` is on `PATH` so `koan` resolves for completion probes.

## Step 5. Integrate into CI Pipelines

Add a one-shot stage to your pipeline definition:

```yaml
- pwsh: |
    dotnet tool restore
    dotnet tool run koan doctor --json
    dotnet tool run koan export compose --out $(Build.ArtifactStagingDirectory)/koan-compose.yml
  displayName: "Smoke test Koan CLI"
```

Requirements:

- Use the manifest created in Step 2 (`dotnet tool restore`).
- Cache `artifacts/packages` or publish to an internal feed for repeatability.
- Capture CLI output for troubleshooting (JSON mode recommended for machine consumption).

## Step 6. Keep Local Profiles in Sync

Leverage the CLI’s profile-aware options for staging/production hygiene:

```pwsh
 dotnet tool run koan up --profile local --dry-run
 dotnet tool run koan export compose --profile ci --out ./deploy/ci/docker-compose.yml
 dotnet tool run koan status --profile local --json
```

- Use `--profile ci` when running under build agents to avoid prod-only behaviors.
- `--engine docker|podman` explicitly selects connectors when both are available.
- `--no-launch-manifest` disables launch manifest persistence for ephemeral runs.

## Edge Cases & Hardening

- **Offline Environments:** Copy `artifacts/packages` into the target machine and use `--add-source <path>`; avoid hitting nuget.org in isolated networks.
- **RID-Specific Installs:** `dotnet tool install` consumes portable packages. For standalone RID builds, unpack the `koan.<version>-<rid>.nupkg` and invoke the embedded executable directly.
- **Provider Conflicts:** When Docker and Podman are both reachable, set `KOAN_ORCHESTRATION_ENGINE=docker` (or use `--engine`). Inspect `koan doctor` output to confirm the chosen provider.
- **Legacy Scripts:** Replace `dotnet run --project src/Koan.Orchestration.Cli` calls with `dotnet tool run koan`; archive or delete deprecated scripts after validation.
- **Telemetry:** Retain CLI output logs (`--json` where possible) to accelerate incident response when orchestration bring-up fails.

## Related Links

- Proposal A4 (`docs/proposals/koan-dotnet10-opportunity-map/A/A04-cli-one-shot-tools-and-dnx.md`).
- CLI host (`src/Koan.Orchestration.Cli`) and runtime core (`src/Koan.Orchestration.Cli.Core`).
- Scripts (`scripts/cli-pack.ps1`, `scripts/cli-all.ps1`) for packaging and smoke tests.
- ADR `ARCH-0047`, `ARCH-0048`, `ARCH-0049` for orchestration service discovery contracts.
