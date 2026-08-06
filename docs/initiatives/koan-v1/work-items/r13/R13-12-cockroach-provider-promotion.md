---
type: SPEC
domain: framework
title: "R13-12 - Promote CockroachDB Entity persistence"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  status: passed
  scope: focused CockroachDB provider, health, pack, consumer, product, and API evidence
---

# R13-12 — Promote CockroachDB Entity persistence

## Outcome

Promote `Sylin.Koan.Data.Connector.Cockroach` as a supported 0.20 networked Entity provider and
capture the exact public `0.20.0` API floors of the three Redis owners published by R13-11.

## Architecture checkpoint

**Application intent:** An application installs the CockroachDB connector, calls `AddKoan()`, and
uses normal `Entity<T>` persistence against a reachable CockroachDB node.

**Public expression:** The complete common expression is one connector package reference, ordinary
`AddKoan()`, an Entity model, and either autonomous discovery or `ConnectionStrings:Cockroach`.

```csharp
builder.Services.AddKoan();

public sealed class Order : Entity<Order>;

var saved = await new Order().Save();
var same = await Order.Get(saved.Id);
```

**Guarantee/correction:** Cockroach owns provider identity/election, discovery and configuration,
source routing, additive schema policy, participation-aware health, startup reporting, and its
Cockroach-specific stable ordering and naming limits. The supported relational foundation and
module-free Npgsql mechanism realize CRUD, native query, paging, provider-bounded streams, batch and
conditional writes, and declared isolation. Unsupported SQL/order, forbidden DDL, invalid
configuration, and unavailable selected sources fail explicitly rather than silently selecting the
PostgreSQL connector or an in-memory fallback.

**Complete intent surface:** One connector reference; `AddKoan()`; Entity statics; optional existing
Cockroach connection/schema/source settings; a reachable CockroachDB node; package-owned startup and
health behavior; the existing real-provider suite; one artifact; and a clean external consumer. No
manual provider registration or PostgreSQL connector reference is required.

**Public concepts:** Existing `CockroachOptions`, Data capabilities, relational schema policy, and
`NpgsqlRepository` own every required decision. Promotion adds no public API, option, registration
path, or runtime concept.

**Coalescence:** PostgreSQL is the closest pg-wire implementation and product pattern. Keep
Cockroach's thin adapter-specific discovery, identity, route, schema, ordering, and naming delta;
keep shared Npgsql mechanics in their already-supported module-free owner; absorb Cockroach's stale
always-critical health implementation into the existing participation-owned Data health law. Add no
new framework abstraction or provider matrix.

**Ergonomics:** The application chooses one package and retains normal Entity IntelliSense.
CockroachDB and PostgreSQL remain independently selectable when both are referenced. The only common
configuration branch is autonomous discovery versus an explicit standard connection string.

## Evidence boundary

1. Add focused tests for unused and runtime-participating health behavior.
2. Run the complete CockroachDB provider project against CockroachDB 26.2.3.
3. Pack the connector with `PublicRelease=true`.
4. Restore, build, and run a clean external consumer from the staged artifact plus NuGet.org; prove
   `AddKoan()` selection and Entity CRUD/query/page/stream behavior against the real node.
5. Compile product truth, run the API guard and cheap coherence; do not run sibling providers or
   framework-wide certification.
6. After main publication, observe the exact public artifact and rerun the consumer from NuGet.org.

## Exit state

This card passes when Cockroach declares 0.20 intent, owns an honest supported claim, focused
native/health/package-consumer evidence passes, and the public artifact is visible on NuGet.org. Its
exact first public version becomes the immutable API floor in the following slice.

## Focused evidence — 2026-07-22

- architecture checkpoint and dependency closure: complete; one connector over already-supported
  relational and Npgsql foundations;
- discovered correction: replace package-availability-critical health with the existing
  participation-owned Data health law;
- participation health correction: Cockroach now reuses `DataAdapterHealthContributorBase`, resolves
  the same per-source options as repositories, stays unknown/non-critical without election or runtime
  participation, and gates readiness only for active sources;
- focused real provider suite: 7/7 passed with zero skips against `cockroachdb/cockroach:v26.2.3`,
  covering the health correction, provider-bounded streaming, and all three AODB isolation modes;
- first staged restore exposed redundant direct dependency floors below the already-supported Npgsql
  mechanism's transitive floors; keep Npgsql as the connector's one package dependency and consume
  its compile/runtime closure transitively, removing four duplicate project/package edges rather than
  suppressing NuGet downgrade protection;
- Redis connector, backend, and abstraction exact `0.20.0` API floors: recorded in their owning
  projects;
- `PublicRelease=true` pack: succeeded at local first-publication version `0.20.0`; its nuspec has one
  Koan dependency, the Npgsql mechanism that owns the complete supported pg-wire closure;
- genuinely external staged-package consumer: restored the connector exactly as `0.20.0` from the
  isolated feed plus public NuGet dependencies, built with zero warnings/errors, selected Cockroach
  through `AddKoan()`, and completed Entity save/get/native-query/page/provider-bounded-stream against
  26.2.3 with `COCKROACHDB|PACKAGE-CONSUMER|CRUD|QUERY|PAGE|STREAM|PASS`;
- generated product truth: 40 claims / 93 packages and the Cockroach owner is in the supported 0.20
  closure;
- API posture: 51/52 configured, with Cockroach as the sole allowed first-publication pending floor
  and three content-only owners;
- generated-drift check and the no-tests repository coherence gate: passed in 51.1 seconds with zero
  doc errors and 1,402 existing warnings; no sibling provider or whole-framework certification ran;
- PR `#102` exact-head gate `29902727400`: passed as the one cheap coherence job with no tests or
  containers; squash-merged to `main` as `3ff7f1950931addd12a16e194299468bd442dcdf`;
- main release `29903009583`: passed; NuGet accepted both the package and symbols as new artifacts,
  and indexed exact `Sylin.Koan.Data.Connector.Cockroach 0.20.0`;
- NuGet.org-only consumer: restored exact `0.20.0` into a new empty cache, built with zero
  warnings/errors, and repeated the real 26.2.3 Entity journey with
  `COCKROACHDB|PACKAGE-CONSUMER|CRUD|QUERY|PAGE|STREAM|PASS`;
- exact `0.20.0` API floor: recorded in the connector project by R13-13.
