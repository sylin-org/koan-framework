# ARCH-0102: The Access Overlay — isolation as a pushed-down, adapter-realized definition block

> **R07 context amendment (2026-07-15):** AODB remains the Data realization contract. Durable
> logical-flow carriage is now the independent Core contract `IKoanContextCarrier` plus
> `KoanContextCarrierRegistry`; it is not a Data-axis plane and `.Carries(...)` has been removed.
> Read carrier-only Database-mode statements below as historical implementation notes; current
> Database routing derives from its declared Data field/source-key provider.

**Status**: Accepted (shape ratified by the Enterprise Architect, 2026-06-25; implementation phased below). **Phases 0–2 realized on `dev`** — overlay naming, provenance flags, the one composer, tenancy+soft-delete on `[DataAxis]`, and **Database-mode auto-routing** (`DatabaseRouteRegistry` + the `AdapterResolver` Priority-1.5 hook; external-only fail-closed). Remaining: Tier-2 visibility surfaces + two pinned Phase-3 follow-ons (strict-isolation `NullKeyBehavior`; overlapping-route boot detection) — see [the implementation plan](../architecture/aodb-implementation-plan.md).
**Date**: 2026-06-25
**Deciders**: Enterprise Architect
**Scope**: Establish that data-segmentation isolation is composed by the framework into a per-operation **Access Overlay Definition Block (AODB)** — an abstract, declarative description of *what* must be isolated — and **pushed down** to the adapter, which **realizes** it against its own schema and limits. The governing separation: the framework **informs the intent**; the vendor/adapter **resolves the mechanism**. This generalizes ARCH-0101's per-plane application into one pushed artifact and removes the conflation that made the framework *pre-choose a physical realization* (a metadata filter) a vendor could not honor (Weaviate's GraphQL-reserved `__` property mangling; soft-delete's field absent on the independent vector store).
**Related**: **ARCH-0101** (the data-axis model — this is its pushdown formalization; the contributors there become the AODB's *sources*) · **DATA-0106** (read-filter contributor seam — the predicate source) · **DATA-0105** (managed-field stamp source) · **ARCH-0096** (`IdentifierComposer` — the name/particle engine) · **ARCH-0100** (durable ambient carrier — the per-op value transport) · **ARCH-0094** (Adapter Forge / the **Conformance Gate** — the realization verifier) · **ARCH-0098/0099** (classification / tenancy — the first axes) · **[koan-design-principles]** (conformity-by-design; descriptor-not-callback; fail-closed; compute-plan-once/value-per-op) · **[contributor-pipelines-never-bespoke]** · **[tenancy-golden-contributor-standard]**.

---

## Context

ARCH-0101 established that all data-segmentation is registered contributors over composition planes, and that an axis registers into the planes its **mode** requires (`Shared` → field + filter · `Container` → name-particle · `Database` → routing). That model is correct and shipped. But it left *where the physical realization is decided* implicit, and the default answer turned out to be "the framework decides." The framework folds the read-filter into a `Filter`, computes the storage name, stamps the field — and hands the adapter a **pre-baked artifact**. The adapter never gets to say "for this isolation intent my store has a better native mechanism."

That conflation is the root of a recurring class of pain, and the vector plane is its clearest instance:

- **The framework picked "metadata filter" for vector isolation.** All tenants share one index; we AND `__koan_tenant == X` into every KNN. The isolation lives entirely in a predicate the *vendor* must honor.
- **Weaviate queries over GraphQL, which reserves `__`** for introspection — so a property literally named `__koan_tenant` is illegal/mangled. The framework imposed a field name the vendor cannot accept.
- **Weaviate has first-class native multi-tenancy** (tenants as isolated shards — true physical isolation). The framework bypassed it and forced a fragile filter instead. We picked the realization; the vendor had a better one and we never asked.
- **Soft-delete's `__deleted`** is set by an *operation* (delete), never stamped on the *vector write* — so pushing its filter to the vector store references a field that store never materialized (vacuous at best, an over-filtering break at worst). [ARCH-0101 §4's operation-override plane and the vector decorator's `FoldReadScope` exclusion are the local patches this ADR generalizes.]

The diagnosis from the architect dialogue: **blob storage isolates by physical key separation (robust); vector isolates by a shared-store filter the vendor must honor (fragile) — and the fragility is not "vectors," it is "the framework chose the realization."** The fix is to push the *intent* and let the adapter choose the *mechanism*, behind a capability and a conformance gate.

---

## Decision

### 1. The Access Overlay Definition Block (AODB)

For every data operation (query, get, upsert, delete, …) the framework composes — from the registered contributors and the current ambient — an **Access Overlay Definition Block**: an abstract, ordered set of **overlay elements** describing the isolation that must hold for this operation. The AODB is pushed down with the operation; the adapter **realizes** each element against its own schema and limits, or **fails closed**.

An overlay element is exactly one of three shapes, one per ARCH-0101 mode:

| Element | Mode | Carries | The adapter realizes it as… |
|---|---|---|---|
| **Moniker** | Database | `{ axis, moniker, provisioning-posture }` | route (and per posture, provision) a separate database / keyspace |
| **Particle** | Container | `{ axis, name, value, position, separator }` | a name particle around the anchor (`TBID_Todo` collection / table / native tenant shard) |
| **FieldFilter** | Shared | `{ axis, field, value, operator, provenance }` | a stamped property + a `WHERE`/metadata predicate |

The block also carries two pieces of context: the **overlay marker** (`__`, §5) and the resolved **target adapter's declared naming policy** (§5).

The AODB is **the one object that is both what we isolate by and how we key the cache** (§7). The contributors of ARCH-0101 are unchanged — they are the **sources** that compose the AODB; this ADR adds the composed artifact, its pushdown, and the adapter's realization contract. **Partition folds in as a Particle source** — DATA-0077 partition is structurally a Container particle and is expressed as one (a small consolidation, not a behavior change).

### 2. The mode is the mechanism dial — not the adapter's whim

The framework **always** pushes the overlay; the adapter **never** decides *whether* to isolate. What the adapter chooses is bounded by the element's **mode**, and the mode is selected by the contributor/placement (framework-side intent), not by the adapter:

- A **Moniker** says "separate database" → the adapter routes/provisions. It may use a separate DB, a schema, or a native multi-tenant shard — whatever *is* separate storage for it.
- A **Particle** says "a name around the anchor" → the adapter resolves its physical name (`TBID_Todo`, or a native tenant shard).
- A **FieldFilter** says "stamp + filter" → the adapter stamps the field on write and applies the predicate on read. Here the adapter's *only* freedom is store-legal naming (§5); it does **not** get to drop the predicate.

**Consequence (the Weaviate native-tenancy answer):** native multi-tenancy is reached by declaring the vector axis **Container/Database mode**, not by an adapter silently substituting a mechanism behind a `Shared`-mode filter. Mechanism selection is an explicit, declared mode decision. This keeps the trust surface honest: in `Shared` mode the isolation predicate is framework-owned and unconditionally applied; in `Container`/`Database` mode the adapter realizes routing/naming and is **conformance-gated** (§9).

### 3. Pillar I — element provenance: `ambient-stamped` vs `operation-sourced`

Every **FieldFilter** element carries its field's **provenance**:

- **`ambient-stamped`** — the value is read from the ambient and written on *every* write of the entity (tenant `__koan_tenant`, moderation `__vis`). Every store that materialises the entity materialises this field.
- **`operation-sourced`** — the value is set by a *specific operation*, not an ambient write (soft-delete's `__deleted`, set by `Delete`; ARCH-0101 §4's operation-override). Only the store that performed that operation holds the field.

**The push is store-aware:** the framework pushes a `FieldFilter` element to a target store **only if that store materialises its field.** `ambient-stamped` ⇒ pushed to every store the entity lives in (primary, vector, …). `operation-sourced` ⇒ pushed **only to the store that performed the operation**; a secondary store (e.g. the independent vector index, which never received the soft-delete) either omits the element or fails closed. This is the generalisation of the vector decorator's `FoldReadScope` exclusion — lifted from a vector special-case into a property of the element. **It is the single subtlety that bites silently**, so it is a first-class field of the AODB, not an adapter convention.

### 4. Pillar II — the fail-closed guarantee stays framework-side for `Shared`; the adapter realizes the mode

The isolation guarantee location is **explicit per mode**:

- **`Shared`/FieldFilter:** the predicate is **always composed and always pushed** by the framework; the adapter must translate it (as it translates any `Filter` today) — it cannot skip it. The adapter's only realization freedom is store-legal field naming (§5). Therefore the *only* residual leak vector in `Shared` mode is a **non-bijective or inconsistent rename**, which §5 makes structurally impossible and §9 round-trips. The framework owns the guarantee.
- **`Container`/`Database`:** the adapter realizes routing/naming and **must fail closed** if it cannot honor a pushed element (no separate keyspace it can route to ⇒ refuse, never silently co-mingle). This is the larger trust surface, and it is exactly the isolation-critical code the **Conformance Gate** (§9) certifies.

This is the precise answer to "does fail-closed move into the adapter?": **for the shared-filter mode, no** — the framework still owns "isolation = this predicate, applied," because it always pushes it and the adapter cannot decline. For the routing modes, the adapter realizes-or-fails-closed, certified by the gate.

### 5. Pillar III — overlay naming: a framework-owned transform, adapter-declared, closed-grammar, injectivity-verified

Injected overlay fields use a reserved **marker** — `__` (`__koan_tenant`, `__deleted`, `__vis`). Some stores cannot accept the canonical spelling (Weaviate/GraphQL **reserve** `__`). The last-mile rename to a store-legal spelling is **framework-owned and adapter-declared**, never hand-rolled per adapter:

1. **The adapter declares a naming rule once** — an extension of the existing naming-capability descriptor (`GetNamingCapability` / `StorageNamingCapability`). It is a **rule over the whole injected namespace** ("spell any `__`-overlay field *this* way"), not a per-field fix — so every axis (tenant, soft-delete, moderation, and any future axis) is covered without the adapter knowing those axes exist (the adapter stays contributor-agnostic too).
2. **Override-only.** Silence is the contract: Mongo / SQL Server declare nothing and get the `__` default. Only a store whose identifier law rejects it speaks up.
3. **The framework applies the rule everywhere** — write-stamp, read-filter, index DDL, schema-create — from that one declaration. **Declare, don't call:** the adapter does not invoke a per-operation `Mutate(...)`; the framework applies the single declared rule at every touchpoint, so the write-name and the read-name are *structurally* identical (bijective by construction). A call-site API would reintroduce "forgot to rename the write path → silent no-op → leak."
4. **Closed grammar, not a callback.** The rule is a closed set — `{ marker-replace, prefix, case-style, separator, max-length/truncation }` — *never* a `Func<string,string>`. Only a declarative rule can be **verified injective at boot** (reusing the `PartitionTokenPolicy.IsInjective` guard from ARCH-0101 §3). Two injectivity checks, not one:
   - **among overlay fields** — no two `__`-fields collapse to the same spelling; and
   - **against the entity's own members** — the rename *spends* the reserved-namespace protection (`__koan_tenant` is GraphQL-illegal *but collision-safe*; `koan_tenant` is legal *but can now collide* with a real user field). The post-transform name must be injective against the declared entity members, or stay within a reserved-by-convention prefix.
5. **Escape hatch for the long tail.** A store with a genuinely weird identifier law (length limit *and* no underscores *and* reserved words) may declare a **custom injective map** — but that map is isolation-critical and is therefore round-tripped by the Conformance Gate (§9). Safe-by-construction default; possible-but-certified long tail.

**Worked example (the whole Weaviate fix in one declared line):**

> Weaviate: *"the `__` marker is GraphQL-reserved — spell overlay fields `koan_` instead."*

From that single declaration: `__koan_tenant` → `koan_tenant`, `__deleted` → `koan_deleted`, every future axis → `koan_*`, applied identically on write, read, index. No per-field code, no per-axis code in the adapter. (Note the empirical correction baked in: `-` is *also* GraphQL-illegal, so the store-legal target is `koan_`, not `-`; the rename target is per-store and **verified against the live adapter**, never assumed — which is why the Weaviate Testcontainers spec is the acceptance test for this pillar.)

### 6. Pillar IV — provisioning is a posture; migration is out of scope

`Database`-mode realization needs the target keyspace to exist. This rides the ensure-created machinery the adapters already run (`RelationalSchemaOrchestrator`, Mongo lazy collection create), keyed off the AODB moniker. **Provisioning authority is a declared posture**, not an assumption:

- **`lazy`** — provision the DB/store on first touch (dev, and prod where the app may create).
- **`eager`** — provision all registered entities' stores for a tenant up front (avoids first-request latency).
- **`external-only`** — the adapter **routes and fails closed if the keyspace is absent**; it never provisions. This is the least-privilege prod posture (DBs come from IaC; the app's credentials cannot `CREATE DATABASE`). It is first-class, not an edge case.

**Explicitly out of scope for the AODB** (named so "routing works" is never mistaken for "DB-per-tenant is done"):
- **Migration across *existing* tenant DBs** — ensure-created provisions a *fresh* keyspace at the current schema; it does nothing for tenant DBs created under a prior version. That is a migration-sweep concern (the P8 relocate/migration saga / additive-only canon), a sibling plane.
- **Connection-pool lifecycle** across many tenant keyspaces (LRU/eviction of idle pools) — the adapter's implementation concern.
- **Backup / health per keyspace.** The AODB *routes*; lifecycle is a sibling.

### 7. Pillar V — layered memoization, keyed by the resolved AODB tuple

The AODB resolves at three stability tiers, and each memoizes accordingly (compute-plan-once / value-per-op):

| Resolved thing | Key | Lifetime |
|---|---|---|
| **The rename rule** (`__` → `koan_`) | `(adapter, entity-type)` | **cache forever** (structural; boot-stable) |
| **The resolved physical name** (`TBID_Todo`) | `(entity-type, axis-values…)` — *the resolved AODB value-tuple itself* | **evictable** (runtime-keyed; grows with tenants/partitions) |
| **The filter** (`WHERE koan_tenant = A`) | — | **never memoized whole** — the field-name is stable (cache the rename map), the literal varies (fill per-op) |

Two correctness properties make this *isolation*, not merely *performance*:

- **The cache key must include every axis value that shaped the name.** "Cache this name for tenant A, storage B, partition C" is safe *only* if the key is complete — drop partition and you serve A's partition-C name to a partition-D request: the cache itself becomes the leak. We already hit and guarded exactly this in the ARCH-0101 §3 name-particle work (the ambient axis value is folded into the name-cache key precisely to stop a cross-tenant name bleed). The clean property that falls out: **the resolved AODB value-tuple *is* the cache key** — the thing you isolate by and the thing you key the memo by are one object.
- **Placement change evicts.** The one event that invalidates a resolved name is a tenant changing modes (A relocated from `Shared` to `Database` by the relocate saga). That is why resolved names live on the *evictable* side, not the structural one: forever **until placement changes**.

### 8. The adapter contract

Composed framework-side from contributors + ambient; realized adapter-side. Mostly existing mechanism, formalized, plus two genuinely new bits.

**The adapter declares (boot, as data):**
- its **naming rule** (§5) — override-only;
- its **mode-realization capabilities** — which of `{ FieldFilter, Particle, Moniker }` it can honor (an InMemory vector store: `FieldFilter` only; Weaviate: `FieldFilter` *and* native-tenant `Particle`/`Moniker`; a relational store: all three).

**The adapter receives (per operation):**
- the **renamed** filter, already in its dialect, inside the query it already translates (existing `IFilterTranslator` path — a renamed field, nothing structurally new);
- the resolved scoped **name** (existing `StorageNameGenerator` path);
- the **moniker / routing value** via the ambient carrier (ARCH-0100), for `Database` mode.

**The adapter realizes:**
- `Shared` → translate the (renamed) predicate — *as today*;
- `Container` → address the scoped name / native shard;
- `Database` → route, and per the posture (§6) provision or fail closed;
- **any element it cannot honor ⇒ fail closed**, never silently co-mingle.

The deliberate consequence: in `Shared` mode the adapter does essentially what it does today (translate a `Filter` whose field is spelled in its own dialect). The new surface is concentrated in `Database`-mode routing/provisioning and the declared naming rule — both small, both conformance-gated.

### 9. The Conformance Gate is load-bearing here (ARCH-0094)

AODB-realization *is* the isolation-critical adapter code the Adapter Forge's **Conformance Gate** exists to certify. The Gate round-trips the two surfaces this ADR creates:

- **Naming bijection** — write under scope A, read under scope B, assert empty; write/read/index all agree on the field spelling.
- **Mode realization** — a `Particle`/`Moniker` adapter must prove cross-scope reads are physically isolated; a declared custom naming map (§5.5) is round-tripped.

A capability token an adapter cannot back under the Gate is a no-capability-lie and fails the build — the same honesty mechanism that keeps ARCH-0084 capability claims true.

---

## Consequences

**What falls out as a consequence rather than a special case:**
- **The Weaviate property-name fix** is one declared line (§5 worked example) — and, optionally, declaring the vector axis `Container` mode realizes Weaviate **native** multi-tenancy instead of a metadata filter.
- **The soft-delete-on-vector fix** is a consequence of element provenance (§3): an `operation-sourced` element is not pushed to a store that never materialised it. The bespoke `FoldReadScope` exclusion is re-homed as a property of the AODB.
- **Storage, cache, data, and vector planes converge** on one composed artifact instead of four per-plane application points.

**Costs / enlarged surfaces (named honestly):**
- **`Database`-mode realization is real adapter work** (routing, posture-aware provisioning, pool lifecycle) — but it rides existing ensure-created machinery and is bounded by the posture knob.
- **The Conformance Gate becomes a prerequisite**, not a nicety, for any adapter claiming `Container`/`Database` realization or a custom naming map.
- **An interface/contract change**: adapters gain a declared mode-realization capability + the naming-rule declaration; the moniker reaches them via the carrier. `Shared`-only adapters are nearly untouched.

**What does not change:**
- The ARCH-0101 contributors (`ManagedFieldDescriptor`, `IReadFilterContributor`, `IStorageNameParticleContributor`, the operation-override, the carrier) are unchanged — they become the AODB's **sources**. This ADR re-homes; it does not rewrite.
- `Shared`-mode adapters keep translating a `Filter` as today (only the field spelling may differ).
- Off (no axis registered) ⇒ empty AODB ⇒ structural no-op ⇒ byte-identical (Reference = Intent).

---

## Non-goals

- **Not a lifecycle engine.** Migration across existing tenant keyspaces, per-tenant backup/health, and connection-pool eviction are sibling planes (P8 saga / adapter implementation), explicitly out of scope (§6).
- **Not the placement policy.** *Which* mode a tenant gets (A isolated, C shared) is decided by the placement/entitlement plane (the P6 broker); the AODB *realizes* whatever mode is current, it does not decide it.
- **Not a new contributor model.** The sources are ARCH-0101's contributors, untouched.

---

## Sequencing

1. **AODB with boot-fixed mode.** Compose + push + realize with the mode declared per-axis at boot (today's reality). Closes the vector/Weaviate friction and unifies the planes. Phase-gated by the Weaviate Testcontainers spec (the acceptance test for §5).
2. **Per-tenant mode (placement).** Unify axis-mode with per-tenant placement (A isolated while C shares) — the prize, but it drags in the P6 broker and the relocate saga (§6, §7 eviction). Lands second.

Each phase follows the standing discipline: ADR-or-design-first → TDD → ARCH-0079 real-`AddKoan()` spec → impl-diff adversarial review → byte-identical regression → Conformance-Gate round-trip → commit.

---

## The five pillars, in one place

1. **Element provenance** — each AODB element declares `ambient-stamped | operation-sourced`; the push is store-aware (an operation-sourced field is never pushed to a store that never materialised it). *Closes the soft-delete-on-vector class.*
2. **Mode is the mechanism dial** — the framework always pushes the overlay and owns the `Shared`-filter guarantee; the adapter realizes the *mode* (routing/naming), never *whether* to isolate. Native tenancy is a declared mode, not an adapter whim.
3. **Provisioning is a posture** (`lazy | eager | external-only`); **migration across existing tenant DBs is out of scope** (a sibling plane).
4. **Overlay naming is a framework-owned transform** — adapter-declared (override-only, a rule over the whole `__` namespace), closed-grammar, boot-verified-injective (against overlay fields *and* entity members), applied by the framework at write/read/index from one declaration.
5. **Layered memoization keyed by the resolved AODB tuple** — the rename rule caches forever; the resolved name caches per axis-tuple until placement changes; the filter never caches whole. Key completeness is an isolation property; the AODB value-tuple *is* the key.

---

## Addendum — clarifications from the convergence investigation (2026-06-25)

A 6-reader investigation (`wf_def3d6c3-f98`) mapped every current isolation surface onto this ADR and an adversarial critic stress-tested the resulting plan ([aodb-implementation-plan.md](../architecture/aodb-implementation-plan.md)). The plan is *directionally sound* but the critic corrected several places where this ADR's prose was imprecise or understated. These corrections are canon; the body above is unchanged for the record.

- **§3 provenance is DERIVED, not author-typed.** `FieldProvenance` is set **once** in `DataAxisExpander` from the declared shape — `Provenance = (axis.OnDeleteValue is not null) ? OperationSourced : AmbientStamped` — and stored as a new `ManagedFieldDescriptor` field. **Do not add an author verb** (the surface must shrink). **And the predicate-axis case is a relocation, not a deletion:** an `IReadFilterContributor` returns an *opaque* `Filter` tree (e.g. soft-delete's `Any(Exists(__deleted,false), Ne(__deleted,true))`) with no per-field provenance, so the composer **must still walk the tree** and match leaves against the operation-override registry to decide the store-aware push — the exact `FilterMentions` logic the vector decorator runs today, relocated into the one composer (2 sites → 1, honestly).

- **§4/§8 the soft-delete *write* stays tenant-gated.** An operation-sourced delete-mark and the ambient-stamped tenant predicate have different provenance, but a soft-delete UPDATE must carry **both** — the `__deleted=true` to set **plus the full ambient read-scope as the `WHERE`**. "Push only to the store that performed the op" governs *which stores* see the element, never *whether* the tenant predicate gates the write. (Plan invariant FC-1, with a cross-tenant acceptance test.)

- **§5.4 injectivity is a NEW domain, not `IsInjective` reuse.** `PartitionTokenPolicy.IsInjective` round-trips **one value** (the Particle/partition value domain). The overlay-naming checks are over **field-name *sets*** post-transform — pairwise-distinctness *and* disjointness from the entity's members — a different domain needing a new `OverlayNameInjectivityVerifier`. Member-disjointness needs the **adapter-projected** member set (relational columns vs serialized property names vs vector properties = a `ProjectionResolver` that does not exist yet). Until it does, **Phase 0 down-scopes the gate to reserved-prefix preservation** (the post-transform name stays in a reserved-by-convention namespace, §5.4's escape, so disjointness holds by construction).

- **§8 the moniker reaches the adapter via `Create(sp, source)`, not the carrier.** The Database-mode routing value rides the existing `IDataAdapterFactory.Create(sp, source)` argument (resolved from `EntityContext` source routing, DATA-0077). `AmbientCarrierRegistry` (ARCH-0100) only *restores the ambient slice across the async hop* — it is not the adapter-facing channel. **Database-mode realization is greenfield:** `AxisMode.Database` registers only a carrier today (the expander's Database case is a no-op), so the Moniker element has no existing realization path — net-new, bounded by the §6 posture, not a re-home. Capability tokens: `DataCaps.Isolation.RowScoped` **is** the FieldFilter token; **add** `ContainerScoped` (Particle) and `DatabaseScoped` (Moniker), reconciling with the pre-existing `VectorCaps.DynamicCollections`; each co-defined with its ARCH-0094 Conformance module.

- **§163 "the contributors are unchanged" means the seam *shape*, not the record fields.** Two source records gain declarative metadata: `ManagedFieldDescriptor.Provenance` and `StorageNamingCapability.OverlayNaming`. **`Mode` is NOT carried on `ManagedFieldDescriptor`** (it is always `Shared` there — a tautology); mode lives on the new `AodbElement` union where it discriminates.

- **§7 the cacheable bit is isolation-legs-only.** The `Aodb.Cacheable` bit carries the *isolation* exclusions (non-equality / operation-sourced elements). The orthogonal **field-transform / `[Classified]` encryption-at-rest** exclusion (`StorageFieldTransformRegistry`, ARCH-0098) **stays independent** — it is not an isolation property and folding it into the AODB is scope creep. `CachedRepository` ORs `(transform-exclusion || !aodb.Cacheable)`.

- **Two fail-closed surfaces this ADR did not name** (now plan gates): **`BatchFacade.Delete(id)`** today bypasses IDOR read-scoping *and* the soft-delete override — the "route every op through the composer" framing must cover it or leave a SURFACES tripwire (FC-2). And **off ⇒ byte-identical** is the single most important non-regression criterion and gets a *dedicated* test (the `IsEmpty` short-circuit at every collapsed site), not a phase note (FC-5).

Full consolidation map, phasing, the fail-closed invariants, and the resolved design questions are in the implementation plan.

---

## Addendum II — the delight inversion (2026-06-25)

The architect re-framed the open questions around developer **delight**, ignoring churn/effort: *"implement with zero config to see it working right now, with sane defaults that just work; and if you can, enrich your flow for better control/visibility. We're enabling the user."* This is the **design law** for the AODB, and it resolves the open questions — most of which were false either/ors.

### The delight ladder
- **Tier 0 — do nothing; isolated and safe.** Reference `Koan.Tenancy` → every entity is isolated across data, blob, cache, and vector. No attribute, no `UserId`, no filter, no config. **Dev-open** so you see it working *right now*; **prod-closed** so you cannot ship a leak. The AODB is what makes "one reference → all planes" true.
- **Tier 1 — enrich for control; one line, zero app-code change.** Declare `Mode = Database` → that tenant routes to its own database (the adapter provisions it). Declare Container/Database on a vector axis → Weaviate uses native multi-tenancy automatically. **The same entity code runs at every isolation strength** — multi-provider transparency extended to *isolation strength*: you declare the strength, you never rewrite the app.
- **Tier 2 — enrich for visibility.** `.Explain()` renders the composed overlay; the boot report shows each entity's AODB; `AssertNoLeak` is one line in a test. The composed AODB is a *thing you can look at*.

### Resolutions
- **Provenance is DERIVED FLAGS, both allowed (resolves OQ-2 — "have both").** `FieldProvenance` becomes `[Flags] { AmbientStamped, OperationSourced }`. The author declares only intent — `.Field(x, provider)` sets `AmbientStamped`; `.OnDelete/.OnFlag(...)` sets `OperationSourced`; both verbs on one field set both. **Zero provenance config; it falls out of the declared verbs.** This is not just ergonomic — it is what lets a future **Moderation** axis (ambient-stamp `__vis` *and* an operation that flips it) be expressible at all, which the XOR enum could not. It also sharpens the store-aware push (next).
- **Store-aware push = "current in *this* store," not "present."** A store S can enforce a field's predicate iff `AmbientStamped-in-S AND (every operation that mutates the field also runs in S)`. Tenant (pure ambient) ⇒ current everywhere. Soft-delete on the independent vector store ⇒ present-but-stale (the delete never reached the vector) ⇒ fail closed until lifecycle-sync. The right answer falls out of the flags instead of being a special-cased exclusion.
- **The AODB is a first-class, INSPECTABLE artifact (resolves OQ-1 toward explicit).** It is a real, public, composable object — *because visibility is the Tier-2 payoff* (it drives `.Explain()`, the boot report, the Conformance Gate, and adapter-author clarity), not merely to avoid an interface change.
- **Lazy provisioning + dev-open are the Tier-0 defaults (resolves OQ-6); eager / external-only / prod-closed are enrichments.** A new tenant's store appears on first touch in dev; you opt into boot-probe/fail-fast when you want it.
- **Self-explaining fail-closed is a first-class acceptance criterion (new — FC-7).** An empty result or a refused boot must say *why*: "this entity is tenant-isolated and no tenant is in scope — `Tenant.Use(...)` or mark `[HostScoped]`." Fail-closed without a message is a delight-killer; with one, the framework *teaches*. `.Explain()` is that, on demand.
- **The honest boundary.** Zero-config-just-works is bounded by what the environment *permits*: the prod app cannot `CREATE DATABASE`, so Database-mode's prod default is **external-only** — route, and if the keyspace is absent, fail closed with a message naming exactly what to provision. Delight in prod is *clarity*, not auto-magic.
- **Other resolutions:** OQ-3 — coarse capability token (Tier-0 routing) + optional structured detail (Tier-1 precision), both. OQ-4 — partition is *viewed-as* a Particle (zero developer impact, Couchbase still native-routes; unified in `.Explain`). OQ-5 — the resolved AODB tuple is the shared *key shape*; a shared resolved cache is an invisible perf detail, but the resolved AODB is inspectable.

**First-class goals the phasing must hit (not just leak-proofing):** the same entity code at every isolation tier · self-explaining fail-closed · the inspectable overlay · zero-config lazy provisioning. These are in the implementation plan's *Delight ladder* section and its acceptance gates.
