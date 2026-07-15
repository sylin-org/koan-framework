---
type: ENGINEERING
domain: engineering
title: "Test authoring guidance"
audience: [developers, maintainers]
status: current
last_updated: 2026-07-14
framework_version: pre-1.0
validation:
  status: verified
  scope: tests/README.md
---

# Test authoring guidance

Koan tests use xUnit v3 directly. The former `TestPipeline`/`TestContext` DSL was removed by
ARCH-0091; do not copy it from history or add general test-harness primitives to `Koan.Testing`.
[`tests/README.md`](../../tests/README.md) is the detailed platform contract.

## Choose the smallest truthful proof

- Use a unit test for a pure policy, parser, serializer, or deterministic algorithm.
- Use `KoanIntegrationHost` when behavior depends on `AddKoan()`, discovery, DI, hosted services,
  configuration, startup ordering, or an ambient `AppHost`.
- Use `Koan.Testing.Containers` and the real provider fixture when claiming connector behavior.
- Use `EntityConformanceSpecs<TEntity>` when an application or adapter should inherit Koan's common
  entity behavior contract.

Reference = Intent is a runtime property. A hand-built service provider cannot prove that a referenced
module is discovered, ordered, started, and composed correctly.

## Place and shape the suite

1. Put it under `tests/Suites/<Domain>/<Scope>/<ProjectName>/`, mirroring the source module.
2. Use the repository's xUnit v3 package versions and `<OutputType>Exe</OutputType>`.
3. Serialize suites that intentionally rely on one process-default Koan host. When facts can nest or
   overlap hosts, select the owned provider with `AppHost.PushScope` for each complete operation.
4. Keep behavior-focused specs under `Specs/<Feature>/`.
5. Reuse seed packs from `tests/SeedPacks` and fixtures from `Koan.Testing.Containers`.
6. Add the project to `Koan.sln` unless it is an explicitly documented bounded infrastructure lane.

## Canonical integration shape

```csharp
public sealed class RuntimeFactsSpec
{
    [Fact]
    public async Task Referenced_modules_compose_through_the_real_host()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await KoanIntegrationHost.Configure()
            .WithEnvironment("Test")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync(ct);

        var runtime = host.Services.GetRequiredService<IKoanRuntimeFacts>();
        Assert.True(runtime.Current.Complete);
    }
}
```

For a provider suite, derive from `KoanDataSpec<TFixture>` or use the documented class fixture. Skip
with the fixture's concrete reason when infrastructure is unavailable; never turn missing Docker into
a false pass.

## Reliability rules

- Use `TestContext.Current.CancellationToken` and bounded polling; avoid unbounded waits and hard sleeps.
- Treat fixture lifetime and ambient host selection as separate concerns. A stopped or failed newer
  host correctly does not resurrect an earlier process default; scope fixture-owned Entity work to
  `fixture.Services` instead of assigning `AppHost.Current`.
- Give every execution an isolated partition, database, port, or temporary root as appropriate.
- Assert the user-visible contract and the failure message, not private implementation structure.
- Keep environment-dependent facts explicit. Unknown or unavailable is not success.
- When a test changes composition or startup behavior, update `docs/SURFACES.md` with its guard.
- Do not weaken a suite to accommodate a connector. State and test the connector's capability boundary.

## Run it

```powershell
dotnet test tests/Suites/<Domain>/<Scope>/<ProjectName>/<ProjectName>.csproj -c Release
dotnet test Koan.sln -c Release
```

Bootstrap infrastructure has a separate bounded runner because loading its test assembly can start
expensive fixtures even when facts are filtered:

```powershell
./scripts/test-bootstrap.ps1 -Lane All
```

## Review checklist

- Does the proof exercise the layer named by the claim?
- Does it boot real Koan when composition matters?
- Does it isolate shared state and fail with useful diagnostics?
- Is external infrastructure either real or explicitly skipped?
- Is the project discoverable from the solution or its documented bounded runner?
- Did the public capability or surface ledger change with the behavior?

Related: [ARCH-0079](../decisions/ARCH-0079-integration-tests-as-canon.md),
[ARCH-0091](../decisions/ARCH-0091-integration-test-harness-redesign.md), and
[`tests/README.md`](../../tests/README.md).
