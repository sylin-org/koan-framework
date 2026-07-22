---
type: SPEC
domain: framework
title: "R13-11 - Promote Redis backend and Entity persistence"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  status: passed
  scope: Redis backend, inert contract, and Data provider published, indexed, consumer-green, and baseline-captured
---

# R13-11 — Promote Redis backend and Entity persistence

## Outcome

Promote `Sylin.Koan.Redis` and `Sylin.Koan.Redis.Abstractions` as the supported shared Redis backend
foundation, and `Sylin.Koan.Data.Connector.Redis` as a supported 0.20 Entity provider for keyed,
ephemeral, and modest-cardinality workloads. Capture Couchbase's exact public `0.20.1` API floor in
the same slice; that baseline bookkeeping does not broaden Redis's guarantee.

## Architecture checkpoint

**Application intent:** An application installs the Redis Data connector, calls `AddKoan()`, and uses
normal `Entity<T>` persistence against a reachable Redis service. It may configure
`ConnectionStrings:Redis`; no manual backend registration is required.

**Public expression:** The common application references only the Data connector. The functional
backend and inert connection contract arrive transitively.

```csharp
builder.Services.AddKoan();

public sealed class Session : Entity<Session>
{
    public string Subject { get; set; } = "";
}

var saved = await new Session { Subject = "operator" }.Save();
var same = await Session.Get(saved.Id);
```

**Guarantee/correction:** The backend owns standard endpoint discovery, one lazy host-owned
multiplexer per exact endpoint, source-aware connection reuse, lifecycle, and corrective failure for
malformed or unavailable selected endpoints. The Data connector owns keyed CRUD, native TTL, fast
keyed removal, source/logical-database routing, limited scan-based filters and pages, capability
facts, and participation-aware health. `AllStream` and `QueryStream` reject before yielding because
the provider cannot bound keyspace traversal. A numbered page bounds the returned result, not Redis
scan cost; traversal is not promised to be snapshot-based, resumable, or mutation-safe.

**Complete intent surface:** One connector reference; `AddKoan()`; Entity statics; an optional
standard Redis endpoint; a reachable Redis service; package-owned startup and health behavior; the
existing real provider suite; three dependency-closed artifacts; and a clean external consumer.
Applications do not need to reference the backend or abstraction directly.

**Public concepts:** Existing `RedisOptions`, `RedisDataOptions`, and Data capability contracts own
deployment and behavior policy. `IRedisConnectionProvider` is the narrow module-author seam for a
shared endpoint pool. Promotion adds no public API, option, registration path, or runtime concept.

**Coalescence:** Reuse the shared backend split and provider tests completed in R11. The backend and
inert contract receive one foundation claim because they jointly express connection ownership; Data
Redis receives the provider claim because it owns Entity semantics. Cache Redis remains a separate,
unassessed product claim and gains no support label from this slice.

**Ergonomics:** The common application installs one discoverable package and keeps ordinary Entity
IntelliSense. The abstraction remains inert by itself; the functional backend activates through
normal `AddKoan()` semantic discovery only when a functional Redis package is present.

## Evidence boundary

1. Run the complete existing Redis Data provider project against Redis 8.8.
2. Reuse focused semantic-activation evidence to verify the abstraction remains inert.
3. Pack all three owners with `PublicRelease=true`.
4. Restore, build, and run a clean external consumer from the staged packages plus NuGet.org; prove
   `AddKoan()` selection, shared default connection identity, Entity behavior, and stream rejection.
5. Compile product truth, run the API guard and cheap coherence; do not run sibling providers or
   framework-wide certification.
6. After main publication, observe the exact public artifacts and rerun the consumer from NuGet.org.

## Exit state

This card passes when the three Redis owners declare 0.20 intent, own honest supported claims,
focused native/inert/package-consumer evidence passes, and all three public artifacts are visible on
NuGet.org. Their exact first public versions become immutable API floors in the following slice.

## Focused evidence — 2026-07-22

- architecture and dependency closure: one application connector, one functional shared backend,
  and one module-free inert contract; Cache Redis remains outside this promotion;
- real provider boundary: the existing fixture uses `redis:8.8.0-alpine`;
- complete Redis Data provider suite: 12/12 passed with zero skips, covering shared backend identity,
  real CRUD/routing, AODB modes, TTL, capability behavior, and managed fields;
- Couchbase exact `0.20.1` API floor: recorded in its owning project;
- runtime or public API changes: none;
- focused semantic-activation class: 6/7 passed; the only failure was the unrelated staged-Core
  generator probe after its sandbox-blocked NuGet restore left a package-cache entry incomplete;
  the Redis package consumer directly proved the promoted activation/inertness boundary;
- `PublicRelease=true` packs: all three artifacts produced successfully at local first-publication
  version `0.20.0`;
- genuinely external staged-package consumer: restored exactly the Redis Data connector `0.20.0`
  from the isolated feed plus public NuGet dependencies, built with zero warnings/errors, selected
  Redis through `AddKoan()`, reused the standard default multiplexer, proved the contract assembly
  has no Core activation dependency, completed Entity save/get/query/page against Redis 8.8, and
  rejected `AllStream` before yielding with
  `REDIS|PACKAGE-CONSUMER|SHARED-BACKEND|INERT-CONTRACT|PASS`;
- generated product truth: 39 claims / 93 packages; the three Redis owners are in the supported 0.20
  closure while Cache Redis remains unassessed at 0.18;
- API posture: 48/51 configured, with exactly the three Redis owners as allowed first-publication
  pending floors and three content-only owners;
- generated-drift check and the no-tests repository coherence gate: passed; the latter was green in
  22.6 seconds after recording the expected OrderIntake Redis lockfile version changes, with zero doc
  errors and 1,402 existing warnings;
- PR `#100` exact-head gate `29900348172`: passed and merged as
  `b5628a7abad1e275522bed74901e1db9a459de29`;
- main publication run `29900614297`: passed and pushed all three exact `0.20.0` artifacts;
- NuGet.org indexed the connector, backend, and abstraction; a genuinely external NuGet.org-only
  application restored into a new empty package cache, built with zero warnings/errors, selected
  Redis through `AddKoan()`, reused the standard multiplexer, proved the contract's activation
  independence, completed Entity save/get/query/page against Redis 8.8, and rejected streaming before
  yielding with `REDIS|PACKAGE-CONSUMER|SHARED-BACKEND|INERT-CONTRACT|PASS`;
- the following R13-12 slice records all three exact `0.20.0` artifacts as immutable API floors.
