# ARCH-0102 (AODB) — the break-and-rebuild convergence plan

- Date: 2026-06-25
- Status: **Plan, pending architect sign-off** (no code moved yet)
- Decision: [ARCH-0102 — the Access Overlay Definition Block](../decisions/ARCH-0102-access-overlay-definition-block.md)
- Mandate (verbatim): *"adjust everything we have to adhere to this ADR. Break-and-rebuild targeting 'fewer but more meaningful moving parts' is a mandate."*
- Provenance: a 6-reader investigation Workflow (`wf_def3d6c3-f98`) mapped every isolation surface onto the AODB, synthesized this plan, and an adversarial critic verified it. **The critic's verdict — *directionally sound, materially overstated* — is folded in below**: three "collapses" were honestly downgraded (one relocation, one scope-limit, one tautology dropped) and two fail-closed gaps were pinned as Phase-1b acceptance gates.

---

## The honest consolidation map

The AODB's value is one composed artifact replacing N independent re-derivations of the same scope. The reductions are real; where the critic showed a "collapse" was actually a relocation, it is marked **[relocation]** and the parts-delta is reported honestly.

| # | Before (the moving parts) | After (fewer, more meaningful) | Δ |
|---|---|---|---|
| 1 | `ReadScopeFold` + `ManagedEqualityReadContributor` + facade `ReadScopeFilter/CurrentManagedValues` + vector `FoldReadScope` + `StorageKeyScoper.ComposeEqualityParticles` + `ScopedEntityCacheKey.AppendScope` — **6 sites each re-deriving scope from `EqualityFields`/`ReadScopeFold`** | **ONE `AodbComposer`** folding the (unchanged) source registries + ambient once into an `Aodb` of provenance/mode-tagged elements; `DataAxis.Explain` consumes the same composer so the diagnostic can't drift | **6 → 1** |
| 2 | facade `ApplyManaged` + 6 `ScopedById/ScopedByIds` IDOR-lowering sites + 4 `OverrideUpsert` soft-delete loops + vector `Search` managed-AND | **ONE store-aware push** at the chokepoint; the 4 delete-shape loops collapse to the single operation-sourced element + the **retained** tenant predicate (see invariant FC-1) | **11 → 1** |
| 3 | `Koan.Tenancy` hand-registers `__koan_tenant` + carrier; `Koan.Data.SoftDelete` hand-registers `__deleted` + read-contributor + override — **5 hand-wired axis-plane seams** | **2 `IDataAxis.Declare` bodies** (`TenantAxis`, `SoftDeleteAxis`); provenance falls out of the `.Field`/`.OnDelete` shape. Guard + posture stay (policy, not plane) | **5 → 2** |
| 4 | `ManagedFieldDescriptor` (no provenance) + `AxisMode` discarded at expansion + vector's `OperationOverrideRegistry` cross-reference + facade's `Current`/`Overrides` split as the only provenance encoding | **`ManagedFieldDescriptor.Provenance`** (new `FieldProvenance` enum) set once in `DataAxisExpander`. **Mode is NOT carried on the descriptor** (critic LOW: it's always `Shared` there — a tautology); mode lives on `AodbElement` where it discriminates | **2 concepts → 1 descriptor field** |
| 5 | `StorageNamingCapability` (anchor/partition only) + Weaviate `IsValidWeaviateProperty` (silently **drops** `__`-fields) + `WeaviateFilterTranslator` read-side (no rename) — **3 per-adapter name sites** | **ONE override-only `OverlayNamingRule`** on the naming capability; framework applies it at write/read/index from one declaration. Weaviate declares `__ → koan_` in one line | **3 → 1** |
| 6 | `CachedRepository._excludeFromCache` 3-way OR + `DataAxis.Explain` 4th derivation | **ONE `Cacheable` bit** on the `Aodb` for the **isolation legs only**. The `StorageFieldTransform`/`[Classified]` encryption leg **stays independent** (critic MEDIUM: it is not an isolation property — folding it in is scope creep) | **4 → 2** (2 axis legs → 1 bit; transform leg unchanged) |
| 7 | `StorageKeyScoper` (leading `/` particle) + `ScopedEntityCacheKey` (trailing `::` segment) + `StorageNameGenerator` (partition special-cased) | **3 thin policy-only renderers over the one `Aodb` Particle set** through `IdentifierComposer`; partition folds in as a Particle source — **gated on the Couchbase `EncodePartitionInName=false` byte-identical regression** (critic LOW) | **3 re-gatherers → 3 renderers + 1 source** |

**Net moving-part count drops materially.** The one place the headline overstated: the vector `FoldReadScope` tree-walk is **relocated into the composer, not deleted** — see FC-3.

---

## The AODB design

### New types
- `src/Koan.Data.Core/Aodb/AodbComposer.cs` — a static, **type-memoized** composer (mirrors `EqualityFields`' compute-plan-once). `Compose(entityType)` reads the three unchanged source registries (`ManagedFieldRegistry`, `StorageNameParticleRegistry`, `OperationOverrideRegistry`) + the DI `IReadFilterContributor[]`, and emits an ordered `Aodb`. The **structural plan** (which elements, modes, provenance, cacheable bit) is memoized per entity-type forever; the **per-op values** are filled at the chokepoint. `IsEmpty ⇒ empty Aodb ⇒ byte-identical no-op.`
- `src/Koan.Data.Abstractions/Aodb/Aodb.cs` — `{ ImmutableArray<AodbElement> Elements, string OverlayMarker, StorageNamingCapability Naming, bool Cacheable }`.
- `src/Koan.Data.Abstractions/Aodb/AodbElement.cs` — the discriminated union: `FieldFilter{ axis, field, value, operator, provenance }` | `Particle{ axis, name, value, position, separator }` | `Moniker{ axis, moniker, provisioningPosture }`. **Mode is implicit in the element shape** (FieldFilter=Shared, Particle=Container, Moniker=Database).
- `src/Koan.Data.Abstractions/Pipeline/FieldProvenance.cs` — `enum { AmbientStamped, OperationSourced }`.
- `src/Koan.Data.Abstractions/Naming/OverlayNamingRule.cs` — closed-grammar record `{ MarkerReplace, Prefix, CaseStyle, Separator, MaxLength }` (reuses `NameCasing`/`NamingUtils`).
- `src/Koan.Data.Abstractions/Naming/OverlayNameInjectivityVerifier.cs` — a **new domain** (NOT `PartitionTokenPolicy.IsInjective`, which round-trips one value): pairwise-distinctness of the rule-transformed overlay-name set, plus member-disjointness (Phase-0 scope decision below).
- `src/Koan.Data.Core/Axes/ProvisioningPosture.cs` — `enum { Lazy, Eager, ExternalOnly }` (Phase 2).
- `DataCaps.Isolation.ContainerScoped` + `DatabaseScoped` tokens (beside the existing `RowScoped` = FieldFilter), each **co-defined with an ARCH-0094 Conformance module** (a token can't ship without its gate).
- `src/Koan.Tenancy/Axes/TenantAxis.cs` + `src/Koan.Data.SoftDelete/Axes/SoftDeleteAxis.cs`.

### Deleted
- `ManagedEqualityReadContributor` (its `Filter.Eq` emission moves into the composer).
- Vector `FoldReadScope`/`FilterMentions`/`AnyMentions` as a *standalone fork* (the logic **relocates** into the composer — FC-3).
- The hand-registration bodies in both `KoanAutoRegistrar`s + `SoftDeleteReadContributor` (→ the two `Declare` bodies).
- Weaviate `IsValidWeaviateProperty` drop-on-write (→ the declared rule).

### Pushdown — no new wire signature (rides existing seams)
The critic confirmed every value already has a transport, so the **default is implicit-transport-with-one-composer**, not a breaking interface change (this is open question OQ-1):
- **FieldFilter (read):** rendered to a `Filter` and ANDed into the `QueryDefinition` exactly as `ReadScopeFilter` does today; the adapter receives a **renamed** field inside the `IFilterTranslator` path.
- **Write-stamp values:** ride the existing `ManagedFieldWriteScope` AsyncLocal (`Current`=ambient-stamped+guarded, `Overrides`=operation-sourced — which **already encodes provenance**).
- **Moniker (Database routing):** rides the existing `IDataAdapterFactory.Create(sp, source)` argument (resolved from the `EntityContext` source slice, DATA-0077), **not** the carrier — the critic corrected the ADR's conflation: `AmbientCarrierRegistry` (ARCH-0100) *restores the ambient slice across the async hop*, it is not the adapter-facing channel.

### Adapter-interface delta — minimal, declarative
- `StorageNamingCapability.OverlayNaming` (override-only; `null` = `__` default). **Declare-don't-call.**
- The `ContainerScoped`/`DatabaseScoped` capability tokens.
- **No new method** on `IFilterTranslator`/`IDataRepository`/`IQueryRepository`/`IVectorSearchRepository`/`IDataAdapterFactory`. Shared-only adapters (Mongo/SQL/SQLite) are byte-identical.

---

## Fail-closed invariants (the critic's `preservesFailClosed: false` — pinned as gates)

These are **acceptance criteria**, not aspirations. Each phase that touches the chokepoint must prove the relevant ones with an ARCH-0079 real-`AddKoan()` spec before merge.

- **FC-1 — soft-delete write is tenant-gated.** A soft-delete UPDATE composes the operation-sourced element (`__deleted=true` to *set*) **PLUS the full ambient read-scope** (tenant equality) as the UPDATE's `WHERE`. *Test:* tenant A soft-deletes id X owned by B → 0 rows, X still visible to B. *Until proven per adapter, keep the load-then-rewrite* (don't assume every adapter does a predicated UPDATE).
- **FC-2 — batch delete is not an IDOR hole.** `BatchFacade.Delete(id)` / `CachingBatchSet.Delete` today bypass IDOR read-scoping **and** the soft-delete override (a pre-existing hole the consolidation must not re-bless). Route it through the same Aodb as single-delete, or document it as an explicit gap with a SURFACES tripwire. *Test:* batch-delete of another tenant's id → no-op; batch-delete of a `[SoftDelete]` entity → soft-removes.
- **FC-3 — predicate-axis provenance is a relocation, reported honestly.** An `IReadFilterContributor` (e.g. `SoftDeleteReadContributor.HideDeleted = Any(Exists(__deleted,false), Ne(__deleted,true))`) returns an **opaque Filter tree with no per-field provenance**. To decide store-aware push for a predicate axis, the composer **must still walk the tree** and match leaves against the operation-override registry — the exact `FilterMentions` logic. So this is **2 sites → 1** (the vector fork + the implicit facade assumption → one composer derivation), **not** a deletion. *Test:* a moderation-style predicate over a non-stamped field is correctly omitted from the vector push by the composer.
- **FC-4 — read-scope pushability fail-closed carries verbatim.** The facade's `RequireScopeForRead`/`FilterSplitter` pushability check (a non-fully-pushable scoped read *throws*) must survive into the Aodb render unchanged — and the **weaker** vector fail-closed (no such check today) converges *up* to the facade's rigor.
- **FC-5 — off ⇒ byte-identical.** The single most important non-regression criterion: `IsEmpty` short-circuits to an empty Aodb at **all** collapsed sites. **Dedicated test**, not a phase note.
- **FC-6 — naming bijection.** write-name == read-name == index-name under the declared rule; live-adapter round-trip (write under A, read under B → empty) is the §9 gate.

---

## Phasing (per ARCH-0102 §Sequencing — boot-fixed mode first, placement second)

### Phase 0 — Close the Weaviate vector gap (the §5/§9 acceptance gate)
The whole sequencing is phase-gated on this; it makes an **empirically-confirmed silent leak** honest (`IsValidWeaviateProperty` drops leading-`_` overlay fields).
- Add `OverlayNamingRule` + `StorageNamingCapability.OverlayNaming`.
- Add `OverlayNameInjectivityVerifier`. **Phase-0 scope decision (critic MEDIUM):** down-scope the gate to **reserved-prefix preservation** — the post-transform target must stay in a reserved-by-convention namespace (`koan_`), so member-disjointness holds *by construction*; the full member-projection check defers until `ProjectionResolver` exists (OQ folded in). Don't claim §5 is "gated" on a check we can't build yet.
- Apply the rule **framework-side, before the adapter sees the stamped dict** (critic MEDIUM: the dict already carries `koan_*`, so `IsValidWeaviateProperty` passes), and to the read-filter spelling + Weaviate schema-property name from **one** path. Hoist `__` to a single framework constant.
- Weaviate declares `{ MarkerReplace: __ → koan_ }`; delete `IsValidWeaviateProperty` drop-on-write.
- *Tests:* FC-6 live-Weaviate Testcontainers round-trip; overlay-vs-overlay injectivity unit; **FC-5** Mongo/SQL byte-identical.
- *Risk:* index/schema-create is a third adapter-internal call site with no framework rename chokepoint today — larger than §8 implies; verify on the live adapter only.

### Phase 1a — Lift Provenance onto the descriptor
- Add `FieldProvenance`; add `Provenance` to `ManagedFieldDescriptor` (**not** Mode — critic LOW). `DataAxisExpander.Register` sets `Provenance = axis.OnDeleteValue is not null ? OperationSourced : AmbientStamped`.
- Interim: the two hand-registrars pass the correct provenance until they migrate (1c).
- Surface provenance in `DataAxisReport`/`DataAxisPreflight` (warn when an operation-sourced axis is read-scoped on a secondary store).
- *Tests:* expander stamps provenance for the soft-delete vs tenant shapes; boot report shows it; **FC-5** (additive, no behavior change).

### Phase 1b — Build the `AodbComposer` and collapse (the headline; HIGH blast radius)
- Add `Aodb`/`AodbElement`/`AodbComposer`; memoize the plan; emit provenance/mode-tagged elements + the cacheable bit (**isolation legs only**).
- Refactor `DataAxis.Explain` to consume the composer.
- Facade: render Aodb FieldFilter elements; collapse `ApplyManaged`/`ScopedById` + the 4 soft-delete loops into the store-aware push **honoring FC-1**.
- Vector: relocate `FoldReadScope` into the composer (**FC-3**); the decorator becomes a thin realizer pushing the materialised elements.
- `StorageKeyScoper` + `ScopedEntityCacheKey` consume the Aodb; `CachedRepository` ORs `(transform-exclusion || !aodb.Cacheable)` — **transform leg stays independent** (critic MEDIUM). Decide `IReadFilterContributor.ExcludesFromCache`'s fate (it covers the pure-predicate-axis-with-no-descriptor case the managed registry can't see).
- Delete `ManagedEqualityReadContributor`; fold partition in as a Particle source (**gated on the Couchbase regression**).
- Add `ContainerScoped` token + Conformance module.
- **Cover `BatchFacade.Delete` (FC-2).**
- *Tests:* composer unit (memoization, store-aware push); the full `ManagedFieldNoLeak`/`AssertNoLeak` TestKit green through the rebuilt path; **FC-1, FC-2, FC-3, FC-4, FC-5** all proven; byte-identical regression across every data adapter + vector + storage + cache.

### Phase 1c — Migrate tenancy + soft-delete onto `IDataAxis` (authoring shrink)
- `TenantAxis`/`SoftDeleteAxis` Declare bodies; remove the hand-registration (keep guard + posture).
- *Tests:* tenancy + soft-delete behavior identical (the byte-identical lever the `ArchivedAxis`/`RegionAxis` fixtures already prove); `AssertNoLeak` green; the `ClaimField` collision ledger still fires.
- *Risk:* tenancy is the flagship — migrate one axis at a time, full suite between.

### Phase 2 — Database/Moniker realization + per-tenant placement (the prize, second)
Entirely greenfield: `AxisMode.Database` registers only a carrier today (the expander's Database case is a no-op), so the Moniker element has **no existing realization path** — net-new adapter behavior bounded by the §6 posture.
- `ProvisioningPosture` + optional `.Provisioning(posture)` builder verb.
- Facade Database-mode branch: realize-or-fail-closed via `Create(sp, source)` + ensure-created; `external-only` fails closed when the keyspace is absent.
- `DatabaseScoped` token + Conformance module; broaden `InspectScopeAdapter` to all three modes.
- Optionally declare the Weaviate vector axis Container mode → native multi-tenancy.
- *Out of scope (sibling planes):* migration across existing tenant DBs, pool lifecycle, per-keyspace backup (P6 broker / P8 saga). Keep the AODB *realizing* the current mode, never *deciding* it.

---

## Open questions for the architect

1. **OQ-1 — explicit Aodb parameter vs implicit transport.** The ADR §160 names "an interface/contract change," but every value already has a transport (filter via translator, values via write-scope, moniker via `Create`). Is a new explicit `Aodb` method parameter worth the breaking change, or is implicit-transport-with-one-composer enough? *(Recommendation: implicit first; revisit only if a guarantee can't be made implicitly.)*
2. **OQ-2 — provenance completeness.** Is `.OnDelete present ⇒ operation-sourced` the whole rule, or can a field be **both** ambient-stamped *and* operation-overridden (a future moderation axis that stamps `__vis` *and* has an op that overrides it)? If so, provenance is per-write-site, not per-descriptor — and the single descriptor field is insufficient.
3. **OQ-3 — capability granularity.** Coarse Container/Database tokens (can-do-at-all) vs `FilterSupport`-style structured detail (which positions / which postures)? Coarse risks a no-capability-lie the Gate catches at runtime; detailed grows the surface.
4. **OQ-4 — partition-as-Particle ownership.** Synthetic `IDataAxis`, built-in Particle contributor, or stay special-cased and merely *viewed* as a Particle? Must resolve **before** 1b (the key scopers consume it) and pin the Couchbase `EncodePartitionInName=false` regression.
5. **OQ-5 — shared resolved-Aodb cache vs shared key shape.** §7 says "one tuple keys every plane's memo," but the data facade memoizes no resolved Aodb today. Is a new shared resolved-Aodb memo tier wanted, or does each plane keep its own memo keyed by the same tuple shape (no shared cache)?
6. **OQ-6 — external-only fail-closed timing.** Boot-probe every tenant keyspace (fail-fast, but needs the full tenant set the P6 broker owns, deferred) vs per-op first-touch throw? Phase-1 boot-fixed Database mode may only fail closed per-op.

---

## What this is not (scope discipline, ARCH-0102 §Non-goals)
Not a lifecycle engine (migration/pooling/backup are sibling planes), not the placement policy (the AODB realizes whatever mode is current), not a new contributor model (the sources are unchanged in *shape*; two gain declarative metadata fields).
