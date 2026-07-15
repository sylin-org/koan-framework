# Koan Test Platform

This directory hosts the Koan testing platform. Suites are organized by Koan runtime module and run
under **xUnit v3** with **Testcontainers 4.x** module fixtures (ARCH-0091), booting real Koan through
`AddKoan()` reflective discovery (ARCH-0079).

## Contract

- **Suites** live under `Suites/<Domain>/<Scope>/` and map 1:1 with Koan runtime modules.
- **Shared testing packages** live under `src/` and are consumed by suites via project references:
  - `src/Koan.Testing.Hosting/` — the xUnit-agnostic ARCH-0079 reflective host (`KoanIntegrationHost`,
    namespace `Koan.Testing.Integration`). Carries no xUnit dependency, so it is referenced by both
    the xUnit-v2 fenced Jobs projects and the xUnit-v3 suites without an assembly collision.
  - `src/Koan.Testing.Containers/` — the xUnit-v3 Testcontainers fixtures (`KoanContainerFixture` + the
    per-engine fixtures) and the `KoanDataSpec<TFixture>` spec base.
  - `src/Koan.Testing/` — the application-facing `EntityConformanceSpecs<TEntity>` kit. The bespoke
    `TestPipeline`/`TestContext` DSL formerly associated with this name was removed in ARCH-0091; do
    not add general harness primitives back to the conformance package.
- **Seed packs** in `SeedPacks/` deliver deterministic data and are content-copied into every test
  project via `tests/Directory.Build.props`.
- **Shared suite libraries** use the `.TestKit` project suffix and explicitly declare
  `<IsTestProject>false</IsTestProject>`. Their abstract specs and fixtures execute only through
  concrete consumer suites; `dotnet test Koan.sln` never launches a TestKit assembly itself.

## xUnit v3 conventions

Suites are xUnit v3 in VSTest-compatible mode (so `dotnet test` keeps working) unless an explicitly
documented self-executing infrastructure lane opts out to prevent fixture side effects. A normal
project opts in with:

```xml
<PropertyGroup>
  <!-- ARCH-0091: xUnit v3 runs as a self-executing test assembly (VSTest-compatible keeps `dotnet test`). -->
  <OutputType>Exe</OutputType>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="xunit.v3" Version="3.2.2" />
  <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.6.0" />
</ItemGroup>
```

Differences from the old xUnit v2 DSL that every spec must respect:

- **Own host selection.** A suite that relies on one process-default Koan host must serialize its
  classes with `[assembly: CollectionBehavior(DisableTestParallelization = true)]`. When a fact can
  overlap, nest, start, or stop another host, enter `AppHost.PushScope(ownedHost.Services)` for the
  complete operation instead. Flow-scoped specs may run concurrently when every Entity-static path
  is covered.
- **`IAsyncLifetime` returns `ValueTask`** (not `Task`) — both `InitializeAsync` and `DisposeAsync`.
- **Native skips** — use `Assert.Skip(reason)` / `Assert.SkipWhen(cond, reason)` /
  `Assert.SkipUnless(cond, reason)`. `SkippableFact` and the `Xunit.Abstractions` namespace are gone;
  `ITestOutputHelper` now lives in the `Xunit` namespace.
- **Ambient cancellation** — use `TestContext.Current.CancellationToken` instead of threading a token
  through fixtures.

Serialization protects scheduling; it does not resurrect an earlier process-default provider after a
newer host stops or fails startup. A long-lived shared fixture therefore owns data/host lifetime only.
Its specs must select `fixture.Services` for each complete Entity-backed fact flow. Never repair a test
by assigning `AppHost.Current`, restoring a predecessor globally, or adding a production fallback.

## Container fixtures (`Koan.Testing.Containers`)

`KoanContainerFixture` is the base for Docker-backed fixtures (built on Testcontainers 4.x official
module builders). Concrete fixtures: `RedisFixture`, `PostgresFixture`, `MongoFixture`,
`SqlServerFixture`, `CouchbaseFixture`, plus the daemon-free `SqliteFixture`, `JsonFixture`, and
`InMemoryFixture`. Each exposes:

- `string Engine` — the engine name (e.g. `redis`).
- `bool IsAvailable` / `string? Reason` — set during init. If Docker is absent, misconfigured, or the
  image pull fails, the fixture comes up **unavailable** with a `Reason` instead of throwing — specs
  then `Assert.SkipWhen(!fixture.IsAvailable, fixture.Reason)`.
- `string ConnectionString` — the live endpoint.
- `IReadOnlyDictionary<string,string?> Settings` / `SettingsForBoot()` — canonical config keys to feed
  into `KoanIntegrationHost`.

Consume a fixture as a class fixture (one container per test class):

```csharp
public sealed class MyRedisSpec(RedisFixture redis, ITestOutputHelper output) : IClassFixture<RedisFixture>
{
    [Fact]
    public async Task Roundtrips()
    {
        Assert.SkipWhen(!redis.IsAvailable, redis.Reason ?? "Redis unavailable");
        var ct = TestContext.Current.CancellationToken;

        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(redis.SettingsForBoot())
            .ConfigureServices(s => s.AddKoan())
            .StartAsync(ct);
        // ...
    }
}
```

For data-adapter suites, derive from `KoanDataSpec<TFixture>` instead of hand-wiring the host:

- `RequireBackingStore()` — `Assert.Skip`s when the fixture is unavailable.
- `BootAsync()` — returns a `BoundHost` (an `await using` host booted with the fixture's settings via
  `KoanIntegrationHost` + `AddKoan()`).
- `NewPartition(label)` + `Lease(partition)` — mint and enter a per-execution `EntityContext.Partition`
  so sequential specs and separate test processes do not collide on shared backing stores.

## Adding a suite

1. Create `Suites/<Domain>/<Scope>/<ProjectName>/` (mirror the `src/` path of the module under test).
2. Reference `src/Koan.Testing.Hosting` (for `KoanIntegrationHost`) and, when you need a real
   backing store, `src/Koan.Testing.Containers` (for the fixtures + `KoanDataSpec`). Do **not**
   reference the retired `Koan.Testing` shim.
3. Adopt the xUnit-v3 csproj shape above and add the `DisableTestParallelization` assembly attribute.
4. Keep specs under `Specs/<Feature>/`.

## Running suites

### Bootstrap lanes

Bootstrap proof is partitioned by composition cost (ARCH-0109). Use the bounded runner instead of a
test filter; Reference = Intent means a filter cannot remove modules referenced by the test assembly.

```pwsh
# Default: 16 deterministic Core bootstrap contracts, no external infrastructure
./scripts/test-bootstrap.ps1

# 16 real AddKoan() pillar proofs using only in-process backends
./scripts/test-bootstrap.ps1 -Lane Pillars

# 7 explicit Redis, ONNX, and sqlite-vec proofs
./scripts/test-bootstrap.ps1 -Lane Infrastructure

# Run the three lanes in cost order
./scripts/test-bootstrap.ps1 -Lane All
```

The runner applies separate build/run deadlines and requires a nonzero xUnit execution summary.
Override a deadline only when diagnosing a known machine constraint, for example
`-BuildTimeoutSeconds 180 -RunTimeoutSeconds 240`. A timeout kills only that lane's child process tree
and reports its lane, phase, project, command, deadline, and captured diagnostics. Infrastructure facts
are xUnit-explicit as an intent marker. The project also sets `IsTestProject=false` so the default
solution invocation cannot accidentally start Docker or native model work: xUnit/VSTest can initialize
a class fixture even when every fact is explicit. Direct `dotnet test` is intentionally not an
execution path for that project; use the bounded runner shown above.

```pwsh
dotnet test Koan.sln
```

For targeted validation, invoke an individual suite project:

```pwsh
dotnet test tests/Suites/Data/Core/Koan.Tests.Data.Core/Koan.Tests.Data.Core.csproj
```

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
    .WithSetting("Koan:Data:Redis:ConnectionString", redis.ConnectionString)  // ARCH-0080 canonical key
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
- Runs as a neutral **`Test`** environment by default (override with `WithEnvironment(...)`). This is
  load-bearing: a bare `HostBuilder` defaults `IHostEnvironment` to **Production**, which trips the
  relational DDL guard (`IsDdlAllowed = DdlPolicy==AutoCreate && (!KoanEnv.IsProduction || AllowProductionDdl)`)
  — so durable relational adapters (Postgres, SQL Server) silently refuse to auto-create tables and every
  data op fails with `relation "…" does not exist`. `Test` is non-production (DDL allowed) without arming
  `Development` self-orchestration heuristics. (SQLite is unaffected — its bridge always allows AutoCreate DDL,
  which is exactly why a SQLite-only matrix can hide this.)

### Why bother

Three commits on `feat/koan-cache-pillar` proved the canon's value before it was even codified:

| Bug class | Surfaced by | Why unit tests missed it |
|---|---|---|
| `TryAddEnumerable<TService>(factory)` indistinguishable-descriptor throw | SQLite integration test (first thing to combine `AddKoanCache + adapter` in one DI graph) | Unit tests hand-roll their DI graphs and skip `AddKoanCache` |
| `CacheWriteOptions.GetEffectiveL1Ttl` not clamped to L2 | Redis SWR integration test | Unit assertions encoded the buggy behavior as "expected" |
| Cross-pillar `IConnectionMultiplexer` registration race | Full-DI bootstrap smoke | Unit tests never compose adapter packages |
| `StartupProbeService` aborts host startup on any infra adapter unavailability | Attempt to write per-pillar boot smokes against a project transitively referencing Redis | Unit tests never start real hosted services through `IHost.StartAsync` |
| Durable Jobs entities never schema-created on Postgres / SQL Server (host defaulted to Production → DDL guard) | Jobs per-DB convergence matrix — the first integration suite to drive a DDL-gated adapter through `KoanIntegrationHost` | Unit tests and the SQLite tier never hit the production-DDL guard |

Without the integration tests, these would have shipped. With them, they're caught at PR time.

### Adding an adapter integration test

1. Create `tests/Suites/<Domain>/<Adapter>/Koan.<Domain>.<Adapter>.Tests/` (mirrors the adapter
   project's path under `src/`).
2. Reference `src/Koan.Testing.Hosting` and `src/Koan.Testing.Containers`, plus the
   adapter's project + its pillar core.
3. Use `KoanIntegrationHost.Configure().ConfigureServices(s => s.AddKoan()).StartAsync(ct)` to build
   the host (or derive from `KoanDataSpec<TFixture>` and call `BootAsync()`). Don't invoke
   `new KoanAutoRegistrar().Initialize(services)` manually — that bypasses the reflective-discovery
   path that production apps actually use.
4. For adapters that need real infrastructure, take the matching `KoanContainerFixture` as an
   `IClassFixture<T>` and gate the body with `Assert.SkipWhen(!fixture.IsAvailable, fixture.Reason)`.
   The container starts once per test class and is torn down on dispose.

### Exemptions

The canon's only standing exemption is orchestration adapters (Docker, Podman, Compose
renderers) — the adapter under test IS the container runtime, so Testcontainers is recursive.
Each must instead ship an alternative integration test (e.g., filesystem fixture asserting the
shape of a generated Compose file). Other exemptions require an ADR amendment.
