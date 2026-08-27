---
type: GUIDE
domain: engineering
title: "Testing Your App — Conformance Kits"
audience: [developers, architects]
status: current
last_updated: 2026-07-19
framework_version: v1.0.0
validation:
  date_last_tested: 2026-07-19
  status: verified
  scope: EntityConformanceSpecs batteries + KoanDataSpec host ownership guidance
---

# Testing Your App — Conformance Kits

**Your app inherits a test suite.** Reference `Sylin.Koan.Testing` from your test project, write one
class per entity, and a battery of conformance specs runs through a real compiled `AddKoan()` host
(ARCH-0079) — round-trip, pushdown-vs-reference-oracle, paging, partition isolation, and (when the
entity declares the trait) cache coherence and embedding. You write one method; the batteries arrive
by inheritance and gate themselves on what the entity declares.

## One class per entity

```csharp
using Koan.Testing;

public sealed class AnimeConformance : EntityConformanceSpecs<Anime>
{
    protected override Anime NewValid() => new() { Title = "Cowboy Bebop" };
}
```

The batteries run on xUnit **v3**, which requires the test project itself to be an executable — a
fresh `dotnet new xunit` project (library-shaped, xUnit v2 packages) will not run them. The test
project references the application project plus these packages, **pinned to the proven set**:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <OutputType>Exe</OutputType>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Sylin.Koan.Testing" />
  <PackageReference Include="xunit.v3" Version="3.2.2" />
  <PackageReference Include="Microsoft.NET.Test.Sdk" />
  <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" PrivateAssets="all" />
  <ProjectReference Include="..\MyApp\MyApp.csproj" />
</ItemGroup>
```

Two version rules, both proven the hard way:

- Pin `xunit.v3` to the **3.x** line. The 4.x packages moved onto Microsoft.Testing.Platform, and
  mixing them with `Microsoft.NET.Test.Sdk` makes `dotnet test` on the .NET 10 SDK refuse to run
  ("Testing with VSTest target is no longer supported").
- If a template added the older `xunit` / v2 runner pair, remove it — mixing v2 test packages with
  the v3 runner produces a host that cannot launch.

`dotnet test` now runs, for `Anime`:

| Battery | What it pins | Gated on |
|---|---|---|
| `RoundTrip_persists_and_reads_back_by_id` | a saved entity reads back by id | always |
| `Paging_returns_every_row_exactly_once` | paging returns every row exactly once | always |
| `QueryPushdown_agrees_with_reference_evaluator` | the adapter's filter results match the shipped in-memory oracle (the bug is always the adapter, never the oracle) | `query.filter` capability |
| `Partition_isolates_writes` | a write in one partition is invisible in another | always |
| `Cacheable_invalidates_on_delete` | a delete is never served from a stale cache | `[Cacheable]` |
| `Embedding_does_not_break_the_save_path` | declaring `[Embedding]` never blocks the write | `[Embedding]` |

## Two rules

1. **Let Koan own host isolation.** Each battery binds static `Entity<T>` operations to its own real
   host through an async-flow scope. Independent conformance classes can follow normal xUnit
   scheduling; no assembly-level parallelization switch is required. If tests deliberately share one
   external database, queue, or container, coordinate that resource explicitly.

2. **Provide the backing store you intend to prove.** Each battery boots its own isolated host with
   temp storage settings for the file adapters (json, sqlite). Host composition, provider access, and
   Entity-operation failures remain test failures; only an inapplicable capability or model trait
   skips its battery. Force a no-container adapter for a Docker-free run:

   ```csharp
   protected override void Configure(IDictionary<string, string?> settings)
       => settings["Koan:Data:Sources:Default:Adapter"] = "inmemory";
   ```

## The packages

| Package | What it is |
|---|---|
| `Sylin.Koan.Testing` | the conformance kit — `EntityConformanceSpecs<T>` + the batteries (this is the one you reference) |
| `Sylin.Koan.Testing.Hosting` | the xUnit-free reflective host `KoanIntegrationHost` (ARCH-0079); reference it directly to boot a Koan host in any test |
| `Sylin.Koan.Testing.Containers` | xUnit v3 Testcontainers fixtures (`KoanContainerFixture` / `KoanDataSpec`) for engine-backed specs |

`KoanDataSpec.BootAsync()` starts a real generic host and delegates ambient ownership to Koan's host
binder. Dispose its returned `BoundHost` with `await using`; stopping an older host cannot clear a
newer owner. Data partitions isolate records, not concurrent process-default host selection.

`EntityConformanceSpecs<TEntity>` follows the same ownership rule: each inherited battery starts and
disposes a binder-owned generic host, then enters that host's flow scope for every Entity operation.
The public consumer surface remains one subclass and one `NewValid()` method.

The pushdown battery reuses the framework's own `InMemoryFilterEvaluator` (the same oracle the
cross-adapter convergence suite uses) — it is referenced, never re-implemented, so the conformance
contract and the framework's contract can never drift.

## Limits (honest)

- The pushdown battery filters on the universal `Id` field, so it pins the adapter's filter-plan path
  on every entity without needing to know your schema; richer per-property cases are yours to add.
- `Cacheable`/`Embedding` batteries gate on the class attribute and skip when absent; the embedding
  battery is a save-path smoke check (full vector-sync assertion needs a running vector store).
- A configured external provider must be reachable. Use `Koan.Testing.Containers` when the test should
  own that infrastructure and expose an explicit availability skip.
- Conformance runs are correctness-first (one host boot per battery), not a performance benchmark.

## See also

- [composition-lockfile.md](composition-lockfile.md) — what your app is composed of, the companion self-check
- [Data capability](../reference/data/index.md) — the Entity grammar the batteries exercise
