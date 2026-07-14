# Koan.Testing

**Your application inherits a test suite.** Reference this package from an xUnit v3 test project,
subclass `EntityConformanceSpecs<TEntity>` once per Entity, and implement one business-valid factory
method. Koan supplies the common persistence and capability batteries through the real application
composition path.

Reference it as part of one coherent Koan package version set. Repository development uses the
project at `src/Koan.Testing`; public package-set readiness is tracked separately from this module's
runtime contract.

## Choose it when

- an application-owned Entity should retain basic persistence behavior as its model evolves;
- a coding agent needs one canonical way to add broad, capability-aware integration coverage;
- a provider change must be checked through the same `AddKoan()` discovery path used by the app;
- reviewers want tests that read as one business-valid example rather than infrastructure wiring.

Use `Koan.Testing.Hosting` when you need a custom reflective host without inherited batteries. Use
`Koan.Testing.Containers` for adapter development against reusable real backing-store fixtures.

## Add one class per Entity

```csharp
using Koan.Testing;

public sealed class TodoConformance : EntityConformanceSpecs<Todo>
{
    protected override Todo NewValid() => new() { Title = "Ship the meaningful step" };
}
```

That class inherits six batteries:

| Battery | Meaning |
|---|---|
| Round trip | A valid Entity saves, receives an id, and reads back. |
| Paging | Paging returns every seeded row exactly once. |
| Query pushdown | A capable adapter agrees with Koan's in-memory filter oracle. |
| Partition isolation | A write in one partition is invisible in another. |
| Cache invalidation | A `[Cacheable]` Entity is not served stale after deletion. |
| Embedding save path | An `[Embedding]` declaration never blocks the persistence path. |

Cache and embedding batteries skip when the Entity does not declare those traits. Query pushdown
skips when the selected adapter does not declare the required capability.

Override `Mutate(TEntity)` only when a conformance extension needs a valid changed Entity. Override
`Configure(IDictionary<string, string?>)` to select an adapter or supply test configuration.

## Configure the test assembly

Conformance batteries boot real generic hosts. Each host owns the process-default `AppHost` binding
for its lifetime, so conformance classes in one test process must run sequentially:

```csharp
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

The helper does not write or clear `AppHost.Current` itself. Host startup and teardown use Koan's
owner-checked generic-host binder, so an older battery cannot clear a newer host owner.

## Backing stores and skips

Each battery uses an isolated temporary root and a unique Entity partition. The default configuration
supports Docker-free file adapters; override `Configure` to select `inmemory` or provide a reachable
external adapter.

The initial reachability probe converts an unreachable backing store into a native xUnit skip for the
battery. Once the store is reachable, conformance failures remain loud. A skip is absence of evidence,
not evidence that the provider conforms.

## Limits

- These are correctness batteries, not performance or load tests.
- The universal query battery filters on `Id`; application-specific predicates still deserve tests.
- The embedding battery protects the save path, not end-to-end vector synchronization.
- Data partitions isolate records but do not make concurrent process-default host selection safe.
- The suite does not replace business invariants or multi-Entity workflow tests.

See [`TECHNICAL.md`](./TECHNICAL.md) for the lifecycle and gating contract and
[`docs/guides/testing-your-app.md`](../../docs/guides/testing-your-app.md) for framework-wide testing
guidance.
