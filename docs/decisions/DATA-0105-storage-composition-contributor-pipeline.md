# DATA-0105: The storage-composition contributor pipeline

**Status**: Proposed (2026-06-22) — *revised after a six-lens adversarial review; this version incorporates the corrections.*
**Date**: 2026-06-22
**Deciders**: Enterprise Architect
**Scope**: Converge the data core's *latent, hardcoded* storage-composition pipeline — the stages every entity operation already flows through (**Route → Name → Schema → Write-stamp → Serialize → Read-filter → Key**) — onto **one discoverable contributor seam**, and re-home today's bespoke cross-cutting concerns (partition, identity, timestamps, indexes, projections) as the *built-in* contributors that prove it. The trigger is tenancy (ARCH-0095); without this, tenancy and classification each force per-adapter bespoke code. Binding constraint: **layered memoization** — every stable substructure (schemas above all) is memoized at the deepest plane where it is stable and never recomputed per op.
**Related**: **ARCH-0095** (tenancy — the first real consumer) · **DATA-0104** (storage-name grammar — the Name stage this extends) · **ARCH-0084** (capability model — the negotiation this *extends*) · **ARCH-0086** (`KoanModule`/`[KoanDiscoverable]`/`KoanRegistry` — discovery) · **ARCH-0094** (the Adapter Forge — the pipeline gives the Conformance Gate *one structural check*, not the whole contract) · the Ambient Charter (typed slices — a contributor's input is a composition of ambient slices it declares).

---

## Context

The data core already has a pipeline; it is welded shut. An empirical survey found **24 cross-cutting concerns**
spread across the stages, each hardcoded into the stage it touches, with **no seam** for a module to add a name segment,
a schema column, a write stamp, a filter predicate, or a cache-key part. Tenancy (ARCH-0095) needs to touch several of
these stages; classification will need the same plus encryption. Implementing each per-adapter scatters the same logic
across every connector — the "N parallel implementations" the redesign exists to delete.

The survey also exposed **pre-existing, code-verified defects**: `ProjectionResolver.Get()` and `IndexMetadata.GetIndexes()`
re-run reflection **per call** (no cache); the base storage name is not cached independent of partition; `PostgresRepository.TableName`
re-resolves routing per property access. The patterns to *fix* this are already in-tree (`AggregateMetadata.IdCache`,
`StorageOptimizationExtensions.Cache`, `StorageNameGenerator.Cache`).

A six-lens adversarial review of the first draft validated the structure but corrected the specifics; this revision folds
in: a layered (not single-plane) memoization model, a structural closure (not a lint), the corrected naming/tier
assignments, the schema-per-tenant correction, an honest capability-negotiation framing, a completeness ledger, the
tenancy subsumption of the shipped slice-1b gate, and named deletions.

---

## Decision

### 1. The stages (and two corrections the review forced)

| Stage | What it shapes | Built-in contributors re-homed here |
|---|---|---|
| **Route** | which adapter/source/schema-qualifier serves the op | source/adapter, `[SourceAdapter]`/`[DataAdapter]`; **schema-per-tenant (4a) lives here** |
| **Name** | the physical storage identifier | the base-name **anchor** (incl. `[Storage]`/`[StorageName]`/`[StorageNaming]` overrides) + **particles** (partition) |
| **Schema** | columns / indexes / DDL | projections, `[Column]` name, `[NotMapped]`/`[IgnoreStorage]` exclusion, `[Index]`/composite/`[Index(Ttl)]`; `[ReadOnly]`/`[RelationalStorage]` are **schema-policy** (DDL-level), distinct from column contributors |
| **Write-stamp** | mutate the entity pre-persist | **identity** (`[Identifier]`), **`[Timestamp]`** |
| **Serialize** | POCO ↔ stored record (the non-POCO field hook) | — *(deferred, §Carve-outs)* |
| **Read-filter** | guard reads | — *(tenant single-consumer carve-out, §8)* |
| **Key** | compose the cache key | partition (today, in `CacheKey.For`) |

- **Name = anchor + particles, and the precedence is shipped behavior.** An explicit `[Storage("audit_log")]`
  override sets the base anchor; particles (partition; never tenant — see below) compose **onto** it. The ordering model
  (§3) must preserve this.
- **Schema-per-tenant (4a) is NOT a name particle — it is a schema qualifier.** DATA-0104's `.` namespace is *flattened
  per adapter* (`StorageNameResolver` `ReplaceDot` → `_` on SQL Server/SQLite), so "tenant rides the `.` namespace
  natively" is false. 4a is a **DB-engine schema** (`CREATE SCHEMA acme; acme.todo`) resolved at the **Route** stage
  (qualification), outside the name resolver. Name-particles are reserved for partition-style **suffixes**. (Shared-schema
  4b remains the discriminator *column* + read-filter; ARCH-0095's "tenant never enters the table-name spine" holds.)

### 2. One unified `IStorageContributor` (not segregated micro-interfaces)

A **single** discoverable interface with per-stage opt-in (default no-op stage methods) plus a declared **stage set** and a
declared **variance-axis set**. The flagship consumer (`TenancyContributor`) touches four stages, so segregation into 4–5
micro-interfaces buys little and costs a discovery/ordering fan-out per seam. A single-stage built-in (partition,
timestamps) implements only its one method.

- **Discovery:** `[KoanDiscoverable]` → `KoanRegistry` (build-time generator + runtime manifest; reflection-free
  discovery, ARCH-0086).
- **Naming:** these are **storage contributors**, deliberately *not* "composition contributors" — the existing
  `IKoanCompositionContributor` (boot composition twin) keeps the word "composition" to avoid the collision the review flagged.
- **Built-ins re-homed (with their deletions, §Consequences):** `PartitionContributor` (Name), `TimestampContributor`
  + `IdentityContributor` (Write-stamp), `ProjectionContributor` + `IndexContributor` (Schema). Tenancy registers one
  `TenancyContributor` (Route-qualifier 4a, Schema-column 4b, Write-stamp, Read-filter, Key).

### 3. Layered memoization (the binding core)

**The plan is a tree of memoized stable substructures.** Each substructure is memoized at the **deepest plane where it is
stable** and composed upward; **no stable substructure — schema, name template, delegate set, index spec — is recomputed
per op.** This is a MUST, gated by a no-recompute audit + an allocation/cycle benchmark (§Phasing 0).

**The planes** (each cache-forever; precedents in-tree):

| Plane | Stable substructures | Precedent |
|---|---|---|
| **Type** | attribute scan, property/index lists, the contributor set, classification facts | `AggregateMetadata.IdCache`, `StorageOptimizationExtensions.Cache` |
| **(Type, adapter)** | base storage name (convention-applied), **schema/DDL shape**, name template + particle slots, compiled stamp/filter delegates (via `Lazy` — avoid concurrent double-compile) | — *(today rebuilt per-facade in `TimestampPropertyBag`/`TenantScopeMetadata`; hoist to here)* |
| **(Type, adapter, source)** | source-specific bits (rare) | — |
| **per-axis complement** (partition, tenant, …) | the composed result keyed by `(parent-plane, declared-axis values)` | `StorageNameGenerator.Cache` |

**Plan vs value, at every layer.** A contributor that adds a column keeps the *schema* stable — "there is a Tenant column"
is invariant per `(Type, adapter)` because the contributor *set* is stable; only the *value* stamped is per op. So tenancy
does **not** make schema (or name-template, or DDL) computation per op — the structure is memoized once; the value is the
only per-op work. This is why the model is a net **perf win** (it eliminates the existing per-op schema recompute), not a tax.

**The closure — structural, not a lint.** A contributor's output is a pure function of `(Type, adapter, source)` **plus the
variance axes it declared**. It is invoked with a **typed slice containing exactly its declared axes** and has **no access to
`EntityContext.Current`**. An undeclared dependency is therefore *unrepresentable* (the contributor never sees the axis), and
the declaration *is* the complement's cache key — so an un-enumerated ambient input cannot be frozen into a forever-plane.
The model is **open to any axis** (partition, tenant, tomorrow classification/region) by declare-and-complement; it is not
limited to a fixed `{partition, tenant}`. (This reuses the Ambient Charter's typed-slices, L3 — a contributor's input is a
composition of declared ambient slices.)

**Correctness carve-out (two disciplines).** Structural planes cache-forever; the **tenant registry / connection routing**
(which physical source a tenant maps to) stays **live-truth, coherence-evicted** (ARCH-0095). Aggressive memoization must not
freeze it.

**Hot path.** One `EntityContext` read per op (at the chokepoint) → extract each contributor's declared axis values →
look up / compose the complements (memoized) → apply the cached plan to the operand (entity/query). **Binding:** the plan is a
fixed-length array iterated by index; stage application takes the axis values as args (no per-op closures, no LINQ on the
plan); a `MemoryDiagnoser` benchmark asserts **0 bytes added per `Upsert`/`Get`** vs the pre-pipeline baseline.

**Deterministic ordering (binding).** Contributors apply in a total, stable order (explicit priority, frozen at discovery).
For **Name** and **Key**, order determines the physical identifier, so non-determinism = split-brain/orphaned tables; the
existing `IndexMetadata` per-call `Guid` group key must become deterministic before it is memoized.

### 4. Capability negotiation — NEW machinery on ARCH-0084 (stated honestly)

ARCH-0084 today is **declare / negotiate / self-report only**: an adapter `caps.Add(token)`; a consumer `caps.Has/Require(token)`
(the single fail-loud path → `CapabilityNotSupportedException`). There is **no** "contributor declares a needed capability →
compose-or-fail" mechanism, and **no tenant-isolation token exists** in `DataCaps`. This ADR **adds** that negotiation: a
contributor declares the adapter capabilities it requires; the framework `Require`s them on the resolved adapter; a mismatch
**fails closed**. The tenant-isolation token is added in phase 4. (This is a new layer, not an existing rule.)

### 5. Completeness ledger (every surveyed concern placed)

| Concern | Disposition |
|---|---|
| base storage name | **framework anchor** (memoized `(Type, adapter)`), not a contributor |
| `[Storage]`/`[StorageName]`/`[StorageNaming]` overrides | **anchor inputs** at Name (precedence: override sets anchor, particles compose on) |
| partition | built-in **Name** particle (+ **Key**) |
| `[Identifier]` id-gen | built-in **Write-stamp** (invariant; inline call deleted) |
| `[Timestamp]` | built-in **Write-stamp** |
| projections, `[Column]` name | built-in **Schema** (converges the `[Column]` vs property-`[StorageName]` double impl) |
| `[NotMapped]`, `[IgnoreStorage]` | built-in **Schema** exclusion (converge the two onto one) |
| `[Index]`/composite/`[Index(Ttl)]` | built-in **Schema** |
| `[ReadOnly]`, `[RelationalStorage]` shape | **Schema-policy** (DDL-level), distinct from column contributors |
| source/adapter, `[SourceAdapter]`/`[DataAdapter]` | **Route** (existing seam) |
| `[Parent]` relationships | **out of scope** — relationship metadata is navigation, not storage composition (own subsystem) |
| `[HostScoped]` / tenant | built-in **Route**(4a) / **Schema**(4b) / **Write-stamp** / **Read-filter** / **Key** |
| `[AppendOnly]`, `[Encrypted]`/`[Classified]`, optimistic-concurrency-version | **0 consumers today** — no seam built (dogfood) |
| soft-delete | **exists** as a partition-move (`Koan.Web.Extensions`), *not* a column+filter attribute — a future column-based one reconciles with that; no seam now |

### 6. Boundaries

- **`Entity.Events` (app hooks) vs storage contributors — by mechanism, not "audience."** Storage contributors are
  framework-owned, capability-gated, run off a memoized structural plan, and read no ambient state directly. **Order is
  pinned:** user `BeforeUpsert` runs first (it may mutate the entity), then framework write-stamp contributors stamp the
  result (so a user can't unstamp the tenant/id). `AfterLoad`/read-filter ordering likewise specified.
- **Identity stays invariant** — modeled as a built-in write-stamp contributor (for uniformity) whose inline facade call is
  removed; it is never pluggable-*away*.
- **AOT:** discovery is reflection-free (`KoanRegistry`). The per-Type attribute scan + delegate compilation (the plan build)
  is the trimming-relevant surface; phase 0 validates it against the `AggregateMetadata` precedent.

### 7. Tenancy integration (subsumes the shipped gate; closes the keyed-path; honest interim state)

- **`TenancyContributor` subsumes `ITenantEnforcer` (slice 1b).** The shipped presence-gate becomes the contributor's
  fail-closed **precondition** (runs first), then write-stamp / read-filter. `ITenantEnforcer` is retired into the
  contributor (a named deletion); the ordering is pinned in the chokepoint.
- **Read-filter is two-shaped.** A predicate into `QueryDefinition` for `Query`/`Count`, **and** a post-fetch ownership
  check for the key-based `Get`/`GetMany`/`Delete` (which bypass `QueryDefinition`). A predicate-only seam would leave
  get-by-id an IDOR leak — ARCH-0095 §5 already pinned both halves.
- **Interim isolation posture (stated honestly).** Until the read-filter contributor lands (phase 4), tenancy `enforce`
  mode is **presence-gate only — reads are unfiltered — i.e. not isolation.** Tenancy is off-by-default and unshipped, so no
  real app is exposed; `enforce` is **experimental until phase 4** and the boot report says so. This is honest sequencing,
  not a regression to hide.

### 8. Dogfood discipline (honest gate)

Converge a stage with **≥2 *existing* built-in consumers**: Write-stamp (identity + timestamp ✓), Schema (projections +
indexes ✓). Name has **one** existing consumer (partition) + tenant **imminent** — labelled imminent, not "present."
**Read-filter and Key have a single consumer (tenant)** and are a **deliberate carve-out**: the tenant read-filter is the
security-critical no-leak proof and earns its seam as a single consumer; classification is the second consumer that
retro-justifies it. No seam is built for a 0-consumer concern.

### 9. Adapter Forge (claim downgraded)

The pipeline gives the Conformance Gate **one structural check** — "does this adapter honor the contributor seams?" — that
**complements**, and does not replace, ARCH-0094's behavioral, black-box-first suite (honesty/surface/correctness/isolation
+ contention/soak/chaos/durability). It is neither necessary nor sufficient for trust on its own.

---

## Phased rollout (each its own green-ratcheted slice; ARCH-0079 + mutation)

0. **Model + base** — the unified `IStorageContributor`, the `[KoanDiscoverable]` discovery, the **layered-memoization cache**
   (the plane tree), the **structural closure** (typed-slice invocation, no ambient access), **deterministic ordering**, and
   the **allocation benchmark** as acceptance gates. Land the standalone memoization fixes here: `ProjectionResolver` +
   `IndexMetadata` → Type-plane memoized (after making `IndexMetadata`'s group key deterministic); base name → `(Type, adapter)`.
1. **Write-stamp** — re-home identity + `[Timestamp]`; **delete** the inline `EnsureIdAsync`/`TimestampPropertyBag` calls
   from `RepositoryFacade`; **preserve the `BatchFacade` invariant** (batch writes are *not* timestamp-stamped today).
2. **Name-particle** — re-home partition; preserve the override→anchor / particle precedence.
3. **Schema-column** — `RelationalSchemaOrchestrator` consults column contributors; converge `[Column]`/property-`[StorageName]`
   and `[NotMapped]`/`[IgnoreStorage]`.
4. **Tenancy = registration** — `TenancyContributor` (4a schema-qualifier at Route, 4b discriminator column at Schema,
   write-stamp **into the dedicated column** [extends the write-stamp seam beyond in-Json mutation — re-derived empirically
   against each relational `ToRow`/`INSERT`], two-shaped read-filter, Key); add the tenant-isolation capability token;
   subsume `ITenantEnforcer`; the SQLite `AssertNoTenantLeak` proof (no-Docker, `(Id, Json)` envelope).
5. **+** classification (the second real consumer) and the **serialization/record hook** for bare-entity stores (JSON/Mongo).

---

## Consequences

- **One mental model** — every storage concern is "declare intent → a contributor realizes it"; nothing (partition,
  timestamps, identity) is special.
- **Tenancy and classification become registration, not code.**
- **Named deletions (net parts down, not up):** `RepositoryFacade`'s inline `EnsureIdAsync`/timestamp calls; `ITenantEnforcer`
  (absorbed into `TenancyContributor`); the per-call reflection paths in `ProjectionResolver`/`IndexMetadata` (replaced by
  memoized substructures); the hardcoded partition concat in `StorageNameGenerator`. Each re-homed concern *moves*, it is not
  wrapped.
- **A performance win, not a tax** — layered memoization removes per-op schema/name/index recompute that exists today; the
  allocation benchmark gates against any hot-path regression.
- **The Adapter Forge gets a concrete structural check**; **self-reporting** lists active contributors per entity.
- **Cost** — a foundational hot-path refactor, mitigated by behavior-preserving re-homing, the green ratchet, the memoization
  law, and the allocation gate. A **deliberate detour** that delays the first tenant no-leak proof by ~3 phases, with an
  honest interim tenancy posture (§7) rather than a hidden one.

## Carve-outs / open

- The **serialization/record-field hook** for bare-entity stores is deferred to phase 5 (its first real consumer).
- The exact **typed-slice API** for declared variance axes is settled in phase 0.
- The **`[Parent]` relationship** subsystem is out of this pipeline's scope (navigation, not storage composition).
- Classification's encrypt/tokenize/mask contributors are **future** (the ARCH-0095 classification axis).
