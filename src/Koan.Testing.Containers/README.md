# Sylin.Koan.Testing.Containers

Use this package for xUnit v3 integration specs that must exercise a Koan data adapter against its
real backing store. It supplies shared Testcontainers fixtures, Docker-free fixtures for the
in-memory/file adapters, and `KoanDataSpec<TFixture>` for the common compiled-host workflow.

## Install

```powershell
dotnet add package Sylin.Koan.Testing.Containers
```

## Choose it when

- the behavior depends on real `AddKoan()` discovery or hosted-service startup;
- an adapter needs a reusable PostgreSQL, MongoDB, Redis, SQL Server, or Couchbase container;
- a Docker-free data spec should use the same fixture and host grammar as container-backed suites;
- each test should receive a fresh host while sharing the expensive backing-store fixture.

Do not choose it for pure unit tests or for application-level Entity conformance. Use
`Koan.Testing.Hosting` for a standalone compiled-composition host and `Koan.Testing` for the application
conformance batteries.

## Configure the test assembly

Use xUnit v3, share one fixture per engine assembly, and serialize tests that boot `KoanDataSpec`
hosts. Each host owns the process-default `AppHost` binding for its lifetime; `KoanDataSpec` does not
make concurrent test classes flow-isolated.

```csharp
using Koan.Testing.Containers;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AssemblyFixture(typeof(PostgresFixture))]
```

## Meaningful result: write a data spec

```csharp
public sealed class TodoSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    [Fact]
    public async Task Saves_and_reads_a_todo()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using var partition = Lease(NewPartition("roundtrip"));

        var saved = await new Todo { Title = "Ship" }.Save();
        (await Todo.Get(saved.Id)).Should().NotBeNull();
    }
}
```

- `RequireBackingStore()` fails with the fixture's diagnostic reason when setup did not produce a
  usable store. Optional local runs can select a Docker-free fixture; required native admission never
  treats missing infrastructure as evidence.
- `BootAsync()` starts a real `KoanIntegrationHost` with `AddKoan()` and returns an async-disposable
  `BoundHost`.
- `BootAsync(configure)` adds test services after `AddKoan()`.
- The settings overload merges per-test settings over the fixture defaults.
- `NewPartition()` and `Lease()` isolate data inside a shared backing store.
- Disposing `BoundHost` stops the host; Koan's generic-host binder releases its owner-checked ambient
  binding.

## Included fixtures

`PostgresFixture`, `MongoFixture`, `RedisFixture`, `SqlServerFixture`, and `CouchbaseFixture` use
Testcontainers. `InMemoryFixture`, `JsonFixture`, and `SqliteFixture` provide the same contract
without Docker. `CockroachFixture` is available for CockroachDB-specific suites.

An explicit `Koan_<ENGINE>__CONNECTION_STRING` environment variable selects a pre-running service;
otherwise container fixtures use Testcontainers' normal runtime discovery. A bad endpoint, unavailable
runtime, image/start failure, or stop failure remains a failed test with its original diagnostic.

## Limits

- Tests within one process must serialize host boots unless they establish their own explicit
  `AppHost.PushScope` around the complete operation flow.
- Partition isolation protects data; it does not make process-default host selection parallel-safe.
- Container-start and teardown failures fail the fixture. Use an explicit Docker-free fixture when
  the intended test contract does not require native infrastructure.

See [`TECHNICAL.md`](./TECHNICAL.md) for lifecycle and ownership details and
[`docs/guides/testing-your-app.md`](../../docs/guides/testing-your-app.md) for the broader testing
surface.
