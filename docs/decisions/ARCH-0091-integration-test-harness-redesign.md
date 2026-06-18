# ARCH-0091: Integration-test harness redesign — Testcontainers modules + idiomatic xUnit v3

**Status**: Accepted (2026-06-18) — execution is pilot-first (Postgres) then staged rollout
**Date**: 2026-06-18
**Deciders**: Enterprise Architect
**Scope**: Resolves assessment card **X-testcontainers-modernize**, re-derived from "bump the dead Testcontainers package" into a full harness redesign at the architect's explicit direction ("get the architecture right — switch underlying technology, adopt other frameworks, check prior art; blast radius is irrelevant"). Replaces the bespoke `tests/Shared/Koan.Testing` container harness with the idiomatic modern .NET pattern, and migrates the test suite from xUnit v2 to v3.
**Related**: ARCH-0079 (integration tests as canon — the reflective `AddKoan()` host requirement, **preserved verbatim**) · the in-repo `tests/Suites/Jobs/**` adapter fixtures (the already-proven modern pattern this generalizes) · JOBS-0005 (the Jobs suite that established `PostgreSqlBuilder` + `IAsyncLifetime` in-repo).
**Prior art**: a 6-agent web-grounded research pass (Testcontainers .NET docs, xUnit v3 docs, EF Core / Marten / RavenDB / Pomelo / OrmLite test harnesses, Aspire.Hosting.Testing, TUnit, Respawn). Findings converged; the dissents (Aspire, TUnit, Respawn) are recorded as explicit rejections below.

---

## Problem

The shared harness under `tests/Shared/Koan.Testing` is **bespoke reinvention bloated by a dead dependency**:

- **A dead container stack.** `DotNet.Testcontainers 1.7.0-beta.2269` (a ~4-year-old pre-rename beta) throws `MissingMethodException` on net10. Each of the 7 per-engine fixtures (`PostgresContainerFixture` is 654 lines) is ~half `MissingMethodException`→`docker run` **CLI-fallback machinery**, plus an env-var override → **local-instance port-scan + credential-guess** cascade, plus a hand-rolled `DockerDaemonFixture`/`DockerEnvironment` daemon probe using `Docker.DotNet` directly.
- **A bespoke test DSL.** `TestContext` (a per-scenario item bag), `TestPipeline` (an Arrange/Act/Assert runner), **two parallel fluent extension families** (`TestContext*Extensions` *and* `TestPipeline*Extensions`), `FixtureRegistry`, `IInitializableFixture`, `TestFixtureHandle` — all reimplementing what xUnit fixtures + `IAsyncLifetime` provide natively.
- **A broken skip story.** `RequireDocker()` **throws** when Docker is absent, so the container suites *fail* rather than skip.
- **Wasteful lifecycle.** `UsingPostgresContainer()` builds a **new container per test method**.

Meanwhile the repo **already contains the right pattern**: the JOBS-0005 adapter fixtures (`tests/Suites/Jobs/.../PostgresJobsFixture.cs`) are ~38-line `IAsyncLifetime` fixtures over a Testcontainers 4.x **module builder** (`new PostgreSqlBuilder(...).Build(); GetConnectionString()`) — no CLI fallback, no probe, no bespoke harness. The redesign is mostly **deletion + generalization of an in-repo pattern**, not invention.

---

## Decision 1 — Container layer: **Testcontainers 4.x official module builders**

Adopt the official per-engine `Testcontainers.<Engine>` 4.11.0 module packages — confirmed to exist and restore for **all eight** engines Koan tests: PostgreSql, MongoDb, Redis, Couchbase, Elasticsearch, MsSql, OpenSearch, Weaviate. Each fixture's container construction becomes `new <Engine>Builder(image).…Build(); await StartAsync(); GetConnectionString()`.

**Deleted as obsolete** (the library subsumes each):
- the dead `DotNet.Testcontainers 1.7.0-beta.2269` package and the `MissingMethodException`→`docker run` CLI fallback (4.x works natively on net10);
- `DockerDaemonFixture` + `Infrastructure/DockerEnvironment` + the **`Docker.DotNet`** dependency (Testcontainers 4.x performs native Docker-host auto-discovery — `DOCKER_HOST`, named pipe, unix socket, Docker context, Desktop/Rancher/Podman);
- the **local-instance port-scan + credential-guess** cascade (`TryDetectLocal`) — it is fragile *and dangerous* (it can silently bind tests to a developer's unrelated local database). The only legitimate piece kept is an **explicit env-var connection override** (`Koan_<ENGINE>__CONNECTION_STRING`) for CI lanes that provide a pre-running service, lifted into the shared base.

Generic `ContainerBuilder` (already used in-repo by the vector `*TestFactory` files on 4.11) remains the fallback for any engine lacking a module — none of the eight need it.

## Decision 2 — Lifecycle: **idiomatic xUnit fixtures over a shared base; delete the bespoke DSL**

Each engine gets one thin fixture (`PostgresFixture : KoanContainerFixture`, ~30–40 lines) that only: starts the module container, exposes a `Settings` dictionary (`Koan:Data:Sources:Default:*`), and reports `IsAvailable`/`Reason`. A small shared abstract base `KoanContainerFixture` (modeled on the peer idiom — EF Core's inheritable `TestStore`, Marten's `DefaultStoreFixture`, RavenDB's `RavenTestDriver`; optionally subclassing the official `Testcontainers.XunitV3` `DbContainerFixture`) owns the `IAsyncLifetime` start/stop, the env-override branch, and the Docker-absent catch.

**Deleted:** `TestPipeline`, `TestContext`, both `*Extensions` families, `FixtureRegistry`, `IInitializableFixture`, `TestFixtureHandle`, and the 7 legacy `*ContainerFixture` files. Diagnostics revert to constructor-injected `ITestOutputHelper`. `SeedPackFixture`/`SeedPackLocator` are retired unless a live consumer is found (only one non-harness reference exists; verify during rollout).

## Decision 3 — Test framework: **migrate xUnit v2 → v3**

Migrate all non-fenced test projects from xUnit v2 (2.9.3) to **xUnit v3 (3.2.2)**, run in **VSTest-compatible mode** (`xunit.v3` + `xunit.runner.visualstudio 3.1.5` + `Microsoft.NET.Test.Sdk 18.6.0`, `<OutputType>Exe</OutputType>`). Validated end-to-end: `dotnet test` works unchanged (no Microsoft.Testing.Platform runner switch), so `green-ratchet.ps1`'s `dotnet test Koan.sln` is unaffected.

v3 is chosen (over staying on v2) because it provides the two features the 8-engine matrix wants natively, with no third-party shim:
- **`[assembly: AssemblyFixture(typeof(T))]`** — one container per engine assembly, shared across all classes, **without** the sequential-execution penalty v2 `ICollectionFixture` imposes.
- **native `Assert.Skip(...)`** — the clean Docker-absent skip (Decision 5), replacing the throwing `RequireDocker()` and the `Xunit.SkippableFact` package.

`IAsyncLifetime` in v3 returns `ValueTask` and inherits `IAsyncDisposable` (mechanical migration).

## Decision 4 — Isolation: **keep Koan's per-execution partition; reject Respawn / transaction-rollback**

A container is **shared per engine assembly** (Decision 3), so cross-test isolation is by **Koan's existing per-execution partition primitive** (`EntityContext.Partition($"{engine}-{ExecutionId:n}")`) — the engine-agnostic reset that already works uniformly across SQL + Mongo + Couchbase + Redis + vector, including transactionless stores. Specs in one container partition-isolate and therefore **parallelize**. The logical reset verb (`DataInstructions.Clear` / `RemoveAll(RemoveStrategy.Safe)`) runs on setup **and** teardown as a defensive net — never drop-and-recreate (the JobsHarness lesson: DDL churn flakes Mongo across rapid host cycles).

**Rejected:** **Respawn** (relational-only — 2 of 8 engines; a second SQL-only mechanism re-fragments the harness) and **transaction-rollback** (≈3 of 8; structurally impossible on the transactionless stores; can't test commit behavior). Two narrow carve-outs keep a virgin container/db: specs that must verify the read path *honors* the partition filter (global-scan / cross-tenant-leak), and specs needing a fresh schema (creation idempotency, first-write index builds).

## Decision 5 — Skip: **native `Assert.Skip` on container-start failure**

The shared base catches the Testcontainers "Docker not available" start exception and sets `IsAvailable=false`/`Reason`; specs guard with `Assert.Skip($"Docker unavailable: {fixture.Reason}")`. Replaces today's `RequireDocker()`-throws (suites that *fail* without Docker) with genuine skips. (This finally makes the B2 pr-gate honest: container suites *skip* on a runner without Docker instead of going red.)

## Decision 6 — ARCH-0079 host: **`KoanIntegrationHost` unchanged; isolated into an xunit-free project**

`KoanIntegrationHost` (the bootstrap-agnostic reflective `AddKoan()` host, ARCH-0079 canon) is **kept verbatim** — the fixture hands it the `Settings` dict and the spec still boots real reflective Koan. The container topology and the reflective-host requirement are orthogonal.

**Project topology change (required by the boundary, Decision 7):** `KoanIntegrationHost` is xunit-agnostic (pure `Microsoft.Extensions.Hosting`). It is moved into an **xunit-free** shared project so a **v2** consumer can reference it across the v2/v3 split without an `Xunit.*` assembly collision. Concretely: `Koan.Testing` is stripped to the xunit-free host/helper surface (`KoanIntegrationHost`, …), and the new **v3** container fixtures + `KoanContainerFixture` base live in a new **`Koan.Testing.Containers`** project (references `xunit.v3` + the `Testcontainers.*` modules). Container test projects reference both; non-container projects reference only the xunit-free host lib.

## Decision 7 — Concurrency boundary: **Jobs projects stay v2 and untouched**

`tests/Suites/Jobs/**` is under active concurrent work and must not be edited. Empirically only `Koan.Jobs.TestKit` references `Koan.Testing`, and the Jobs tests use **only `KoanIntegrationHost`** (no bespoke type being deleted). The contract that keeps the fenced suites green: **`KoanIntegrationHost`'s public surface and its xunit-free project stay stable.** Decision 6 guarantees this. The Jobs projects remain xUnit v2; the solution is therefore **mixed v2(Jobs)/v3(rest)** until the Jobs boundary clears — safe because v3 runs in VSTest-compatible mode (both adapters coexist under `dotnet test`). Migrating the Jobs projects to v3 is a tracked follow-on.

## Decision 8 — Rejected substrates

- **Aspire.Hosting.Testing — rejected as the matrix substrate.** Structural misfit with ARCH-0079: `DistributedApplicationTestingBuilder` runs the AppHost **out-of-process** and launches resources as separate processes, so `AddKoan()` would execute inside a spawned resource the spec cannot reach into and assert against — but ARCH-0079 requires the in-process host *be* `AddKoan()`. Every `CreateAsync` overload also demands an AppHost entry-point, so a pure data-connector spec would have to invent an AppHost just to stand up one container (more bespoke scaffolding, not less), and it offers no skip-when-no-Docker advantage. **Reserved** for a separate, additive purpose: one or two whole-app smoke tests for the *orchestration pillar itself* (`IKoanAspireRegistrar` + `AddKoanDiscoveredResources` + `[KoanService]` generation) — tracked as a follow-on card, never the data-matrix substrate.
- **TUnit — rejected.** Orthogonal scope-creep: it does nothing for the actual pain (the bespoke fixtures / dead package / CLI fallback), which plain xUnit + Testcontainers already solves in-repo, while imposing an assertion+lifecycle rewrite on ~85 projects. The reflective-host canon is framework-agnostic. Revisit only as an isolated experiment after the harness is rebuilt, if source-gen discovery or concurrent-container throttling is specifically wanted.

---

## Consequences

- **Net deletion.** The dead beta package, `Docker.DotNet`, the CLI fallback, the port-scan/credential-guess cascade, `DockerDaemonFixture`/`DockerEnvironment`, `TestPipeline`/`TestContext`/`FixtureRegistry`/both extension families/`IInitializableFixture`/`TestFixtureHandle`, and 7 fat fixtures (~200–650 lines each) all go; replaced by ~8 fixtures of ~30–40 lines + one small base. The harness becomes idiomatic xUnit any .NET developer recognizes.
- **Honest skips + faster suites.** Container suites skip cleanly without Docker; one container per engine assembly (was per test method).
- **`KoanIntegrationHost` (ARCH-0079) is preserved verbatim** and consumers re-point through the unchanged host; the reset runs through the real `AddKoan()` data layer, which *strengthens* canon (the reset exercises the same adapter capability path the specs test).
- **Blast radius (accepted):** ~50 container/integration test projects rewritten off the `TestPipeline` DSL, ~64 spec files, and the v2→v3 migration of all non-fenced projects. This was explicitly authorized.
- **Mixed v2/v3 solution** until the Jobs boundary clears (Decision 7); resolved by a follow-on once that concurrent work lands.

## Execution (pilot-first, staged)

1. **Pilot — Postgres, end-to-end:** split `Koan.Testing` (xunit-free host) + new `Koan.Testing.Containers` (`KoanContainerFixture` base + `PostgresFixture`); migrate `Koan.Data.Connector.Postgres.Tests` to v3; rewrite its specs to idiomatic xUnit (`[assembly: AssemblyFixture]` + `Assert.Skip` + `KoanIntegrationHost`); prove green via `dotnet test` against a **real Postgres container**. Architect sign-off on the proven pattern.
2. **Rollout:** the other 7 engine fixtures; rewrite the remaining specs; v3-migrate all non-fenced test projects; delete the bespoke harness; verify the full container matrix with Docker.
3. **Close-out:** PROGRESS row + Divergence; `tests/README` (ARCH-0079 section) refreshed; a `koan-testing` reference card/skill if warranted; follow-on cards filed (Jobs-projects→v3; the Aspire orchestration smoke test).
