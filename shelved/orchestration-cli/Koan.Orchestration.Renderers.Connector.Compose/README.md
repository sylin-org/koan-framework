# Koan.Orchestration.Renderers.Connector.Compose

> ✅ Validated against host-mount discovery, healthcheck rendering, and network defaults on **2025-09-29**. See [`TECHNICAL.md`](./TECHNICAL.md) for component breakdown and edge-case coverage.

Exporter that turns a `Koan.Orchestration.Models.Plan` into a Docker Compose v2 manifest used by the Koan CLI and orchestration tooling.

## Capabilities

- Declares `internal`/`external` networks and attaches services appropriately (app on both; dependencies on internal only).
- Injects persistence mounts based on `HostMountAttribute` or image heuristics (Postgres, Mongo, Redis, SQL Server, Weaviate, Ollama).
- Emits healthchecks when `Plan` services provide an HTTP endpoint, falling back to TCP probing when curl/wget are unavailable.
- Adds named volumes automatically for Ci profile; bind-mounts `./Data/{service}` for Local/Staging; skips auto-mounting in Prod.
- Generates optional `build` blocks for the app (`api`) when a project file is present, using the repo root as context.

## Quick verification

```pwsh
# Render compose for the current project
dotnet run --project src/Koan.Orchestration.Cli -- export compose

# Inspect the generated YAML
Get-Content .Koan/compose.yml
```

## Programmatic usage

```csharp
using Koan.Orchestration.Renderers.Connector.Compose;
using Koan.Orchestration.Models;

Plan plan = Planner.Build(Profile.Local); // or your custom discovery flow
var exporter = new ComposeExporter();

await exporter.GenerateAsync(plan, Profile.Local, ".Koan/compose.yml");
```

- `Profile` drives persistence heuristics (bind mounts vs named volumes vs none).
- The exporter writes directly to `outPath`; it does not return an in-memory representation.
- `ExporterCapabilities` advertises readiness probe support so callers know healthchecks are emitted.

## Tips & edge cases

- Ensure services include container images (`ServiceSpec.Image`); missing images cause empty entries and Compose will fail to boot.
- Healthchecks rely on curl/wget/bash inside the container—supply lightweight base images when possible.
- If reflection-based host mount discovery fails (e.g., missing optional assembly), the exporter falls back to heuristic mounts or none at all; add explicit entries to `ServiceSpec.Volumes` to override.
- To avoid bind mounts in Local profile, set your own volumes before handing the plan to the exporter.

## Related docs

- [`Koan.Orchestration.Cli`](../Koan.Orchestration.Cli/README.md) – CLI front-end invoking the exporter.
- [`Koan.Orchestration.Connector.Docker`](../Koan.Orchestration.Connector.Docker/README.md), [`Koan.Orchestration.Connector.Podman`](../Koan.Orchestration.Connector.Podman/README.md) – providers consuming the generated Compose files.
- `/docs/engineering/index.md`, `/docs/architecture/principles.md` – orchestration design principles.

