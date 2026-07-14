---
type: GUIDE
domain: engineering
title: "Testing Your App — Conformance Kits"
audience: [developers, architects]
status: current
last_updated: 2026-07-14
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-14
  status: verified
  scope: EntityConformanceSpecs batteries + KoanDataSpec host ownership guidance
---

# Testing Your App — Conformance Kits

**Your app inherits a test suite.** Reference `Sylin.Koan.Testing` from your test project, write one
class per entity, and a battery of conformance specs runs through a real reflective `AddKoan()` host
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

`dotnet test` now runs, for `Anime`:

| Battery | What it pins | Gated on |
|---|---|---|
| `RoundTrip` | a saved entity reads back by id | always |
| `Paging` | paging returns every row exactly once | always |
| `QueryPushdown_agrees_with_reference_evaluator` | the adapter's filter results match the shipped in-memory oracle (the bug is always the adapter, never the oracle) | `query.filter` capability |
| `Partition_isolates_writes` | a write in one partition is invisible in another | always |
| `Cacheable_invalidates_on_delete` | a delete is never served from a stale cache | `[Cacheable]` |
| `Embedding_does_not_break_the_save_path` | declaring `[Embedding]` never blocks the write | `[Embedding]` |

There is a second, optional hook — `protected override TEntity Mutate(TEntity e)` — and nothing else.

## Two rules

1. **Run sequentially.** Each booted host owns the process-default ambient provider used by the
   static `Entity<T>` API. The testing helpers use the host's owner-checked lifecycle binding, but do
   not make concurrent test classes flow-isolated. Add this once to your conformance test project:

   ```csharp
   [assembly: CollectionBehavior(DisableTestParallelization = true)]
   ```

2. **Provide a backing store (or let it skip).** Each battery boots its own isolated host with temp
   storage for the file adapters (json, sqlite). If your entity's adapter needs a container that isn't
   running, the batteries **skip cleanly** rather than fail. Force a no-container adapter for a
   Docker-free run:

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

The pushdown battery reuses the framework's own `InMemoryFilterEvaluator` (the same oracle the
cross-adapter convergence suite uses) — it is referenced, never re-implemented, so the conformance
contract and the framework's contract can never drift.

## Limits (honest)

- The pushdown battery filters on the universal `Id` field, so it pins the adapter's filter-plan path
  on every entity without needing to know your schema; richer per-property cases are yours to add.
- `Cacheable`/`Embedding` batteries gate on the class attribute and skip when absent; the embedding
  battery is a save-path smoke check (full vector-sync assertion needs a running vector store).
- Conformance runs are correctness-first (one host boot per battery), not a performance benchmark.

## See also

- [composition-lockfile.md](composition-lockfile.md) — what your app is composed of, the companion self-check
- [data-modeling.md](data-modeling.md) — the entity grammar the batteries exercise
