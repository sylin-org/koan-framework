# Koan.Orchestration.Renderers.Compose

## Contract
- **Purpose**: Render Koan orchestration plans into Docker Compose manifests for local and CI workloads.
- **Primary inputs**: `OrchestrationPlan` produced by Koan planners, provider metadata (Docker/Podman), and adapter capabilities describing services.
- **Outputs**: Compose YAML, supporting documentation, and boot report annotations summarizing generated artifacts.
- **Failure modes**: Missing provider metadata (e.g., container image), unsupported Compose features, or plan inconsistencies.
- **Success criteria**: Generated Compose files start services without manual edits, modules contribute volumes/env correctly, and diagnostics highlight unsupported capabilities.

## Quick start
```csharp
using Koan.Orchestration.Renderers.Compose;

var plan = await OrchestrationPlanner.PlanAsync(ct);
var exporter = new ComposeExporter();
var compose = await exporter.RenderAsync(plan, new ComposeExportOptions
{
    ProjectName = "koan-dev",
    OutputPath = Path.Combine(solutionRoot, "compose"),
    IncludeProfiles = { "dev" }
});

Console.WriteLine($"Compose written to {compose.OutputPath}");
```
- Generate an orchestration plan (aggregating adapters/providers) and pass it to `ComposeExporter` to emit YAML.
- Customize output via `ComposeExportOptions` (project name, profiles, secrets, networks).

## Configuration
- Merge environment-specific overrides by populating `ComposeExportOptions.Overrides`.
- Enable healthcheck rendering by ensuring providers supply health metadata; exporter will embed Compose healthchecks.
- To generate Podman-compatible manifests, combine with `Koan.Orchestration.Provider.Podman` metadata.

## Edge cases
- Port conflicts: exporter auto-detects collisions; monitor warnings and override using explicit port assignments in adapter metadata.
- Secret mounts: ensure referenced secrets exist in Koan secrets providers; exporter renders mounts but cannot provision secret values.
- Multi-network setups: declare networks in provider metadata; exporter creates networks but requires Docker/Podman engine permissions.
- Large configs: break outputs into per-module files using `ComposeExportOptions.ServiceFilters` to keep YAML manageable.

## Related packages
- `Koan.Orchestration.Abstractions` – orchestration planner producing plans for the exporter.
- `Koan.Orchestration.Provider.Docker` / `.Podman` – feed runtime metadata.
- `Koan.Orchestration.Cli` – CLI front-end that invokes this exporter.

## Reference
- `ComposeExporter` – main entry point for rendering Compose.
- `ComposeExportOptions` – configuration options controlling output shape.
- `ComposeArtifacts` – result structure with output path and metadata.
