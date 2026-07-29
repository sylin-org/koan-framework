# Sylin.Koan.Testing

**Your application inherits a test suite.** Reference this package from an xUnit v3 test project,
subclass `EntityConformanceSpecs<TEntity>` once per Entity, and implement one business-valid factory
method. Koan supplies the common persistence and capability batteries through the real application
composition path.

## Install

```powershell
dotnet add package Sylin.Koan.Testing
```

## Choose it when

- an application-owned Entity should retain basic persistence behavior as its model evolves;
- a coding agent needs one canonical way to add broad, capability-aware integration coverage;
- a provider change must be checked through the same `AddKoan()` discovery path used by the app;
- reviewers want tests that read as one business-valid example rather than infrastructure wiring.

Use `Koan.Testing.Hosting` when you need a custom compiled-composition host without inherited batteries. Use
`Koan.Testing.Containers` for adapter development against reusable real backing-store fixtures.

## Certify a Data adapter

`DataConformanceManifest` is the one executable claim declaration. It projects runtime `DataCaps` into the primer's
profiles, keeps targets and declines explicit, and compiles with evidence into a deterministic
`DataConformancePacket`. An adapter certification suite inherits `DataAdapterConformanceSpecs`; the inherited theory
executes all 105 stable primer IDs and refuses missing, deferred, infrastructure-only, or contradictory evidence.

```csharp
var manifest = DataConformanceManifest.For("acme", claims => claims
    .From(DataCaps.Describe(repository, repository.GetType().Name))
    .Observe(DataConformanceProfiles.RegisteredReads, advertised: true));

var packet = DataConformancePacket.Compile(
    manifest,
    new DataConformancePacket.Identity(sourceSha, providerVersion, driverVersion, fixtureVersion),
    evidence,
    dependencies,
    rowCases);
```

Generate or validate the catalog directly from the primer, then run a packet-aware adapter gate:

```powershell
pwsh scripts/forge-verify.ps1 -CatalogOnly
pwsh scripts/forge-verify.ps1 -Adapter Acme -Plane record -Strict
```

Strict Forge exits `0` for PASS, `1` for behavioral RED/false claims, `2` for deferred proof, `3` for malformed or
stale protocol data, and `4` for unavailable provider infrastructure. No non-green state is silently skipped.

Every adapter fixture can use the same eight lifecycle modules through `DataScenarioCatalog`: fault, cancellation,
pool saturation, two-host, restart, durability, isolation, and soak. `DataBenchmarkRunner.Observe(...)` records cold
or warm elapsed time, allocations, provider dispatches, and provider work against a pinned provider/driver/runner
fixture. The TestKit deliberately carries no global performance threshold; comparison policy belongs to the exact
versioned fixture.

## Meaningful result: add one class per Entity

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

Override `Configure(IDictionary<string, string?>)` only to select an adapter or supply test
configuration. `NewValid()` is the complete Entity extension surface.

## Host isolation is automatic

Conformance batteries boot real generic hosts and bind every Entity operation to the creating host's
async flow. Independent conformance classes can use normal xUnit scheduling; no assembly-level
`DisableTestParallelization` attribute is required. Host startup/teardown still uses Koan's
owner-checked generic-host binder, so an older battery cannot clear a newer host owner.

This contract isolates Koan host/provider selection, Entity partitions, and temporary roots. A test
suite that deliberately points multiple classes at the same external database, queue, container, or
other shared resource still owns that resource's scheduling policy.

## Backing stores and failures

Each battery uses an isolated temporary root and a unique Entity partition. Override `Configure` to
select `inmemory` for a Docker-free run or to provide a real external adapter.

Only missing capabilities and absent model traits skip their inapplicable batteries. Host startup,
composition, provider access, and Entity-operation failures retain their original exception and fail
the test. A missing database is not evidence that its provider conforms.

## Limits

- These are correctness batteries, not performance or load tests.
- The universal query battery filters on `Id`; application-specific predicates still deserve tests.
- The embedding battery protects the save path, not end-to-end vector synchronization.
- Flow-scoped hosts and data partitions isolate independent conformance specifications; explicitly
  shared external infrastructure is outside that boundary.
- The suite does not replace business invariants or multi-Entity workflow tests.

See [`TECHNICAL.md`](./TECHNICAL.md) for the lifecycle and gating contract and
[`docs/guides/testing-your-app.md`](../../docs/guides/testing-your-app.md) for framework-wide testing
guidance.
