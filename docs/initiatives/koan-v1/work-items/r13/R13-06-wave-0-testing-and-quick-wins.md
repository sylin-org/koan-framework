---
type: SPEC
domain: framework
title: "R13-06 - Wave 0 Testing Substrate and Quick Wins"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-21
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-21
  status: passed
  scope: ARCH-0120 positions 1-7
---

# R13-06 — Wave 0 testing substrate and quick wins

- Status: `passed`
- Depends on: passed R13-01 through R13-05 bootstrap protections
- Owners: ARCH-0120 positions 1-7
- Unlocks: dependency-closed Wave 0 publication and Wave 1 contract admission

## Meaningful outcome

The public testing substrate fails closed around real host, external-infrastructure, and teardown
lifecycle; duplicated Web provider-container ownership converges on that substrate; and reusable Cache
behavior has one family test-kit owner. The testing family plus SQLite Cache, Data SoftDelete, Web Admin,
and the deterministic Web Auth test provider then earn supported 0.20 admission through exact owner,
consumer, artifact, and API evidence.

## Exploration checkpoint

**Task:** Admit the seven dependency-ordered Wave 0 owners as supported 0.20 packages after closing the
shared test-lifecycle, ambient-host, reusable Cache-conformance, package-consumer, and quick-win evidence
gaps required by ARCH-0120.

**Application intent:** An application author can inherit Koan's Entity conformance contract, a framework
author can boot and dispose the real compiled application host, and a provider author can exercise real
container-backed infrastructure without setup or teardown defects becoming a false pass. Application
authors can separately opt into durable local Cache, recoverable Entity deletion, authenticated
Development diagnostics, and a deterministic local OAuth/OIDC simulator through one package reference
and the existing `AddKoan()` composition.

**Public expression:** The complete expressions are:

```csharp
public sealed class OrderConformance : EntityConformanceSpecs<Order>
{
    protected override Order NewValid() => new() { Number = "A-100" };
}

await using var host = await KoanIntegrationHost.Configure()
    .ConfigureServices(services => services.AddKoan())
    .StartAsync(ct);

public sealed class OrderProviderSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output);
```

The four runtime expressions are: reference `Sylin.Koan.Cache.Adapter.Sqlite` and retain `[Cacheable]`;
reference `Sylin.Koan.Data.SoftDelete` and add `[SoftDelete]`; reference `Sylin.Koan.Web.Admin` in an
authenticated Development web host; or reference `Sylin.Koan.Web.Auth.Connector.Test` in a Development
host and start `/auth/test-oidc/challenge`. All retain the application's existing `AddKoan()` call.

**Guarantee/correction:** Host startup, test setup, readiness, Entity operations, host stop, container
stop, and owned temporary cleanup fail with their original error; dual lifecycle failures aggregate.
No shared test base assigns `AppHost.Current`; the owner-safe binder or a complete `AppHost.PushScope`
selects the intended host and repeated/overlapping hosts restore correctly. A configured external
container endpoint or required native lane that is unavailable fails admission rather than counting as
green. SQLite Cache persists the Local tier across hosts, SoftDelete hides/reveals/restores/purges only
the selected Entity type, Admin returns an authenticated and permanently sanitized Development-only
projection, and the Test auth provider exercises the maintained OAuth/OIDC callback pipeline while
remaining inactive outside Development unless explicitly enabled. Each correction names the missing
host, provider, infrastructure, configuration, policy, or protocol input.

**Complete intent surface:** The three testing package projects and public types; all nine concrete
fixtures; the Web adapter-surface container helpers and host bases; the record/vector conformance host
bases that later native lanes consume; existing Cache cross-engine and SQLite persistence suites; the
SoftDelete, Admin, Web Auth unit/integration, bootstrap lifecycle, and testing meta suites; package-owned
README/TECHNICAL files; project-local version intent; exact package artifacts; `product/claims.json`;
generated product truth; deterministic/native admission declarations; and the bounded R13 terminal
reconciler. No application repository, inline endpoint, test DSL, fixture registry, release coordinator,
or second maturity ledger is introduced.

**Public concepts:** `KoanIntegrationHost`/`IntegrationHost` express a real generic-host lifecycle;
`EntityConformanceSpecs<TEntity>` expresses inherited application Entity guarantees;
`KoanContainerFixture`, its concrete fixtures, and `KoanDataSpec<TFixture>` express explicit provider-test
infrastructure and partition ownership. `[Cacheable]`, `[SoftDelete]`, standard ASP.NET authorization,
and Web Auth provider definitions are existing business concepts. Couchbase fixture query/management
facts are exposed only because a real consumer needs those endpoints to reset its owned test store.

**Docs read:** `docs/engineering/index.md` establishes controller/entity/constants/package guardrails;
`docs/architecture/principles.md` requires business-shaped APIs, standard .NET ownership, compiled
composition, and corrective failure; `docs/toc.yml`, root `README.md`, and `samples/CATALOG.md` identify
current public truth and retired history; `docs/engineering/test-authoring.md` owns the three-ring,
real-host, `PushScope`, cancellation, and fixture rules; ARCH-0091 owns the idiomatic xUnit v3 and
Testcontainers boundary; ARCH-0120 and the R13 parent own the exact Wave 0 exit gate; R11-02/R11-05
freeze all seven `keep` dispositions and current user guarantees.

**Code read:** `KoanIntegrationHost.cs` owns generic-host construction and fail-loud async disposal;
`EntityConformanceSpecs.cs` owns application conformance and flow-scoped host selection;
`KoanContainerFixture.cs`/`KoanDataSpec.cs` own provider fixture and host grammar but currently swallow
container setup/teardown classification; Web AdapterSurface duplicates five Testcontainers owners and
directly mutates the ambient host; `CrossEngineCacheBehaviorSpecBase.cs` contains the reusable Cache
contract but is trapped inside one executable project; the current SQLite Cache, SoftDelete, Admin, and
Auth Test suites already prove their core runtime deltas and identify the exact missing admission cells.

**Reusing:** standard `IHost`, `IAsyncDisposable`, `IAsyncLifetime`, Testcontainers module builders,
`AppHost.PushScope`, `EntityContext.Partition`, xUnit/TRX result parsing, the R13 admission runner, existing
family suites, project-local NBGV version intent, SDK package validation, product-surface compilation,
and terminal-outcome reconciliation.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| Wave 0 execution record | `docs/initiatives/koan-v1/work-items/r13/R13-06-wave-0-testing-and-quick-wins.md` | one mutable record for exact claims, repairs, commands, and results |
| container lifecycle meta-tests | `tests/Suites/Testing/Koan.Testing.Containers.Tests/` | prove public fail-closed setup/teardown and one real repeated-container lifecycle without coupling the product library to tests |
| reusable Cache conformance test kit | `tests/Suites/Cache/AdapterSurface/Koan.Cache.AdapterSurface.TestKit/` | family-owned semantics reusable by SQLite now and Redis later without adding Cache dependencies to `Sylin.Koan.Testing` |
| shared Web container bridge | `tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.TestKit/Containers/KoanWebContainerHelper.cs` | one non-product adapter from Web reset needs to the public container fixtures; deletes five duplicated Testcontainers owners |
| package-only Wave 0 consumer proof | `tests/Koan.Packaging.Tests/Wave0PackageConsumerTests.cs` | pack the exact seven candidates, restore outside the repository from a local feed, compile/run their public expressions, and verify dependency/artifact boundaries |

**Coalescence:** Closest pattern: `KoanIntegrationHost`, `KoanContainerFixture`, and the Data AdapterSurface
test kit. Keep all seven R11 package dispositions. Keep generic-host law in Hosting, application Entity
conformance in Testing, heavy provider infrastructure in Testing.Containers, and runtime guarantees in
their existing product owners. Absorb Web's five duplicated container constructors into the public
fixtures; extract Cache behavior into one family test-kit owner; rebuild fail-open lifecycle and ambient
selection around owned disposal and `PushScope`; delete direct `AppHost.Current` assignment, swallowed
touched-base teardown, and duplicated container construction. Core is too wide to own test infrastructure;
`Sylin.Koan.Testing` is too narrow to take Cache dependencies; a universal harness is explicitly rejected.

**Ergonomics:** Humans and coding models still choose one of three testing verbs—inherit a contract, boot
a host, or use a provider fixture. IntelliSense exposes no registry or pipeline DSL. Quick-win application
code remains reference plus existing business decoration/configuration. Failure output maps directly to
the host, fixture, admission cell, policy, or protocol endpoint, and package-only proof prevents repository
project references from hiding missing transitive assets.

**Constraints satisfied:**

- no inline HTTP endpoints; existing controller routes remain the only HTTP surface;
- no repository abstraction or new data-access path; Entity statics remain canonical;
- stable product identifiers remain in existing package constants/options; test-only infrastructure names
  stay in the owning test fixture boundary;
- no large-data behavior changes;
- package README/TECHNICAL and current generated product docs change with admitted behavior;
- no new runtime hot path, maturity ledger, release format, or universal test framework;
- no publication, private dogfood, credential, remote mutation, or later-wave promotion.

**Risks:** The public container base previously converted every start exception into an optional skip and
swallowed every stop exception; making that boundary honest intentionally turns infrastructure defects red.
The Web matrix has legacy environment-variable aliases that are not part of the accepted public fixture
contract and will converge on `Koan_<ENGINE>__CONNECTION_STRING`. A real native container cell depends on
the native lane's Docker availability and is inconclusive if skipped. Newly admitted packages intentionally
have no SDK API baseline until their first immutable public 0.20 artifact exists; R13-01's publisher guard
allows exactly that first publication and rejects the next patch until the earliest version is recorded.

## Acceptance

1. All seven owners have supported claims, project-local 0.20 intent, exact deterministic/native cells,
   package-owned docs, focused owner proof, and package-only consumer evidence.
2. Touched shared test setup/teardown fails closed and repeated/overlapping host ownership is green.
3. Web AdapterSurface no longer constructs duplicate provider containers or directly assigns the ambient
   host in its shared bases.
4. SQLite Cache runs one reusable family contract plus its persistence/tag/sliding-expiration delta.
5. Product truth compiles without drift, API baselines report the seven owners as first-publication pending,
   and terminal reconciliation reports 7/55 active supported.
6. No commit, push, publication, tag, release, deployment, private evidence, or full release ratchet occurs.

## Implementation result

- `KoanContainerFixture`, `KoanDataSpec`, `EntityConformanceSpecs`, and `KoanIntegrationHost` now
  preserve original failures, aggregate dual lifecycle faults, honor test cancellation, and never
  convert required provider infrastructure into a green skip.
- The Web AdapterSurface test kit delegates its five external engines to the public container
  fixtures, and touched shared Web/vector host bases no longer assign `AppHost.Current` directly.
- Cache behavior now lives in one reusable provider-neutral family test kit. SQLite runs that kit
  plus its owned durability/tag/sliding-expiration delta and clears its connection pool on disposal,
  so owned files are actually released.
- Admin proves overlapping host ownership and uses deterministic test logging. The Test auth
  protocol host owns ephemeral Data Protection and deterministic logging, so OAuth/OIDC evidence is
  independent of a workstation key ring and Windows Event Log permissions.
- The seven candidates carry `0.20` intent, five supported claims own exact admission cells, the PR
  gate derives and executes every deterministic claim cell, and the native workflow owns the exact
  Docker-backed cell. A package-only application packs, restores, compiles, and runs all seven
  candidates outside the repository.
- Windows test intermediates retain the conventional project-local `obj/` exclusion after output
  redirection, preventing stale non-redirected generated sources from entering solution builds.

## Validation result

The eleven exact admission cells passed **53/53 named results with zero failed, skipped, unknown, or
timed-out results**: host lifecycle 2, Entity conformance/ownership 4, deterministic container
lifecycle 3, native Postgres repeated lifecycle 1, package-only consumer 1, SQLite Cache conformance
7, SQLite delta 5, SoftDelete 10, Admin 13, auth token 2, and OAuth/OIDC protocol 5.

Additional closure evidence:

- product truth: `33` claims / `93` packages, generated JSON and Markdown byte-current;
- API guard: `35/42` assembly baselines configured, exactly these seven first publications pending,
  and three content-only supported owners;
- terminal reconciliation: `7/55` resolved as active supported, zero removed, `48` remaining;
- public package consumer: seven candidate packages restored, built, and ran from an isolated system
  temporary directory and local feed;
- deterministic owner suites: Admin 13/13, Cache cross-engine 14/14, SQLite delta 5/5, auth protocol
  5/5, and package-baseline policy 8/8.
- complete Debug solution build: zero warnings and zero errors, including both new Wave 0 projects.

No remote mutation or publication occurred. The next boundary is maintainer-authorized publication
and public observation of this dependency-closed wave; that observation also supplies R12-07.
