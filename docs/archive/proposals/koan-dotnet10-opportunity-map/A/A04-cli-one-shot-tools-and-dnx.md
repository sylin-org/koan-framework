# A4 — Koan CLI Modernization (SDK 10)

> **Contract**  
> **Inputs:** Existing orchestration CLI intent from [ARCH-0047](../../decisions/ARCH-0047-orchestration-hosting-and-exporters-as-pluggable-adapters.md), endpoint/persistence rules in [ARCH-0048](../../decisions/ARCH-0048-endpoint-resolution-and-persistence-mounts.md), unified service metadata per [ARCH-0049](../../decisions/ARCH-0049-unified-service-metadata-and-discovery.md), and refactoring guidance in [ARCH-0068](../../decisions/ARCH-0068-refactoring-strategy-static-vs-di.md).  
> **Outputs:** Re-architected Koan CLI delivering modular command hosting, deterministic packaging (`koan` tool), and documentation/automation that unblock one-shot execution, DNX shim, package pruning, and tab completion.  
> **Success Criteria:** New CLI runs as a packaged tool (`dotnet tool exec koan -- doctor`), exposes well-scoped command modules, emits provenance-aligned logs, supports tab-completion generation, and remains within the orchestration adapter boundaries declared in the ADRs.  
> **Error Modes:** Runtime dependency mis-resolution (provider discovery), packaging skew between RID-specific and portable builds, command handler misconfiguration yielding ambiguous help, failure to honor profile/plan metadata, or regression in container bring-up flows. Each mode must surface actionable diagnostics and be covered by regression tests.

---

## 1. Goals & Non-Goals

**Goals**
- Deliver a sustainable, modular CLI foundation that embodies the intent of the orchestration ADRs while enabling A4 capabilities (one-shot, DNX shim, pruning, completions).
- Separate command surface, orchestration domain services, and packaging so each concern is testable and replaceable.
- Unlock rapid extension: adding new orchestration verbs or non-orchestration utilities should not require touching a 900-line monolith.

**Non-Goals**
- Changing orchestration SPI contracts (providers, exporters) beyond surface refactors required to remove static coupling.
- Introducing new orchestration features (e.g., Helm exporter) beyond the scope required to validate the new CLI architecture.
- Maintaining backwards compatibility with the existing `koan-orchestrate` command name; we will migrate to `koan` as the canonical tool identity.

---

## 2. Target Architecture

### 2.1 Layers

1. **Koan.Orchestration.Cli.Core** (new)  
   - Pure, testable services: `IPlanService`, `IEndpointFormatter`, `IPackageLocator`, `IProviderSelector`.  
   - Encapsulates planner, exporters, and adapter discovery while honoring manifests/attributes (ARCH-0047/48/49).  
   - No console or file-system side effects beyond abstractions.

2. **Koan.Orchestration.Cli.Host** (new)  
   - Thin host wiring `System.CommandLine` commands, middleware, telemetry, and dependency injection.  
   - Provides global options (`--profile`, `--verbosity`, `--json`, `--engine`).  
   - Generates completions via `dotnet-suggest` and exposes `koan completion <shell>` convenience commands.

3. **Koan.Orchestration.Cli.Commands**  
   - One class per verb (`DoctorCommand`, `ExportCommand`, `UpCommand`, `StatusCommand`, `InspectCommand`, `LogsCommand`, `DownCommand`).  
   - Handlers depend on abstractions defined in Core and return structured `CommandResult` models for common formatting.

4. **Koan.Orchestration.Cli.Tooling** (new packaging project)  
   - Multi-targeted pack profile producing:  
     - Portable tool (`dotnet tool install koan`).  
     - RID-specific bundles for win-x64/linux-x64/osx-arm64.  
   - Manages DNX shim artifacts, package pruning toggles, and offline feeds.

### 2.2 Composition & Extensibility

- Adopt a minimal DI container (`Microsoft.Extensions.DependencyInjection`) in Host.  
- Commands register via `ICommandModule` interface. Modules can be discovered using assembly scanning (opt-in) without hard dependencies.  
- Shared middleware enforces profile validation, adapter pre-load, and consistent error handling (exceptions → exit codes).

### 2.3 Packaging & Distribution

- `ToolCommandName` becomes `koan`.  
- `dotnet pack` pipeline produces portable `.nupkg` plus RID-specific `.nupkg` via `PackRidSpecific=true`.  
- `Directory.Build.props` introduces scoped `<RestoreEnablePackagePruning>true</RestoreEnablePackagePruning>` for CLI/tooling solutions and sample templates.  
- DNX shim delivered as `tools/dnx/koan.cmd` + `koan` (Bash) referencing the installed tool.  
- Provide `dotnet tool restore` manifest entries for repo CI and automation.

---

## 3. Workstreams

1. **Foundational Split**  
   - Extract Core services from the current monolithic `Program.cs`.  
   - Introduce interfaces, pure helpers (static per ARCH-0068 guidance), and unit tests.  
   - Preserve existing behavior while reducing reliance on globals.

2. **Command Host Migration**  
   - Introduce `System.CommandLine` host with global options and sub-commands.  
   - Migrate `doctor` first as a tracer bullet, ensuring diagnostics parity.  
   - Incrementally port remaining commands, deleting legacy static switch once parity achieved.

3. **Packaging & Tool Identity**  
   - Update `.csproj` to emit `koan` tool, dropping `koan-orchestrate`.  
   - Extend `scripts/cli-*.ps1` to use the packaged tool.  
   - Author CI snippet for one-shot execution:

     ```pwsh
     dotnet tool update --global koan --add-source ./artifacts/packages
     dotnet tool exec koan doctor --json
     ```

4. **Developer Experience Enhancements**  
   - Add `koan completion <shell>` command plus documentation for PowerShell, Bash, Zsh, and Fish.  
   - Publish DNX shim (`dnx.cmd`/`dnx`) pointing to `dotnet tool exec koan`.  
   - Document package pruning toggle impacts in CLI README/TECHNICAL.

5. **Documentation & Samples**  
   - Update A4 how-to, CLI README, and TECHNICAL with new architecture diagrams and usage.  
   - Refresh `docs/proposals/koan-dotnet10-opportunity-map/how-to` with a CLI modernization guide.  
   - Add CI templates under `samples/` demonstrating one-shot usage.

6. **Validation & Telemetry**  
   - Add unit tests for each command via `System.CommandLine` invocation.  
   - Introduce smoke tests executing `dotnet tool run koan doctor` against mocked providers.  
   - Ensure provenance logs include `cli-host` version, command, flags, and adapter selections.

---

## 4. Edge Cases & Risk Mitigation

- **Offline environments**: Tool must operate solely on local package feeds; provide documented `--add-source` workflow.  
- **Mixed runtimes (Docker+Podman)**: Provider selection logic must remain deterministic even when both engines report availability.  
- **Windows vs WSL path handling**: DNX shim and compose exports must handle path separators consistently, tested via CI matrix.  
- **Manifest drift**: Planner should fail fast when service manifests are missing required fields, guiding developers to regenerate adapters.  
- **Legacy script compatibility**: Transition scripts emit warnings when invoking deprecated commands, pointing to the new tool usage.

---

## 5. Milestones & Deliverables

| Milestone | Deliverable | Validation |
|-----------|-------------|------------|
| M1 – Core Extraction | `Koan.Orchestration.Cli.Core` project, service interfaces, baseline unit tests | `dotnet test` core suite |
| M2 – Command Host | `koan` host with `doctor` + `status` commands migrated | CLI smoke tests, parity checklist |
| M3 – Full Command Parity | All verbs migrated, legacy entry point removed | Regression suite + manual plan export validation |
| M4 – Packaging Modernization | Tool packaging outputs + DNX shim | `dotnet pack` + install-from-artifacts scenario |
| M5 – DX Enhancements | Tab-completion command, docs, CI examples | Docs lint, CI template run |
| M6 – Final Validation | Provenance checks, telemetry review, A4 acceptance sign-off | Koan solution build + curated acceptance notebook |

---

## 6. References & Follow-Ups

- Align with orchestration SPI ADRs: [ARCH-0047](../../decisions/ARCH-0047-orchestration-hosting-and-exporters-as-pluggable-adapters.md), [ARCH-0048](../../decisions/ARCH-0048-endpoint-resolution-and-persistence-mounts.md), [ARCH-0049](../../decisions/ARCH-0049-unified-service-metadata-and-discovery.md).  
- Follow refactoring guardrails from [ARCH-0068](../../decisions/ARCH-0068-refactoring-strategy-static-vs-di.md).  
- Produce a follow-up ADR if new extensibility patterns emerge (e.g., command module discovery) or if orchestration SPI adjustments become necessary.
