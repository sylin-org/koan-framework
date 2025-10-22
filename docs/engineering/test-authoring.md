---
type: ENGINEERING
domain: engineering
title: "Test authoring guidance"
audience: [developers, maintainers]
status: current
last_updated: 2025-10-09
framework_version: v0.6.3
validation:
  status: drafted
  scope: docs/engineering/test-authoring.md
---

# Test authoring guidance

## Contract
- **Scope**: Creating or migrating tests into the greenfield Koan testing platform located under `tests/`.
- **Inputs**: Legacy coverage references (`tests.old`), suites described in [TEST-0002](../decisions/TEST-0002-test-parity-migration-roadmap.md), the shared `Koan.Testing` harness, and module-specific fixtures or seed packs.
- **Outputs**: Spec projects that adhere to the TestPipeline workflow, live under the correct pillar directory, and register any new fixtures or seed data.
- **Failure modes**: Suites landing outside the pillar structure, bypassing TestPipeline, duplicating fixtures, or omitting deterministic data and diagnostics.
- **Success criteria**: Every new spec uses TestPipeline arrange/act/assert semantics, lives in `Suites/<Domain>/<Scope>/`, consumes shared fixtures instead of bespoke helpers, and ships with runnable instructions plus updated coverage status in TEST-0002.

## Pillar-first checklist

1. **Pick the target pillar** – Core → Data → Web → AI → Jobs → Storage → Media → Cache → Canon (follow TEST-0002 ordering before touching phase-two work).
2. **Trace legacy coverage** – locate the original tests under `tests.old` and copy over only the behaviors the pillar still requires.
3. **Select suite location** – `tests/Suites/<Pillar>/<Scope>/<ProjectName>/`.
4. **Reference the harness** – add a project reference to `tests/Shared/Koan.Testing/Koan.Testing.csproj`.
5. **Describe the suite** – create or update `testsuite.yaml` with lane, scope, module, and short description.
6. **Implement specs** – build scenarios with `TestPipeline` plus shared fixtures; keep each spec self-validating.
7. **Seed data** – store deterministic inputs in `tests/SeedPacks/` when scenarios require persisted artifacts.
8. **Update documentation** – mark progress in [TEST-0002](../decisions/TEST-0002-test-parity-migration-roadmap.md) and add suite links to module README/TECHNICAL companions when relevant.

## Directory structure

```
/tests
  Shared/Koan.Testing/        # Harness + fixtures
  SeedPacks/<name>.json       # Deterministic inputs (optional)
  Suites/
    <Pillar>/
      <Scope>/                # e.g., Unit, Integration, Connector.SqlServer
        <ProjectName>/
          Specs/<Feature>/    # Group specs by behavior not layer
          testsuite.yaml
          GlobalUsings.cs
```

- **Scope naming**: Use `Unit`, `Integration`, `Connector.<Provider>`, or domain-specific slices (`Core`, `Api`) to match the parity roadmap.
- **Project name**: Prefer `Koan.Tests.<Domain>.<Scope>` for consistency. Sample-specific suites follow `S<num>.<Name>` conventions.

## Implementing specs

- **Always use `TestPipeline`**: Arrange via `.UsingServiceProvider()` or fixtures, Act through asynchronous delegates, and assert with the provided context. Avoid inline `new TestContext()` calls.
- **Pass the suite’s `ITestOutputHelper`** so failures surface contextual logs.
- **Leverage shared fixtures**: Docker, Redis, Postgres, and Mongo helpers already exist under `tests/Shared/Koan.Testing/Infrastructure`; reuse them rather than spinning new containers manually.
- **Reset framework caches**: When specs mutate entity metadata (events, adapters, partitions), call the provided hooks (`EntityEventTestHooks.Reset<TEntity, TKey>()`, `TestHooks.ResetDataConfigs()`) during Arrange to prevent cross-test leakage.
- **Stream and pagination defaults**: Honor data access guardrails—use `Entity<T>.AllStream(...)` or paging helpers in data-heavy specs.
- **No hard sleeps**: When timing matters, drive time through harness abstractions (e.g., inject clock services or expose purge timestamps) instead of `Task.Delay`.
- **Validate deterministic outputs**: When the spec relies on generated IDs, capture them from events or metadata before assertions so tests stay stable.

### Sample pattern

```csharp
[Fact]
public Task Some_behavior()
    => TestPipeline.For<MySpec>(_output, nameof(Some_behavior))
        .UsingServiceProvider("services", ConfigureServices)
        .Arrange(ctx =>
        {
            using var scope = ctx.CreateServiceScope("services");
            ctx.SetItem("runtime", scope.ServiceProvider.GetRequiredService<IMyRuntime>());
        })
        .Act(async ctx =>
        {
            var runtime = ctx.GetRequiredItem<IMyRuntime>("runtime");
            var result = await runtime.ExecuteAsync(ctx.Cancellation);
            ctx.SetItem("result", result);
        })
        .Assert(ctx =>
        {
            var result = ctx.GetRequiredItem<MyResult>("result");
            result.Status.Should().Be(MyStatus.Success);
            return ValueTask.CompletedTask;
        })
        .RunAsync();
```

### Resolver + discovery scenarios

- **Contract**
  - **Inputs**: Discovery target assemblies (for example `Koan.Cache.Adapter.*`), the resolver under test, and a suite that already references the concrete adapter packages.
  - **Outputs**: Deterministic assertions proving the resolver locates known registrars, rejects unknown adapters, and tolerates mixed casing.
  - **Failure modes**: Forgetting to reference adapter assemblies (registrars never load), omitting an explicit `typeof(SomeRegistrar)` to trigger JIT loading, or skipping negative-path coverage.
  - **Success criteria**: Each resolver spec primes assemblies explicitly, verifies happy + error paths, and exercises case-insensitive lookup semantics.

- **Prime discovery explicitly**: Call `_ = typeof(MyAdapterRegistrar);` inside the spec before resolving to make sure trim/linker friendly builds don’t skip static constructors.
- **Reference every adapter package**: The spec project (`*.csproj`) must include `ProjectReference` entries for each adapter you expect the resolver to discover; otherwise DependencyContext won’t surface them.
- **Cover the edges**:
  - Success for each in-box adapter (`memory`, `redis`, etc.).
  - Case variance (`"MeMoRy"`) to enforce ordinal-insensitive behavior.
  - Unknown adapter names throwing `InvalidOperationException` with the canonical message.
- **Keep specs short**: Use the standard `TestPipeline` harness with `.Assert(...)` and return `ValueTask.CompletedTask` so resolver tests run instantly without external fixtures.

```csharp
[Fact]
public Task Resolve_returns_redis_registrar()
    => TestPipeline.For<CacheAdapterResolverSpec>(_output, nameof(Resolve_returns_redis_registrar))
        .Assert(_ =>
        {
            _ = typeof(RedisCacheAdapterRegistrar);

            var registrar = CacheAdapterResolver.Resolve("redis");
            registrar.Should().BeOfType<RedisCacheAdapterRegistrar>();
            return ValueTask.CompletedTask;
        })
        .RunAsync();
```

## Guardrails for new fixtures

- Place reusable fixtures under `tests/Shared/Koan.Testing/Fixtures` and expose convenience extensions in `Pipeline/` to keep spec bodies terse.
- Document required environment variables in the fixture summary and ensure they respect Docker probe fallbacks (see `DockerEnvironment`).
- Reject fixtures that duplicate framework services—prefer registering actual module services and exercising real pipelines.

### Data core runtime fixture

- Use `DataCoreRuntimeFixture` (under `tests/Suites/Data/Core/Koan.Tests.Data.Core/Support`) when a suite needs a fully wired `Koan.Data` runtime with file-backed JSON storage, optional SQLite, and temp root isolation.
- Acquire it via `TestPipeline.For(...).Using<DataCoreRuntimeFixture>(...)`, then call `BindHost()` before asserting behaviors that rely on ambient `AppHost.Current`.
- Dispose partitions through the provided `UsePartition` lease to guarantee clean teardown and temp directory cleanup.

## Validation workflow

1. Run the consolidated solution tests when possible:

  ```pwsh
  dotnet test Koan.sln
  ```

2. Run the suite locally:

   ```pwsh
   dotnet test tests/Suites/<Pillar>/<Scope>/<ProjectName>/<ProjectName>.csproj
   ```

3. If the suite has integration dependencies (Docker, external services), run with diagnostics enabled:

   ```pwsh
   dotnet test tests/Suites/<Pillar>/<Scope>/<ProjectName>/<ProjectName>.csproj --logger "trx;LogFileName=TestResults.trx"
   ```

4. For documentation updates, execute `scripts/build-docs.ps1 -Strict` to keep references valid.

Capture results in commit messages or PR descriptions so reviewers see the validation evidence.

## Reporting progress

- Update the checklist in [TEST-0002](../decisions/TEST-0002-test-parity-migration-roadmap.md) when a suite lands, including PR numbers or known follow-ups.
- If a task blocks on missing fixtures or ADR decisions, annotate with `[#issue-id blocked: reason]` inside the ADR checklist.
- Retire legacy projects only after the parity checklist item closes and CI proves the replacement suite passes consistently.

## Related references

- [TEST-0001 Koan testing platform realignment](../decisions/TEST-0001-koan-testing-platform.md)
- [TEST-0002 Test parity migration roadmap](../decisions/TEST-0002-test-parity-migration-roadmap.md)
- [ADR directory](../decisions/index.md)
