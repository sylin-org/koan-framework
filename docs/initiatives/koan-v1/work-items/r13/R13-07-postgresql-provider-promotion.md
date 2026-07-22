---
type: SPEC
domain: framework
title: "R13-07 - Promote PostgreSQL Entity persistence"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  status: passed
  scope: focused provider, pack, public consumer, product, API, merge, and publication evidence green
---

# R13-07 — Promote PostgreSQL Entity persistence

## Outcome

Promote PostgreSQL as the first high-value networked Entity provider after the initial lean slice.
The supported product claim owns both the application-facing connector and its module-free shared
Npgsql mechanism:

- `Sylin.Koan.Data.Connector.Postgres`
- `Sylin.Koan.Data.Relational.Npgsql`

The shared package belongs because it is in the connector's public dependency closure and is the
explicit provider-author mechanism. It does not become a second application choice or activate a
provider by itself.

## Architecture checkpoint

**Application intent:** An application installs the PostgreSQL connector, calls `AddKoan()`, and uses
normal `Entity<T>` save, query, paging, and streaming operations against a reachable PostgreSQL
service.

**Public expression:** The complete common expression is one package reference, ordinary
`AddKoan()`, an `Entity<T>` model, and either autonomous local PostgreSQL discovery or
`ConnectionStrings:Postgres`. PostgreSQL is the only runtime prerequisite. No repository,
provider-registration API, or release concept enters application code.

```csharp
builder.Services.AddKoan();

public sealed class Order : Entity<Order>;

var saved = await new Order().Save();
var same = await Order.Get(saved.Id);
```

**Guarantee/correction:** The connector owns provider identity and election, configuration, source
routing, schema policy, health, and startup explanation. It realizes PostgreSQL Entity CRUD, query,
paging, provider-bounded streams, and the declared isolation modes within documented limits. The
shared Npgsql package owns only PostgreSQL-wire repository mechanics and activates nothing alone.
Unreachable services, forbidden DDL, invalid configuration, and unsupported predicates or ordering
fail explicitly instead of silently weakening the requested behavior.

**Complete intent surface:** Connector package reference; `AddKoan()`; Entity statics; optional
standard connection configuration; reachable PostgreSQL; startup facts and health; the existing
provider suite; the connector and Npgsql artifacts; and one clean external package consumer. There
are no additional application actions.

**Public concepts:** Existing `PostgresOptions` represents configuration that changes provider
guarantees. Existing `NpgsqlRepositoryOptions` is restricted to the provider-author boundary. This
promotion adds no public type, command, runner, manifest, or certification concept.

**Coalescence:** The supported SQLite claim is the closest product pattern and R13-06 is the closest
promotion pattern. Keep PostgreSQL's adapter-owned decisions, keep shared Npgsql mechanics at the
module-free relational mechanism owner, and promote both as one dependency-closed pg-wire slice.
Do not add a provider matrix, admission runner, candidate planner, certificate, or workflow.

**Ergonomics:** A user chooses one package and keeps the normal Entity grammar. IntelliSense remains
centered on `Entity<T>`; the Npgsql mechanism is visible only to provider authors. The one meaningful
configuration branch is autonomous discovery versus an explicit standard connection string.

## Evidence boundary

1. Run the existing PostgreSQL provider project against its real Testcontainers service.
2. Pack the connector and shared Npgsql mechanism with `PublicRelease=true`.
3. Restore, build, and run a clean external consumer using only the staged packages plus NuGet.org;
   the consumer boots `AddKoan()` and persists/queries an Entity against PostgreSQL.
4. Compile generated product truth and run the API-floor guard.
5. Run the cheap repository-coherence boundary; do not run unrelated provider or framework tests.

## Exit state

This card passes when the two owners declare 0.20 intent, belong to one honest supported claim, the
focused native and external-consumer evidence passes, and the resulting public artifacts are visible
on NuGet.org. The first public versions become their immutable API floors in the following promotion
slice; no nonexistent baseline is invented before publication.

## Focused evidence — 2026-07-22

- real PostgreSQL provider suite: 19/19 passed against `postgres:18.4-alpine` in 22.9 seconds;
- `PublicRelease=true` packs: connector and shared Npgsql artifacts produced successfully at the local
  first-publication version `0.20.0`;
- genuinely external package-only consumer: clean restore, zero-warning Release build, real
  Testcontainers PostgreSQL boot, `AddKoan()` provider selection, and Entity save/get/query passed
  with `POSTGRES|PACKAGE-CONSUMER|PASS`;
- generated product surface: 34 claims across 93 packages, current; and
- API posture: 42/44 configured, exactly the PostgreSQL connector and Npgsql mechanism pending first
  publication, and three content-only owners.

No sibling provider or framework-wide test ran.

## Public evidence — 2026-07-22

- PR `#96` exact-head gate `29893297175` passed as one job with no tests or containers;
- guarded merge produced main commit `b89cec6266080186db4fdd3fee99aa04b089abbc`;
- main release run `29893491621` packed the supported surface and pushed both first artifacts;
- NuGet.org indexed `Sylin.Koan.Data.Connector.Postgres 0.20.1` and
  `Sylin.Koan.Data.Relational.Npgsql 0.20.1`;
- a clean NuGet.org-only consumer built with zero warnings, selected `postgres` through `AddKoan()`,
  and passed Entity save/get/query against `postgres:18.4-alpine` with
  `POSTGRES|PUBLIC-CONSUMER|PASS`; and
- both exact `0.20.1` versions are now immutable API floors at their owning projects.

R13-07 passes. No workstation publication, tag, GitHub Release, or additional release mechanism was
used.
