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
      Abstractions/                # Unit (primitives, contracts)
      Topology/                    # Unit (resolver, layered orchestration)
      Coherence.InMemory/          # Unit + cornerstone
      Coherence.Messaging/         # Unit
      Web/                         # Unit + middleware TestServer
      Adapter.Redis/               # Integration (Testcontainers)
      Adapter.Sqlite/              # Integration (temp file)
    Integration/
      Bootstrap/                   # Boot-smoke: full AddKoan() reflective discovery
        Koan.Tests.Integration.Bootstrap/
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
- `Fixtures/RedisContainerFixture` / `PostgresContainerFixture` / `MongoContainerFixture` /
  `WeaviateContainerFixture` / `OpenSearchContainerFixture` / `ElasticSearchContainerFixture` /
  `CouchbaseContainerFixture` — all follow the same pattern: env-var override → local TCP ping →
  Testcontainers Docker daemon → Docker CLI fallback. Each has a matching
  `TestPipeline<Name>Extensions.Using<Name>Container()` and `TestContext<Name>Extensions.Get<Name>Fixture()`.
- `Pipeline/TestPipelineDockerExtensions` wires the daemon fixture into a pipeline run and surfaces
  diagnostics whenever Docker is unavailable.

## Integration tests are canon (ARCH-0079)

Every `Koan.*.Adapter.*`, `Koan.*.Connector.*`, and `Koan.Cache.Coherence.*` package ships at
least one integration spec that exercises the adapter against real infrastructure. Every pillar
core ships at least one boot-smoke spec that goes through `services.AddKoan()` reflective
discovery and verifies the pillar's primary service resolves. This is mandatory before release;
ad-hoc helpers and per-suite ports of the same fixture logic are rejected.

### `KoanIntegrationHost` — the canon helper

All integration tests build their host through `Koan.Testing.Integration.KoanIntegrationHost`:

```csharp
await using var host = await KoanIntegrationHost.Configure()
    .WithSetting("Cache:Redis:Configuration", redis.ConnectionString)
    .ConfigureServices(services => services.AddKoan())          // Reference = Intent
    .StartAsync(ct);

var client = host.Services.GetRequiredService<ICacheClient>();
```

The helper:
- Builds a real `IHost` (via `HostBuilder`) so `IHostApplicationLifetime` and the full hosted-
  services lifecycle are available — bare `ServiceCollection.BuildServiceProvider()` lacks both
  and silently breaks tests that touch hosted services.
- Seeds in-memory configuration from a dictionary (`WithSetting` / `WithSettings`).
- Stays bootstrap-agnostic — tests choose `s.AddKoan()` (full reflective discovery), `s.AddKoanCore()`
  + manual registrations (partial), or mock-injection variants.
- Returns an `IntegrationHost` wrapper that implements `IAsyncDisposable` (so `await using` works
  cleanly — `IHost` itself only declares `IDisposable`).

### Why bother

Three commits on `feat/koan-cache-pillar` proved the canon's value before it was even codified:

| Bug class | Surfaced by | Why unit tests missed it |
|---|---|---|
| `TryAddEnumerable<TService>(factory)` indistinguishable-descriptor throw | SQLite integration test (first thing to combine `AddKoanCache + adapter` in one DI graph) | Unit tests hand-roll their DI graphs and skip `AddKoanCache` |
| `CacheWriteOptions.GetEffectiveL1Ttl` not clamped to L2 | Redis SWR integration test | Unit assertions encoded the buggy behavior as "expected" |
| Cross-pillar `IConnectionMultiplexer` registration race | Full-DI bootstrap smoke | Unit tests never compose adapter packages |
| `StartupProbeService` aborts host startup on any infra adapter unavailability | Attempt to write per-pillar boot smokes against a project transitively referencing Redis | Unit tests never start real hosted services through `IHost.StartAsync` |

Without the integration tests, these would have shipped. With them, they're caught at PR time.

### Adding an adapter integration test

1. Create `tests/Suites/<Domain>/<Adapter>/Koan.<Domain>.<Adapter>.Tests/` (mirrors the adapter
   project's path under `src/`).
2. Reference `tests/Shared/Koan.Testing/Koan.Testing.csproj` plus the adapter's project + its
   pillar core.
3. Use `KoanIntegrationHost.Configure().ConfigureServices(s => s.AddKoan()).StartAsync(ct)` to
   build the host. Don't invoke `new KoanAutoRegistrar().Initialize(services)` manually — that
   bypasses the reflective-discovery path that production apps actually use.
4. For adapters that need real infrastructure, request a container via the pipeline's
   `.RequireDocker().UsingXxxContainer()` chain. The container fixture is shared across the test
   class via the `TestPipeline` machinery.

### Exemptions

The canon's only standing exemption is orchestration adapters (Docker, Podman, Compose
renderers) — the adapter under test IS the container runtime, so Testcontainers is recursive.
Each must instead ship an alternative integration test (e.g., filesystem fixture asserting the
shape of a generated Compose file). Other exemptions require an ADR amendment.

## Legacy Tree

The retired codebase lives in `../tests.old`. Borrow fixtures or specs as you migrate components,
then delete the unused legacy projects when the suite is stable.
