# Koan Test Platform

This directory hosts the greenfield Koan testing platform. It replaces the legacy `tests.old` tree and
provides an opinionated, parallel-friendly structure for every suite.

## Contract

- **Suites** live under `Suites/<Domain>/<Scope>/` and map 1:1 with Koan runtime modules.
- **Shared assets** (fixtures, pipelines, diagnostics) live in `Shared/` and are consumed by
  every suite via project references.
- **Seed packs** in `SeedPacks/` deliver deterministic data for scenarios and are versioned
  alongside the specs that rely on them.
- All specs execute through the `TestPipeline` facade to guarantee Arrange/Act/Assert semantics
  and consistent diagnostics output.

## Layout

```
/tests
  Directory.Build.props           # Test-wide MSBuild defaults and temp output isolation
  README.md                       # You are here
  SeedPacks/                      # Deterministic data packs (JSON or NDJSON)
  Shared/
    Koan.Testing/                 # Test harness library and pipeline primitives
      Infrastructure/             # External runtime helpers (Docker probes, etc.)
  Suites/
    Core/
      Unit/
        Koan.Tests.Core.Unit/
    Data/
      Core/
        Koan.Tests.Data.Core/
      Connector.SqlServer/
        Koan.Data.Connector.SqlServer.Tests/
    Canon/
      Unit/
        Koan.Tests.Canon.Unit/
      Integration/
        Koan.Tests.Canon.Integration/
    AI/
      Unit/
        Koan.Tests.AI.Unit/
    Cache/
      Unit/
        Koan.Tests.Cache.Unit/
```

## Adding a Suite

1. Create `Suites/<Domain>/<Scope>/<ProjectName>/` and run `dotnet new xunit` if you need a blank start.
2. Reference `Shared/Koan.Testing/Koan.Testing.csproj` for the pipeline, fixtures, and diagnostics.
3. Describe suite requirements in `testsuite.yaml` and keep specs under `Specs/<Feature>/`.
4. Use the `TestPipeline` facade for every scenario to keep Arrange/Act/Assert explicit and
   automatically register fixtures via `WithFixture`.

## Running suites

```pwsh
dotnet test Koan.sln
```

For targeted validation, invoke an individual suite project:

```pwsh

    ### Sample Suite Coverage (as of 2025-10-07)

    - **MCP Sample Suite**: `Koan.Samples.McpService.Tests` (S12.MedTrials.McpService)
      - Initial health check test for `/health` endpoint.
    - **DocMind Sample Suite**: `Koan.Samples.DocMind.Tests` (S13.DocMind)
      - Initial health check test for `/health` endpoint.
    - **PantryPal/Recipes Sample Suite**: `Koan.Samples.PantryPal.Tests` (S16.PantryPal)
      - Initial health check test for `/health` endpoint.

    Next: Add scenario/integration tests for each sample suite covering endpoints, flows, and sample-specific behaviors.
dotnet test tests/Suites/Data/Core/Koan.Tests.Data.Core/Koan.Tests.Data.Core.csproj
```

## Infrastructure helpers

- `Infrastructure/DockerEnvironment.cs` performs cross-platform probing for the Docker daemon and
  honors `DOCKER_HOST` overrides, CLI contexts, and named pipe/socket fallbacks.
- `Fixtures/DockerDaemonFixture` caches probe results, disables Ryuk for Testcontainers-powered suites,
  and exposes availability metadata so specs can decide whether to skip or fall back without duplicating logic.
- `Fixtures/RedisContainerFixture` builds on the Docker probe to provision a disposable Redis instance via
  Testcontainers or reuse local/explicit endpoints when available.
- `Fixtures/PostgresContainerFixture` provides a Postgres connection string by preferring explicit/local instances and
  falling back to a disposable Testcontainers-hosted database when Docker is available.
- `Fixtures/MongoContainerFixture` surfaces a MongoDB connection string, preferring explicit/local clusters first and
  provisioning a disposable Testcontainers-backed instance when Docker is reachable.
- `Pipeline/TestPipelineDockerExtensions` wires the fixture into a pipeline run and surfaces diagnostics
  whenever Docker is unavailable.
- `Pipeline/TestPipelineMongoExtensions` registers the Mongo fixture so scenarios can request a Mongo connection with a single call.

## Legacy Tree

The retired codebase lives in `../tests.old`. Borrow fixtures or specs as you migrate components,
then delete the unused legacy projects when the suite is stable.
