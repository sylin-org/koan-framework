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

## Guardrails for new fixtures

- Place reusable fixtures under `tests/Shared/Koan.Testing/Fixtures` and expose convenience extensions in `Pipeline/` to keep spec bodies terse.
- Document required environment variables in the fixture summary and ensure they respect Docker probe fallbacks (see `DockerEnvironment`).
- Reject fixtures that duplicate framework services—prefer registering actual module services and exercising real pipelines.

## Validation workflow

1. Run the suite locally:

   ```pwsh
   dotnet test tests/Suites/<Pillar>/<Scope>/<ProjectName>/<ProjectName>.csproj
   ```

2. If the suite has integration dependencies (Docker, external services), run with diagnostics enabled:

   ```pwsh
   dotnet test tests/Suites/<Pillar>/<Scope>/<ProjectName>/<ProjectName>.csproj --logger "trx;LogFileName=TestResults.trx"
   ```

3. For documentation updates, execute `scripts/build-docs.ps1 -Strict` to keep references valid.

Capture results in commit messages or PR descriptions so reviewers see the validation evidence.

## Reporting progress

- Update the checklist in [TEST-0002](../decisions/TEST-0002-test-parity-migration-roadmap.md) when a suite lands, including PR numbers or known follow-ups.
- If a task blocks on missing fixtures or ADR decisions, annotate with `[#issue-id blocked: reason]` inside the ADR checklist.
- Retire legacy projects only after the parity checklist item closes and CI proves the replacement suite passes consistently.

## Related references

- [TEST-0001 Koan testing platform realignment](../decisions/TEST-0001-koan-testing-platform.md)
- [TEST-0002 Test parity migration roadmap](../decisions/TEST-0002-test-parity-migration-roadmap.md)
- [ADR directory](../decisions/index.md)
