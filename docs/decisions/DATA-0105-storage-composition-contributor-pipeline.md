# DATA-0105: The storage-composition contributor pipeline

**Status**: Proposed (2026-06-22) — *revised twice after adversarial review; this version adopts the **descriptor-not-callback** model, **consumes ARCH-0096** for the Name stage, **drops the Key stage** (cache-key composition is a cache-pillar concern), and folds in the second-review corrections.*
**Date**: 2026-06-22
**Deciders**: Enterprise Architect
**Scope**: Converge the data core's *latent, hardcoded* storage-composition pipeline — the stages every entity operation already flows through (**Route → Name → Schema → Write-stamp → Serialize → Read-filter**) — onto **one discoverable contributor seam**, and re-home today's bespoke cross-cutting concerns (partition, identity, timestamps, indexes, projections) as the *built-in* contributors that prove it. **The seams are tenancy-agnostic and live in `Koan.Data.Core`; cross-cutting concerns that are *not* intrinsic to the data core (tenancy, classification) provide their contributors from their *own modules* (`Koan.Tenancy`, …) under Reference = Intent — the data core never names them (§0).** The trigger is tenancy (ARCH-0095); without this, tenancy and classification each force per-adapter bespoke code *inside the data core*. Binding constraint: **layered memoization** — every stable substructure (schemas above all) is memoized at the deepest plane where it is stable and never recomputed per op; a contributor returns an **immutable descriptor**, never a per-op callback.
**Related**: **ARCH-0096** (the identifier-composition primitive — the Name stage *delegates* to it; cache-key composition lives there, not here) · **ARCH-0097** (the axis-generic ambient carrier — a contributor reads declared ambient *slices*, and a cross-cutting axis like tenant rides as a slice from its own module) · **ARCH-0095** (tenancy — the first *external contributor module* (`Koan.Tenancy`); **this ADR supersedes ARCH-0095 §2's "4a rides the `.` namespace" — see §1**) · **DATA-0104** (storage-name grammar — the anchor this preserves) · **ARCH-0084** (capability model — the negotiation this *extends*) · **ARCH-0086** (`KoanModule`/`[KoanDiscoverable]`/`KoanRegistry` — discovery, incl. cross-module) · **ARCH-0094** (the Adapter Forge — the pipeline gives the Conformance Gate *one structural check*, not the whole contract) · **[koan-design-principles]**.

---

## Context

The data core already has a pipeline; it is welded shut. An empirical survey (six-lens, code-verified) found cross-cutting concerns spread across the stages, each hardcoded into the stage it touches, with **no seam** for a module to add a name segment, a schema column, a write stamp, or a filter predicate. Tenancy (ARCH-0095) needs to touch several of these stages; classification will need the same plus encryption. Implementing each per-adapter scatters the same logic across every connector — the "N parallel implementations" the redesign exists to delete.

The survey also confirmed, against current source, **pre-existing defects** this ADR's phase 0 fixes first: `ProjectionResolver.Get()` and `IndexMetadata.GetIndexes()` re-run reflection **per call** (no cache); `IndexMetadata` is **non-deterministic** (a per-call `Guid` group key at `:35`, a `Dictionary` iteration at `:41`); `AdapterNaming.GetOrCompute` is **misnamed and does no caching** — it walks DI + routing on every property access (the factory it returns caches, this method does not); and the composed storage name is cached **coupled to partition**, so the base anchor re-resolves per partition. The patterns to *fix* this are in-tree (`AggregateMetadata.IdCache`, `StorageOptimizationExtensions.Cache`, `StorageNameGenerator.Cache`, `TimestampPropertyBag`).

Two adversarial review rounds validated the structure and corrected the specifics; this revision folds in: the **descriptor-not-callback** model, a layered (not single-plane) memoization model with the dead `(Type,adapter,source)` plane removed, a structural closure (not a lint), the corrected naming/tier assignments, the schema-per-tenant correction (with an explicit ARCH-0095 supersession), an honest capability-negotiation framing, a completeness ledger, the tenancy subsumption of the shipped slice-1b gate, the **dropped Key stage**, and named deletions.

---

## Decision

### 0. The contributor pattern — generic seams in the data core; contributors from modules

This is the load-bearing shape, stated once. **`Koan.Data.Core` owns a set of generic, tenancy-agnostic
*seams*; modules register *contributors* into them.** The data core never names tenancy, classification, or any
other cross-cutting concern — it only knows the seams.

**The seams (all in `Koan.Data.Core`, tenancy-agnostic):**

| Seam | Stage | Shape | First built-ins | First *external* contributor |
|---|---|---|---|---|
| **`IParticleFormatter` / particle** (ARCH-0096) | Name (+ cache Key) | a name/key particle | partition | tenant (cache key only) |
| **`IStorageGuard`** | (pre-op) | fail-closed check at the chokepoint; throw to block | — | tenant gate (`Koan.Tenancy`) |
| **`IWriteStamp`** (`StorageWritePlan`) | Write-stamp | sync entity/record mutation | identity, `[Timestamp]` | tenant discriminator |
| **(read-filter seam)** | Read-filter | predicate + post-fetch ownership check | — | tenant filter |
| **(schema-column contributor)** | Schema | a column added to DDL + projection | — | tenant discriminator column |
| **(route qualifier)** | Route | a schema qualifier | — | tenant 4a schema |
| **typed slice** (ARCH-0097) | ambient | a registered ambient axis (read by contributors) | — | `TenantContext` |

**Where contributors live.** Intrinsic concerns (identity, `[Timestamp]`, partition, projections, indexes) are
**built-in** contributors shipping inside `Koan.Data.Core`. Cross-cutting concerns that are *not* the data core's
business — **tenancy** and **classification** — ship their contributors from their **own modules** (`Koan.Tenancy`
is the first). A module:

1. **defines its ambient slice** (e.g. `TenantContext`, ARCH-0097) and its **developer surface** (`Tenant.Use`,
   `.WithTenant`, `[HostScoped]`) — *not* the data core;
2. **registers its contributors** (`IStorageGuard`, `IWriteStamp`, read-filter, schema-column, particle) via
   `[KoanDiscoverable]` / its `KoanAutoRegistrar` — discovered across modules (ARCH-0086);
3. is **Reference = Intent**: referencing the module lights the concern up; **not referencing it = the seam is
   empty = no-op** (structural absence, no per-op "is tenancy on?" branch in the data core).

So the "tenancy kernel" is not tenancy code *in* `Koan.Data.Core`; it is **`Koan.Tenancy` referenced** (it
depends on `Koan.Data.Core` and registers the contributors). The data core compiles, tests, and ships with **zero
knowledge of tenancy** — that is the conformity-by-design invariant this ADR exists to guarantee, and the property
the implementation must hold (a data-core grep for "tenant" returns nothing but seam-agnostic names).

### 1. The six stages (and the two corrections review forced)

| Stage | What it shapes | Built-in contributors re-homed here |
|---|---|---|
| **Route** | which adapter/source/**schema-qualifier** serves the op | source/adapter, `[SourceAdapter]`/`[DataAdapter]`; **schema-per-tenant (4a) lives here** |
| **Name** | the physical storage identifier | the base-name **anchor** (incl. `[Storage]`/`[StorageName]`/`[StorageNaming]` overrides) + **particles** (partition) — **composed via ARCH-0096** |
| **Schema** | columns / indexes / DDL | projections, `[Column]` name, `[NotMapped]`/`[IgnoreStorage]` exclusion, `[Index]`/composite/`[Index(Ttl)]`; `[ReadOnly]`/`[RelationalStorage]` are **schema-policy** (DDL-level), distinct from column contributors |
| **Write-stamp** | mutate the record pre-persist | **identity** (`[Identifier]`), **`[Timestamp]`** |
| **Serialize** | POCO ↔ stored record (the non-POCO field hook) | — *(deferred, §Carve-outs)* |
| **Read-filter** | guard reads | — *(tenant single-consumer carve-out, §8)* |

**Two corrections, both load-bearing:**

- **The "Key" stage is dropped.** Composing the cache key is a **cache-pillar** concern: `CachedRepository` wraps the entity repository **outside** `RepositoryFacade` (the survey confirmed the wrap order: adapter → `RepositoryFacade` → `CachedRepository`), so a data-pillar Key contributor would be *structurally unreachable* by the code that builds cache keys. Cache-key composition instead delegates to **ARCH-0096's `IdentifierComposer`** with the logical-key policy, and the cache pillar reads the partition/tenant ambient axes itself — exactly as it reads them today. Tenant therefore enters the **cache key** (a particle registered in the cache composer), **never** the data pipeline's stages.

- **Schema-per-tenant (4a) is NOT a name particle — it is a schema qualifier. (This supersedes ARCH-0095 §2.)** ARCH-0095 §2 wrote that 4a "rides DATA-0104's `.` namespace." The survey disproved it: `StorageNameResolver.ReplaceDot` *flattens* the `.` to the adapter separator per adapter (`acme.todo` → `acme_todo` on SQL Server/SQLite), so a tenant cannot ride the dot as a name particle. **4a is a DB-engine schema** (`CREATE SCHEMA acme; acme.todo`) resolved at the **Route** stage, outside the name resolver — and it is **net-new Route machinery**: `AdapterResolver.ResolveForEntity` returns only `(Adapter, Source)` with no schema slot (confirmed). Only Postgres has a native `SearchPath`; it is the prototype, ambient-ized, and adapters without a schema concept fail closed under a schema-isolation capability token. Name-particles stay reserved for partition-style suffixes; ARCH-0095's "tenant never enters the table-name spine" holds. *(An erratum is filed on ARCH-0095 §2 pointing here.)*

- **Name = anchor + particles, and the precedence is shipped behavior.** An explicit `[Storage("audit_log")]` override sets the base anchor; particles (partition) compose **onto** it. ARCH-0096 owns the composition and preserves this precedence; this pipeline owns *which* particles register.

> **ERRATUM (2026-06-22) — the Serialize stage realizes record-field injection; the "sibling column" premise is re-derived.**
> An empirical re-derivation ([the managed-field design memo](../architecture/tenancy-managed-field-design.md) §1, adversarially reviewed) disproved the assumption that a non-POCO discriminator can ride a **sibling physical column**: relational adapters persist **only `(Id, Json)`** on write (`SqliteRepository.cs:822`), so a sibling column is never populated, and the value is in neither the entity nor the Json without a hook. The correct mechanism is a **managed field injected into the persisted record** via a **Serialize-stage `ContractResolver` hook** into the Json envelope (relational) / a BSON element (Mongo) / a sibling key (JSON-file), filtered by making the **shared `FieldPathResolver` managed-aware** (one change reaches both the translator and the pushability splitter). The indexed sibling/computed column becomes an **optional Schema-stage optimization** (a computed/expression index over the JSON-access expression). Consequences: (1) the **Serialize stage is advanced from a carve-out to phase 3b** for relational — a deliberate, internally-consistent reorder; (2) the **`Write-stamp` record-field-injection sub-shape is unexercised in v1** (reserved for a future POCO-shaped concern) — the discriminator is a Serialize hook, not a Write-stamp; (3) enforcement spans **planes, not one gateway** (the facade covers the relational/document repo path only — cache-key, vector, and raw/RLS planes each get their own honoring or fail-closed). The memo is the authoritative design.

### 2. The contributor returns a descriptor, not a callback

A storage contributor is **declarative**: at **plan-build** time (once per `(Type, adapter)`) it returns **immutable descriptors** for the stages it participates in; the framework owns **applicators** that apply those descriptors to the operand per op. There is **no per-op contributor callback**.

```
sealed record StorageContributorPlan(
    NameParticle[]   NameParticles,    // → ARCH-0096 ParticleDescriptors
    ColumnSpec[]     Columns,          // Schema
    WriteStamp[]     WriteStamps,      // Write-stamp (see two sub-shapes below)
    ReadFilterSpec?  ReadFilter,       // Read-filter (predicate + post-fetch shapes)
    RouteQualifier?  RouteQualifier);  // Route (4a schema qualifier)

interface IStorageContributor
{
    AxisId[] DeclaredAxes { get; }                                   // the ambient axes it reads (closure key)
    StorageContributorPlan BuildPlan(in StoragePlanContext ctx);     // ctx = { Type, adapter, source } — NO ambient
}
```

- **Why descriptors, not a unified callback interface (the prior draft's model):** a descriptor is **memoizable**, **structurally closed by construction** (it literally cannot read `EntityContext.Current` — it is built from `(Type, adapter, source)` only, and carries the *declared axes* it will be applied with), **inspectable and conformance-checkable as data** (the Adapter-Forge structural check reads the plan, it does not execute it), and **0-alloc to apply** (the applicator is a framework-owned `static` over a `readonly struct` slice). The flagship `TenancyContributor` declares a Route qualifier (4a), a column (4b), a write-stamp, and a read-filter — **as four descriptor fields on one plan**, not four callbacks.
- **Discovery:** `[KoanDiscoverable]` → `KoanRegistry` (build-time generator + runtime manifest; reflection-free, ARCH-0086). These are **storage contributors** — deliberately *not* "composition contributors" (the existing `IKoanCompositionContributor` boot-twin keeps that word).
- **The two write-stamp sub-shapes (must be expressible without per-adapter bespoke):** a `WriteStamp` is either a **POCO-property mutation** (identity, `[Timestamp]` — set a property on the entity) **or** a **record-field injection** (the tenant discriminator — write a value into a dedicated stored column / a non-POCO field). Both are descriptors over the framework-owned applicator; the relational `(Id, Json)` envelope realizes the second as a sibling column, the bare-entity stores (JSON/Mongo) realize it through the Serialize hook (phase 5). Identity stays **invariant** — a built-in write-stamp for uniformity, never pluggable-away; its inline facade call is the named deletion.

### 3. Layered memoization (the binding core)

**The plan is a tree of memoized stable substructures**, memoized at the **deepest plane where it is stable** and composed upward; **no stable substructure — schema, name template, contributor plan, delegate set, index spec — is recomputed per op.** A MUST, gated by a no-recompute audit + an allocation benchmark (§Phasing 0).

**The planes** (the dead `(Type, adapter, source)` plane the prior draft listed is **removed** — `source` is connection routing, not shape; it produced nothing):

| Plane | Stable substructures | Precedent |
|---|---|---|
| **Type** | attribute scan, property/index lists, the **ordered contributor set + frozen applicator-delegate arrays**, classification facts | `AggregateMetadata.IdCache`, `StorageOptimizationExtensions.Cache` |
| **(Type, adapter)** | the resolved **anchor** (adapter-styled — adapter-*dependent*, so it belongs here, not Type), **schema/DDL shape**, the `CompositionPolicy` + particle slots, compiled stamp/filter delegates (via `Lazy` — no concurrent double-compile) | — *(today rebuilt per-facade in `TimestampPropertyBag`; hoist to here)* |
| **per-axis complement** (partition, tenant, …) | the composed result keyed by `(parent-plane, declared-axis values)` | `StorageNameGenerator.Cache` (after the anchor/partition split, ARCH-0096 §3) |

- **The contributor set + applicator-delegate arrays freeze at the Type plane** (the highest-leverage memo): resolve interface dispatch **once**, hold a `readonly` array of `static` applicators, iterate by index per op (no LINQ on the plan, no per-op closures, no boxing of axis values — even enums).
- **Plan vs value, at every layer.** A contributor that adds a column keeps the *schema* stable — "there is a Tenant column" is invariant per `(Type, adapter)`; only the *value* stamped is per op. Tenancy does **not** make schema/name/DDL per-op; the structure is memoized once. This is why the model is a net **perf win** (it deletes today's per-op schema/index recompute), not a tax.
- **The closure — structural, not a lint.** A contributor's plan is a pure function of `(Type, adapter, source)`; it is applied with a **typed slice containing exactly its declared axes** and has **no access to `EntityContext.Current`**. An undeclared dependency is *unrepresentable*, and the declaration *is* the complement's cache key. Reuses the Charter's typed slices (L3).
- **The slice-1b precondition is value-per-op, not an ambient read.** The presence-gate decision (is a tenant in scope?) is the *value* of the declared `tenant` axis at the chokepoint — a plan operand, not an ambient read inside the applicator (reconciles the no-ambient law with §7's gate).
- **Correctness carve-out (two disciplines).** Structural planes cache-forever; the **tenant registry / connection routing** stays **live-truth, coherence-evicted** (ARCH-0095). Aggressive memoization must not freeze it.

**Hot path (binding, [koan-design-principles] §4).**
- **One ambient read per op, at the chokepoint.** Today the ambient is read more than once below the facade (`AdapterNaming` re-reads `EntityContext.Current?.Partition`; `AdapterResolver` reads `Current`; `Data<>` entry points re-read for the transaction coordinator). The pipeline **snapshots the ambient once at the chokepoint into a `readonly struct` slice and threads it down** — the adapter stops reading `AsyncLocal`. A counting-probe test asserts exactly one read per op. *(This is an improvement over the current multi-read state, not a description of it.)*
- **Synchronous applicators.** Write-stamp and read-filter apply **synchronously** — id-gen, timestamp, and stamp are pure-CPU; an `async` applicator allocates a state machine and fails the 0-alloc gate.
- **`readonly struct` plan + struct slice + frozen delegate array**; a `MemoryDiagnoser` benchmark asserts **0 bytes added per `Upsert`/`Get`** vs the pre-pipeline baseline.
- **Disabled = structurally absent.** Tenancy-off (and any concern with no registered contributor) is an **empty plan array built at discovery**, not a per-op `Mode == Off` branch (the mode is cached in a field at build). A Type-plane **`IsInvariantOnly` fast path** routes the common case (no non-trivial contributor) to today's exact inline calls, byte-for-byte.

**Deterministic ordering (binding).** Contributors apply in a total, stable order (explicit priority, frozen at discovery). For **Name**, order determines the physical identifier, so non-determinism = orphaned tables; **`IndexMetadata` must become deterministic first — both the per-call `Guid` group key (`:35`) and the `Dictionary` iteration order (`:41`)** — before its output is memoized.

### 4. Capability negotiation — NEW machinery on ARCH-0084 (stated honestly)

ARCH-0084 today is **declare / negotiate / self-report only**: an adapter `caps.Add(token)`; a consumer `caps.Has/Require(token)` (the single fail-loud path → `CapabilityNotSupportedException`). There is **no** "contributor declares a needed capability → compose-or-fail" mechanism, and **no tenant-isolation token** in `DataCaps`. This ADR **adds** that negotiation: a contributor declares the adapter capabilities its plan requires; the framework `Require`s them on the resolved adapter; a mismatch **fails closed**. The isolation token is added in phase 4. This is a new layer, not an existing rule.

> **NOTE (2026-06-22) — the managed-field registry is the contributor pattern for non-POCO ambient fields (a declared static-reach deviation).**
> The [managed-field design memo](../architecture/tenancy-managed-field-design.md) realizes a `ManagedFieldDescriptor` (a `StorageName` + `ClrType` + ambient `ValueProvider` + `AppliesTo` + `RequiredCapability` + `Indexed`) — this **is** the §0 contributor pattern as data, registered at boot by the owning module's registrar (like `IStorageGuard`). It uses a **static `ManagedFieldRegistry`** rather than DI resolution, **declared here as a deliberate deviation** justified solely by the Serialize-stage `ContractResolver` needing static reach deep in adapter serialization (no DI scope there). The capability token names the **adapter** guarantee, not the consumer — **`DataCaps.Isolation.RowScoped`** (axis-free, stays in `Koan.Data.Abstractions`), mirroring `Write.ConditionalReplace`. The `RequiredCapability` gate fails closed unless the adapter announces the token **and** is an `IQueryRepository` **and** pushes scalar `Eq` on the managed field (so a managed predicate can never silently land as an in-memory residual).

### 5. Completeness ledger (every surveyed concern placed)

| Concern | Disposition |
|---|---|
| base storage name | **framework anchor** (memoized `(Type, adapter)`, ARCH-0096), not a contributor |
| `[Storage]`/`[StorageName]`/`[StorageNaming]` overrides | **anchor inputs** at Name (override sets anchor; particles compose on) |
| partition | built-in **Name** particle (via ARCH-0096) |
| **cache key (partition/tenant in the key)** | **out of this pipeline** — an ARCH-0096 cache-pillar consumer (the dropped "Key" stage) |
| `[Identifier]` id-gen | built-in **Write-stamp** (invariant; inline call deleted) |
| `[Timestamp]` | built-in **Write-stamp** |
| projections, `[Column]` name | built-in **Schema** (converges the `[Column]` vs property-`[StorageName]` double impl) |
| `[NotMapped]`, `[IgnoreStorage]` | built-in **Schema** exclusion (converge the two onto one) |
| `[Index]`/composite/`[Index(Ttl)]` | built-in **Schema** |
| `[ReadOnly]`, `[RelationalStorage]` shape | **Schema-policy** (DDL-level), distinct from column contributors |
| source/adapter, `[SourceAdapter]`/`[DataAdapter]` | **Route** (existing seam) |
| `[Parent]` relationships | **out of scope** — navigation metadata, not storage composition (own subsystem) |
| `[HostScoped]` / tenant | built-in **Route**(4a) / **Schema**(4b) / **Write-stamp** / **Read-filter** (+ cache-key particle via ARCH-0096) |
| `[AppendOnly]`, `[Encrypted]`/`[Classified]`, optimistic-concurrency-version | **0 consumers today** — no seam built (dogfood) |
| soft-delete | **exists** as a partition-move (`Koan.Web.Extensions`), *not* a column+filter attribute — a future column-based one reconciles with that; no seam now |

### 6. Boundaries

- **`Entity.Events` (app hooks) vs storage contributors — by mechanism, not "audience."** Storage contributors are framework-owned, capability-gated, run off a memoized plan, read no ambient directly. **Order is pinned and matches today's:** user `BeforeUpsert` runs first (the survey confirmed it fires before the persist callback), then framework write-stamp applicators stamp the result inside the persist callback (so a user cannot unstamp the tenant/id). `AfterLoad`/read-filter ordering likewise specified.
- **AOT:** discovery is reflection-free (`KoanRegistry`). The per-Type attribute scan + delegate compilation (the plan build) is the trimming-relevant surface; phase 0 validates it against the `AggregateMetadata` precedent.

### 7. Tenancy integration (subsumes the shipped gate; closes the keyed path; honest interim state)

- **`TenancyContributor` subsumes `ITenantEnforcer` (slice 1b) — the gate moves, it never disappears.** The shipped presence-gate becomes the contributor's fail-closed **precondition** (runs first), then write-stamp / read-filter. **The deletion of `ITenantEnforcer` and the landing of its replacement happen in the same slice** — the chokepoint is never, for one commit, ungated. Ordering is pinned in the chokepoint.
- **Read-filter is two-shaped.** A predicate into `QueryDefinition` for `Query`/`Count`, **and** a post-fetch ownership check for the key-based `Get`/`GetMany`/`Delete` (which bypass `QueryDefinition`). A predicate-only seam would leave get-by-id an IDOR leak — ARCH-0095 §5 pinned both halves.
- **Interim isolation posture (stated honestly).** Until the read-filter contributor lands (phase 4), tenancy `enforce` mode is **presence-gate only — reads are unfiltered — i.e. not isolation.** Tenancy is off-by-default and unshipped, so no real app is exposed; `enforce` is **experimental until phase 4** and the boot report says so. Honest sequencing, not a hidden regression.

### 8. Dogfood discipline (honest gate)

Converge a stage with **≥2 *existing* built-in consumers**: Write-stamp (identity + timestamp ✓), Schema (projections + indexes ✓). Name has **one** existing consumer (partition) + tenant **imminent** — labelled imminent, not present. **Read-filter has a single consumer (tenant)** and is a **deliberate carve-out**: the tenant read-filter is the security-critical no-leak proof and earns its seam as a single consumer; classification is the second consumer that retro-justifies it. No seam is built for a 0-consumer concern.

### 9. Adapter Forge (claim downgraded)

The pipeline gives the Conformance Gate **one structural check** — "does this adapter honor the contributor seams, and does its plan carry the declared tenant stamp+filter in order?" — that **complements**, and does not replace, ARCH-0094's behavioral, black-box-first suite (honesty/surface/correctness/isolation + contention/soak/chaos/durability). Neither necessary nor sufficient for trust on its own.

---

## Phased rollout (each its own green-ratcheted slice; ARCH-0079 + mutation)

- **0a — standalone fixes first (ship perf+correctness now, shrink the refactor, validate the memo law on real code):** memoize `ProjectionResolver` + `IndexMetadata` at the Type plane (**after** making `IndexMetadata` deterministic — the `:35` `Guid` group key **and** the `:41` `Dictionary` iteration); fix `AdapterNaming.GetOrCompute` (the misnamed no-cache per-op offender); split the base anchor to a `(Type, adapter)` cache independent of partition (ARCH-0096 §3). Each a green-ratchet commit.
- **0b — model + base:** the `IStorageContributor` **descriptor** interface, `[KoanDiscoverable]` discovery, the **layered-memoization** plane tree, the **structural closure** (typed-slice application, no ambient), **deterministic ordering**, `OFF = structurally-absent` + the `IsInvariantOnly` fast path, and the **allocation benchmark** as acceptance gates. The Name stage **delegates to ARCH-0096**.
- **1 — Write-stamp:** re-home identity + `[Timestamp]`; **delete** the inline `EnsureIdAsync`/`UpdateTimestamp` calls from `RepositoryFacade` (`:114-115`, `:126-127`); **preserve the `BatchFacade` invariant** (batch writes are *not* timestamp-stamped today — confirmed at `BatchFacade.Save :202-203`).
- **2 — Name-particle:** re-home partition as the first ARCH-0096 `ParticleDescriptor`; preserve override→anchor / particle precedence; pin vector-untouched.
- **3 — Schema-column:** `RelationalSchemaOrchestrator` consults column descriptors; converge `[Column]`/property-`[StorageName]` and `[NotMapped]`/`[IgnoreStorage]`.
- **4 — Tenancy = registration:** `TenancyContributor` (4a schema-qualifier **net-new at Route**, Postgres `SearchPath` prototype + schema-isolation cap token; 4b discriminator column at Schema; write-stamp **record-field injection** into the dedicated column; two-shaped read-filter); add the tenant-isolation capability token; subsume `ITenantEnforcer` (same slice); the **SQLite `AssertNoTenantLeak` proof** (no-Docker, `(Id, Json)` envelope — needs only Schema-column + Read-filter, not the Serialize hook).
- **5 — +** classification (the second real consumer) and the **serialization/record hook** for bare-entity stores (JSON/Mongo).

---

## Consequences

- **One mental model** — every storage concern is "declare intent → a contributor's descriptor realizes it"; nothing (partition, timestamps, identity) is special.
- **Tenancy and classification become registration, not code.**
- **Named deletions (net parts down):** `RepositoryFacade`'s inline `EnsureIdAsync`/timestamp calls; `ITenantEnforcer` (absorbed into `TenancyContributor`, same slice); the per-call reflection in `ProjectionResolver`/`IndexMetadata` (memoized); the no-cache DI walk in `AdapterNaming.GetOrCompute`; the hardcoded partition concat in `StorageNameGenerator` (→ ARCH-0096 particle); the partition-coupled base-anchor cache (split). Each re-homed concern *moves*, it is not wrapped.
- **A performance win, not a tax** — layered memoization removes per-op schema/name/index recompute that exists today; the allocation benchmark gates against any hot-path regression.
- **The Adapter Forge gets a concrete structural check**; **self-reporting** lists active contributors per entity.
- **Cost** — a foundational hot-path refactor, mitigated by behavior-preserving re-homing, the green ratchet, the memoization law, and the allocation gate. A **deliberate detour** that delays the first tenant no-leak proof by ~3 phases, with an honest interim tenancy posture (§7) rather than a hidden one.

## Carve-outs / open

- The **serialization/record-field hook** for bare-entity stores is deferred to phase 5 (its first real consumer).
- The exact **typed-slice API** for declared variance axes is settled in phase 0b (co-designed with ARCH-0096's `Particle`/`CompositionPolicy`).
- The **`[Parent]` relationship** subsystem is out of this pipeline's scope (navigation, not storage composition).
- **Classification's** encrypt/tokenize/mask contributors are **future** (the ARCH-0095 classification axis).
- **Blob binding** (`StorageBindingAttribute`) is an **ARCH-0096 follow-on** (a different shape — a routing tuple), not part of this pipeline.
