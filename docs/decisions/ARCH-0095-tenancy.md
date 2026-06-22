# ARCH-0095: First-class multi-tenancy — the ambient Tenant slice, the eight primitives, and the classification axis

**Status**: Accepted (2026-06-21) — *design complete; three-round external review returned a unanimous "ship." Implementation is phased/TDD (see §Implementation plan); this ADR records the decision and the empirically-verified enforcement seam.*
**Date**: 2026-06-21
**Deciders**: Enterprise Architect
**Scope**: How Koan provides multi-tenancy — tenant **isolation**, identity/membership, the control plane, lifecycle, enforcement, and a composing **data-classification** axis — as the flagship typed slice of the Facet-3 Ambient primitive, with the **same developer experience regardless of tenancy mode**. The full implementation-ready design is [tenancy-design.md](../architecture/tenancy-design.md); this ADR is the decision record.
**Related**: [Ambient Context Charter](../architecture/ambient-context-charter.md) (the 11 Laws + truth-test this design is measured against — tenancy is its flagship slice) · **ARCH-0084** (the capability model the classification axis and the guard ride) · **ARCH-0079** (integration tests as canon — the isolation test-kit P7) · **ARCH-0091** (the Testcontainers/xUnit-v3 harness P7 builds on) · **SEC-0004** (the `IAuthorize` capability floor the membership gate extends) · **DATA-0104** (storage-name grammar — why tenant never enters the table-name spine) · **DATA-0077** (`PartitionNameValidator` — the partition axis tenancy is distinct from) · **JOBS-0005** (durable jobs — lifecycle sagas ride the ledger) · **WEB-0068** (read-path predicates — host/tenant membership filtering) · **[ARCH-0094](ARCH-0094-adapter-forge.md)** (the Adapter Forge — the Conformance Gate **is** the tenancy isolation test-kit P7; the external-infra delegation seam is the lock-in answer). Full review trail: [tenancy-external-review-findings.md](../architecture/tenancy-external-review-findings.md).

---

## Context

Facet 3 of the foundation redesign unifies seven ambient mechanisms into one carrier (the
[Ambient Context Charter](../architecture/ambient-context-charter.md)). The charter found that the
**missing dimension is tenancy**: Koan has no tenant concept, `partition` does dataset routing with no
identity/isolation/propagation, and process-wide mutable statics actively defeat in-process isolation.
Tenancy is the charter's hard gate and its flagship typed slice.

A four-lens harvest of 41 sourced practitioner pains, then a three-round external review (three frontier
models per round) plus a four-persona delight harvest, drove the design. The reviews **validated the
structural thesis and every settled fork**, corrected nine overstated claims toward honesty, and collapsed
~20 proposed features into **eight load-bearing primitives**. The unanimous verdict was **ship**.

The thesis — Koan **owns every backend pillar in one runtime**, so a tenant can be a runtime property that
flows into data, cache, vector, search, jobs, messaging, observability, and every durable carrier — is
both the unique capability (no single-ORM library can fan a lifecycle verb across every axis) and the
fatal adoption barrier (a team mandated to run un-owned infra faces rip-out-or-walk-away). The decision
embraces both: build the owns-every-axis capability, and ship the **external-infra delegation seam** +
the Adapter Forge (ARCH-0094) so it **coordinates** un-owned axes too.

---

## Decision

The full design is [tenancy-design.md](../architecture/tenancy-design.md). The load-bearing decisions:

### 1. Tenant is the flagship ambient slice — same DX, fail-closed, structurally enforced

Tenant flows via the charter's ambient carrier (an immutable, `AsyncLocal`, restore-on-dispose record —
generalized from today's `EntityContext`). The developer writes `Todo.Get(id)` / `todo.Save()`
**identically across every mode**; the mode is configuration + a per-tenant registry strategy, never code.
Once tenancy is on, entities are tenant-scoped by default (`[HostScoped]` opts out), reads/writes with no
tenant in scope **fail closed and fail loud with a fix-naming error** (charter L6), and a forgotten
predicate is **structurally impossible** because enforcement lives at the data chokepoint, not in
per-query discipline. Tenancy is **off by default** (single-tenant apps pay nothing) and activates on a
gradient `off → warn → enforce` (no Reference=Intent instant-cliff).

The "same DX" claim is made honest (the no-boot-lies principle): entity code is uniform, but *migration*
DX is placement-aware, *read performance* scales with classification posture, and *cross-entity*
operations are not mode-invariant — each boot-reported.

### 2. The isolation mode ladder

Four depths — deployment (silo, out of scope) / connection (db-per-tenant, modes 2–3 = one strategy) /
schema (4a, a native DB-engine schema qualifier) / row (4b, discriminator + filter + RLS).
**Tenant never enters the table-name spine** (it would collide with DATA-0104's `-` separator).

> **Erratum (2026-06-22, superseded by [DATA-0105](DATA-0105-storage-composition-contributor-pipeline.md) §1):**
> this section originally read "4a, native schema riding DATA-0104's `.` namespace." That is wrong —
> `StorageNameResolver.ReplaceDot` *flattens* the `.` to the adapter separator per adapter, so a tenant
> cannot ride the dot as a name particle. **4a is a DB-engine schema qualifier resolved at the Route stage**
> (`CREATE SCHEMA acme; acme.todo`), and it is **net-new Route machinery** — `AdapterResolver` returns only
> `(Adapter, Source)` with no schema slot (Postgres `SearchPath` is the prototype; adapters without a schema
> concept fail closed under a schema-isolation capability token). The "tenant never enters the table-name
> spine" conclusion is unaffected.
Placement is **per-tenant** (heterogeneous registry), changeable by a verb. Honest limit: the ladder makes
substrate choice and movement cheap and visible; it does not repeal physics (pool fan-out, catalog bloat).

### 3. Identity global · membership per-tenant · roles on membership · tenant-gate-above-roles

One human, N tenants. The tenant gate is **prior to and independent of** the role check (you cannot role
across a boundary). Membership is **resolved per request, server-side, never trusted from the token**
(closes the stale-claim and deprovisioning-lag breach classes); the request-tenant is a **routing hint
authorized against memberships** (closes the IDOR-from-URL class). This extends, not replaces, the SEC-0004
capability floor.

### 4. The eight load-bearing primitives (P8 internal); the kernel and the Magic Cliff

P1 chokepoint guard · P2 tenant state machine · P3 Suspend-as-quiesce · P4 logical Export/Import (elevated
— the substrate for snapshot/branch/merge) · P5a migration executor + P5b fleet orchestrator · P6
connection broker (+ credential/KMS seam) · **P7 isolation test-kit (= the Conformance Gate's first
incarnation, ARCH-0094)** · **P8 saga coordinator (internal-only** — orchestrates only the idempotent,
compensable framework primitives P3/P4/P6, carries no user logic; each saga owns its own consistency
semantics, which is the anti-leak boundary; the dev surface stays `Tenant.Erase()`, never `ISaga.Step()`).
Each dogfoods an existing pillar. The **tenancy kernel** (P1–P3 + P7) is delivered by **referencing the
`Koan.Tenancy` module** — a Reference = Intent module that depends only on `Koan.Data.Core` and registers the
tenant contributors into the generic storage-pipeline seams (§5, DATA-0105 §0). It is a *module*, not a *SKU*:
"no separate SKU" means no paid/feature tier, not "tenancy code lives in the data core" — it explicitly does
**not** (the data core is tenancy-agnostic; a grep for "tenant" in `Koan.Data.Core` returns nothing). The
**Magic Cliff** is documented — the flagship async-hop guarantee needs `Koan.Jobs` + `Koan.Messaging`. Adoption
is graceful layering.

The **developer surface lives in `Koan.Tenancy`**, not the data core: the `TenantContext` ambient slice
(ARCH-0097), the accessors `Tenant.Current` / `Tenant.Use(id)` / `Tenant.None()`, the `.WithTenant(…)`
extension, and the `[HostScoped]` opt-out are all the module's — built on the data core's generic
`EntityContext.WithSlice`/`GetSlice` carrier.

### 5. Enforcement at the chokepoint — the empirically-verified seam

Enforcement is the charter's law L8 (leaks structurally impossible at the lowest framework-owned layer).
**The enforcement lives in `Koan.Data.Core` as generic, tenancy-agnostic seams — `IStorageGuard` (the
fail-closed pre-op check), `IWriteStamp` (the discriminator stamp), and the read-filter seam — into which
`Koan.Tenancy` registers contributors (DATA-0105 §0).** The data core invokes "the registered guards / stamps /
filters"; it never names a tenant. The seam was re-derived from the data-core code and verified directly (not
assumed):

- **P1 read-filter + write-guard ⇒ `RepositoryFacade<TEntity,TKey>`** (`src/Koan.Data.Core/RepositoryFacade.cs`).
  It is the **single universal chokepoint**: every read (`Get`/`GetMany`/`Query`/`Count`/`QueryRaw`/`CountRaw`)
  and every write (`Upsert`/`UpsertMany`/`Delete`/`DeleteMany`/`RemoveAll`/`ConditionalReplaceAsync`) plus
  the batch path pass through `Guard(ct)` (lines 47–51), and **no adapter bypasses it** (all are wrapped at
  `DataService.GetRepository()`). Writes already stamp ID + `[Timestamp]` (lines 107–108), so the tenant
  **stamp-and-verify** slots into the existing write pattern. The read-filter injects the tenant predicate
  into `QueryDefinition` for `Query`/`Count`, and is a **post-fetch ownership check** for the key-based
  `Get`/`GetMany`/`Delete`. Implementation: `Koan.Tenancy` registers a `TenantStorageGuard : IStorageGuard`
  (the fail-closed gate — it computes `[HostScoped]` and reads `Tenant.Current` itself) plus the tenant
  write-stamp and read-filter contributors; the facade invokes the registered seams generically and never
  names a tenant.
- **The shared-schema discriminator is an invisible, framework-managed shadow field** (ratified). A
  tenant-scoped entity carries no tenant property on its POCO (secure-by-default, can't-forget, charter L8);
  the adapter persists/filters a hidden discriminator at the storage layer, driven by the ambient tenant.
  Adapters announce tenant-isolation support as a capability (ARCH-0084); a tenant-scoped entity on an
  adapter that does not announce it fails closed under `enforce` (never fail-open). Marker-interface and
  base-class alternatives were rejected as opt-in (a forgotten marker reintroduces the leak). Sequenced
  JSON → relational (RLS backstop) → Mongo.
- **P6 connection routing ⇒ `EntityContext.ContextState` + `AdapterResolver.ResolveForEntity`**
  (`src/Koan.Data.Core/AdapterResolver.cs`). The resolver returns a static `(Adapter, Source)` tuple read
  from the ambient context; the `DataService` cache key is `(EntityType, KeyType, Adapter, Source)`.
  **Key consequence:** mapping a tenant → a distinct `Source` (its `Placement.dataSourceRef`) makes the
  existing source→connection resolution and the existing cache key deliver per-tenant routing for
  db-per-tenant **with no new connection subsystem**. The genuinely-new work narrows to: a `Tenant`
  routing dimension on `ContextState`, a tenant→source resolution step (a priority above `Source`), the
  `ICredentialProvider` seam, and pool **session reset** — there is **no per-request session reset today**,
  confirming the RLS connection-state-poisoning risk; reset hooks at the facade `Guard` and **must fail the
  process, never return a tainted connection**.
- **RLS is the backstop** (not the primary guard) for the raw/bulk surface the application floor can't
  reach (`IDataService.Direct`, raw SQL); `Tenant.FanOutQuery<T>` is the sanctioned cross-substrate admin
  query so operators never drop to raw SQL and bypass P1.
- **Multi-axis auto-flow** (charter L9): tenant rides into cache keys, coherence channels, vector/search
  collection keys, connection-pool session vars, **job payloads** (captured at submit, fail-closed-restored
  at execute), **message envelopes** (+ outbox `TenantId` partitioning), and observability labels.

### 6. The control plane is dogfooded Koan, with durable-carrier discipline

The registry/identity/lifecycle are `[HostScoped]` `Entity<T>` + `IKoanJob<T>` in an independently-placeable
root store (no scope contamination, no second admin framework). Tenant identity is an **immutable surrogate
`id`** with mutable `codes{current/previous}` as **keyed entities** (O(1) resolve, race-safe global
uniqueness; rename = metadata, storage never moves). The **#1 v1 regret class** is mandated now: every
**durable serialized carrier** carries tenant + honors classification — a versioned **audit envelope**
(typed taxonomy + actor + ambient snapshot + causal order + extension bag), messaging **outbox
`TenantId`-partitioning**, and **classified-field stripping** in DLQ / retry-ledger / event-store. The
**credential/KMS seam** (`ICredentialProvider` + pluggable KMS key-ring + tenant-scoped rotation +
crypto-shred) ships as a seam now; certificates carry a Key-ID + verification endpoint.

### 7. The classification axis — a composing, layered-policy capability

Classification is an **orthogonal capability that tenancy composes with**, not tenancy-core. It is **one
extensible axis** (a category = `{name, default-posture, applicable-handlings, retention-default}`), with
built-in `[Pii]`/`[Phi]`/`[Pci]`/`[Secret]` as sugar over `[Classified(...)]` and app-defined categories
in config. The entity declares a **fact**; *handling* is **layered policy** (solution config = posture +
capabilities → tenant overrides), with a **mutability lock = policy-gate-above-tenant** (mirrors
tenant-gate-above-roles). `[Secret]` adds the distinct **write-only/masked-read** handling. Identity-PII
**handling is solution-governed but residency shards by the identity's home region**. The embeddability
**entity hint is cut** — embeddability is config (`allowEmbedding`), intent is the existing `[Embedding]`,
and classified fields are **excluded from the AI/semantic stack by default** (a real, boot-reported
limitation; the deepest tension the review surfaced). Any effective-policy change is a **migration saga,
bidirectionally**. The axis rides the capability model (announce handlings → adapters announce support →
compose or fail-closed).

### 8. Lifecycle as sagas; the erasure certificate is the flagship

`Relocate` is a **saga** (quiesce → copy → atomic registry cutover → verify → rollback; honest
"zero-downtime-read, blocked-to-write window"); `Erase` is a **verifiable state machine** (quiesce →
fan-out across every axis incl. durable carriers → verify → certify; a partial-failure path
`EraseFailedStuck`, never a silent certify). The **cryptographically-signed erasure certificate** — per-axis
disposition counts, honest sync-vs-async-vs-retention axis classes, retention exceptions, Key-ID +
verification endpoint — is the cross-persona flagship delight and the "why-Koan" artifact for regulated
SaaS: only a runtime that owns every axis can prove disposition from cache/vector/search/logs, not just the
database.

### 9. Closed decisions, discipline, and go-to-market

- **D1 — additive-only migrations in v1.** A breaking change is a *declaration* (`[BreakingMigration]` the
  canary refuses) with `PatientV2`-style versioning as the escape; watch semantically-breaking-additive
  (NOT NULL without default, new enum value).
- **D2 — P8 stays internal** (not a dev primitive, not a workflow engine).
- **D3 — graceful layering, no Lite SKU**; name + test the tenancy kernel; document the Magic Cliff.
- **Discipline:** *attributes and verbs are expensive forever; config is cheap and discoverable — bias new
  surface to config.* This drove the embeddability-hint cut and keeps the dev surface small (≈4 attributes,
  3 accessors, 3 verbs, 1 config block; entity code unchanged — the externally-validated coherence verdict).
- **GTM is greenfield-only** ("Day-0 foundation for your next SaaS," not a migration pitch); the
  external-infra delegation seam + Adapter Forge is the technical loosener.

---

## Consequences

- **The leak you cannot write.** Read + write + every axis are guarded at one structural chokepoint;
  fail-closed; a forgotten predicate is impossible. This is the answer to "why Koan over homegrown RLS,"
  and P7 turns it into proof regenerated every build.
- **The day-one tenancy decision stops being a decision.** Isolation is a reversible config dial; the
  day-200 HIPAA / month-nine enterprise pivot is a config diff + a relocate fan-out, not a re-platform.
- **One artifact, three payoffs.** The isolation/classification test-kit (P7) keeps ARCH-0084 honest, is
  the v1 isolation proof, and is the Adapter Forge's Conformance Gate — built once.
- **Honest, not magical.** Nine overstated claims were corrected; the boot report self-attests posture;
  `Relocate`/`Erase` are sagas with defined consistency models, not verb-pretense.
- **The dev surface stayed small; accretion is host-plane** (sagas, broker, state machines) and invisible
  to the developer — the externally-validated coherence outcome.
- **Lock-in is defused, not denied.** Owns-every-axis is embraced; the delegation seam + Adapter Forge make
  it *coordinate* un-owned axes; GTM is honestly greenfield.
- **Cost:** real host-plane complexity (the broker, the saga coordinator, the classification handling
  matrix, the KMS seam) and a hard dependency on the charter's ambient carrier landing first.

---

## Implementation plan (phased TDD — ARCH-0079 real-store spec + mutation check each step)

0. **Decouple (prerequisite).** Make the data core tenancy-agnostic: the **axis-generic ambient carrier**
   (ARCH-0097 — `EntityContext.WithSlice`/`GetSlice`, no `tenant` field) and the **generic seams**
   (`IStorageGuard`, the read-filter seam) in `Koan.Data.Core`; create the **`Koan.Tenancy` module** and move
   `TenantContext`/`Tenant`/`TenancyMode`/`TenancyOptions`/`[HostScoped]`/the gate into it. (The shipped
   slices 1a/1b put these *in* the data core; this slice extracts them.)
1. **Ambient `Tenant` slice (in `Koan.Tenancy`)** — `TenantContext` over the generic carrier + the
   `Tenant.Current/Use/None` + `.WithTenant` surface (restore-on-dispose; fail-closed).
2. **P1 chokepoint guard** — `Koan.Tenancy`'s `TenantStorageGuard : IStorageGuard` (registered, discovered)
   + the tenant read-filter (predicate-into-`QueryDefinition` + post-fetch ownership check) + write
   stamp-and-verify contributors; fix-naming fail-closed error; `[AllowUnscopedWrite]` escape; the RLS
   backstop named. **This is the Conformance Gate's first incarnation (ARCH-0094 P7)** — ship
   `AssertNoTenantLeak` + the property-based fuzz with it.
3. **P2 state machine + P3 Suspend-as-quiesce** at the chokepoint.
4. **P6 connection broker** — tenant→source resolution above `AdapterResolver`'s `Source` priority;
   `ICredentialProvider` seam; guaranteed pool session reset (fail-the-process); honest boot-report limits.
5. **Control-plane keyed entities** — `Tenant`/`TenantCode`/`TenantDomain`/`Identity`/`Membership`
   (`[HostScoped]`); the durable-carrier schema (audit envelope, outbox `TenantId`, DLQ stripping).
6. **Resolution + membership-gate-above-roles** (SEC-0004 seam re-derived from code).
7. **The classification axis** — `[Classified]`/`[Pii]`/`[Phi]`/`[Secret]`; layered policy + mutability
   lock; capability-model integration; searchable blind-HMAC; request-scoped plaintext map.
8. **Lifecycle ops as `IKoanJob`/sagas** (P5/P8) + **the erasure certificate**.

Each phase uses Workflows for the substantive build/verify where fan-out helps, and is green-ratcheted.

---

## Carve-outs / open (for implementation follow-ons)

- **The ambient carrier's own name** (charter Q1; `Ambient` recommended) — tenancy's surface is `Tenant`
  regardless.
- **The exact SEC-0004 membership-gate seam** — re-derive from code before phase 6.
- **Tenant hierarchy** — the nullable `ParentTenantId` + plain `tenant_id` discriminator seam ships in v1;
  behavior is deferred (flat v1).
- **Classification × analytics residency** — `[ProjectedToHost]` over classified fields needs a
  cross-region detokenization fan-out that fights the pinning; v1 excludes classified fields from host
  projection by default.
- **Surgical per-tenant backup/restore** and **BYOC via `Relocate`** — north-stars P4 makes tractable; not v1.
- **SSO/SCIM brokering** — membership shape modeled now; per-IdP brokering is above-layer / v2.
