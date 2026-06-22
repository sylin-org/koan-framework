---
type: ARCHITECTURE
domain: core
title: "Redesign Completion Ledger — the grand autonomous run"
audience: [architects, ai-agents]
status: active
last_updated: 2026-06-22
---

# Redesign Completion Ledger

> **The durable orchestration plan + status ledger for the autonomous run** that drives the remaining Koan
> redesign — **Facet 3** (ambient + tenancy), **Facet 4**, and the cross-cutting capabilities
> (storage-composition pipeline, classification, Adapter Forge) — to **FULL implementation + exhaustive
> tests.** No time or effort limit; the objective is to come out the other side with every capability the
> ADRs describe **implemented and tested.** This ledger survives compaction — **update the status table as
> work proceeds.** It references, and does not restate, the per-ADR designs and the memory anchors.

## Mandate (architect, 2026-06-22)

Establish all necessary ADRs (capturing the scope gathered so far); **iterate over each facet**, doing
**side-discovery** as necessary to identify opportunities for *"fewer but more meaningful moving pieces"*;
then **implement** and write **exhaustive tests**. No limits on time or effort. Adjust ADRs and this plan
as discovery warrants.

## Governing canon (apply to every decision)

- **[[koan-design-principles]]** — conformity-by-design (structural > disciplinary) · aggressive/layered
  memoization · descriptor-not-callback seams · hot-path discipline.
- **[[koan-redesign]]** — fewer-but-more-meaningful parts · consolidation = deletion · dogfood-gated
  convergence (≥2 existing consumers) · green ratchet. Target map: [foundation-consolidation-plan.md](./foundation-consolidation-plan.md).
- **Method + boundaries** — [[koan-architect-working-style]] (verify empirically; re-derive agent/review
  findings against current source before coding) · [[no-stopgaps-full-implementation]] ·
  [[break-and-rebuild-preferred]] · [[koan-ergonomics-first-no-csharp-ceremony]] ·
  **[[persona-separation-no-gposingway]]** (never the user-platform names). Commit on `dev`, **don't push**.
  Newtonsoft canonical. **Ultracode is on → author Workflows for substantive phases.**

## Method — per facet / capability

1. **Side-discovery** — a convergence survey (Workflow): what bespoke parallel implementations could this
   facet collapse onto one meaningful primitive? (The storage pipeline collapsed partition/timestamps/
   identity; classification reuses it; the ambient carrier collapses 7 mechanisms.) Output: the convergence
   opportunities to fold into the ADR.
2. **ADR(s)** — establish or adjust, capturing scope; **adversarial review (Workflow)** before ratification
   (this caught real canon-errors twice on DATA-0105).
3. **Phased TDD** — RED → GREEN → **mutation-check**; **ARCH-0079 real-store integration spec** every step.
4. **Exhaustive tests** — isolation/no-leak proofs across adapters, **allocation benchmarks (0-bytes/op)**,
   the conformance kit, the full **green ratchet**.
5. **Persist** — memory + this ledger + commit.

## ADR inventory (the "necessary ADRs")

| ADR | Scope | Status |
|---|---|---|
| ARCH-0084 | Unified capability model (Facet 1) | ✅ Accepted |
| ARCH-0086 | `KoanModule` boot primitive (Facet 2) | ✅ Accepted |
| [ambient-context-charter](./ambient-context-charter.md) | Facet-3 ambient truth-test (11 Laws) | ⚠️ charter only — the full 7-mechanism unification is future; the **load-bearing typed-slice carrier is now ARCH-0097** |
| **ARCH-0097** | The axis-generic ambient carrier (typed slices) | ✅ **Authored (Proposed)** — `EntityContext.WithSlice`/`GetSlice`; cross-cutting axes (tenant, classification) ride as registered slices from their own modules, not named data-core fields. The decoupling prerequisite for `Koan.Tenancy`. |
| ARCH-0094 | The Adapter Forge | ✅ Accepted — **implement** (queued post-tenancy) |
| ARCH-0095 | Tenancy | ✅ Accepted — **§2 erratum filed** (4a = Route schema-qualifier, not a name particle; net-new Route machinery) |
| **ARCH-0096** | Identifier-composition primitive (anchor + ordered particles) | ✅ **Authored (Proposed)** — empirically-scoped: data+cache = the dogfood-2; vector already converged; jobs/blob = same-shape follow-ons; tenant = cache-key particle; `Koan.Core.Naming` home (layering verified acyclic) |
| **DATA-0105** | Storage-composition contributor pipeline | ✅ **Finalized (Proposed, revised ×2)** — consumes ARCH-0096; descriptor-not-callback; **6 stages (Key dropped)**; 3 memo planes; sync applicators; must-fixes i–xi + upgrades A–D folded; 3-lens spot-check ship-ready |
| **ARCH-0098** | The data-classification axis (`[Pii]`/`[Phi]`/`[Pci]`/`[Secret]`, layered policy, field-transform + crypto) | ✅ **Accepted (review-amended; phase 1 LANDED `cc486781`)** — side-discovery `wf_c4cb6674-564` + adversarial review `wf_6a0f0278-5b5` (RATIFIABLE-WITH-AMENDMENTS, 9 verified HIGH+ findings folded): a **POCO-property value-transform** (write-stamp **clone-then-encrypt** at the adapter-agnostic chokepoint — covering batch `AppliesInBatch` + `ConditionalReplaceAsync` — + a **net-new read-reverse** below `Data.QueryWithCount`) — NOT a serialize hook; a **sibling** `ClassifiedFieldRegistry`/`IFieldTransform` (round-trip, property-keyed, two-gate off-model) distinct from the managed-field registry (inject, one-way); greenfield crypto on the `IIssuerKeyStore` *shape* + §3a contract (retiring-key retention · owning-tenant decrypt · count-aware nonce budget · KMS-not-DP master-wrap · tenant-local blind-index → erasure cert); cache=L2-exclusion phase-3 gate; messaging carrier=named follow-on. **Phase 1 (facts foundation) done; phase 0 (priority field + open `StorageWritePlan`) next.** |
| **ARCH-0099** | Tenancy realignment (posture · owner-admin · mandatory mode ladder · tenant-as-native-container · positional particles) | ✅ **Accepted (2026-06-22)** — architect realignment, prior-art-grounded (`wf_94dbd26f-e67`) + audit (`wf_c789391c-72e`). Supersedes: ARCH-0095 (posture=**dev-open/prod-closed via `KoanEnv`** not Mode=Off; owner-admin disentangled — backdoor dead, **first-owner-of-own-tenant** built; mode ladder **mandatory**), ARCH-0096 §5 (tenant **IS** a leading name particle), DATA-0105 §1 (tenant isolation = **per-adapter native container**, not relational-schema-only), reconciling DATA-0094 (the general, Accepted model). **Positional-particle primitive LANDED `a46c11fc`** (`Particle.Position` + per-particle separator; 19/19, data-naming 40/40 byte-identical, mutation-killed). NEXT = posture seam + `IParticleContributor` discovery seam → wire `Koan.Tenancy` to register the leading tenant-container particle. |
| **Facet 4** | (undefined here) | ☐ **TO SCOPE** via side-discovery against [foundation-consolidation-plan.md](./foundation-consolidation-plan.md) → ADR(s) |

## The sequence (dependency-ordered; refine via side-discovery)

1. **Foundational ADRs** — author **ARCH-0096** + final-revise **DATA-0105** + the **ARCH-0095 §2 erratum**.
2. **Storage pipeline impl** — **phase 0a** standalone memo/determinism fixes (`ProjectionResolver`/
   `IndexMetadata`[+determinism @:35 & :41]/`AdapterNaming.GetOrCompute`/base-name→(Type,adapter)) → the
   pipeline model (descriptor base · 3-plane cache · structural closure via typed-slice · deterministic
   ordering · 0-alloc benchmark · `IsInvariantOnly` fast path · off=structurally-absent) → convergence
   (write-stamp; name-particle re-homing partition onto ARCH-0096, **data + cache first**; schema-column).
3. **Tenancy = registration** on the pipeline — the 8 primitives (P8 internal), kernel, control-plane keyed
   entities, membership-gate-above-roles, lifecycle sagas, **erasure certificate** — + **exhaustive tests**
   (`AssertNoTenantLeak` across adapters, SQLite-first no-Docker proof; two-shaped read-filter incl.
   get-by-id; durable-carrier stamping).
4. **Classification axis** — side-discovery → ADR → impl (the layered policy; encrypt/tokenize/mask as
   Serialize contributors; the serialization/record hook for bare-entity stores; the 2nd pipeline consumer
   that retro-justifies the read-filter/serialize seams).
5. **Ambient unification** — the charter → ADR → collapse the 7 ambient mechanisms onto one carrier (promote
   `EntityContext`; re-home AI scopes + cache behavior; delete `CacheScope`/`_override`; typed slices).
   *Sequencing open:* tenancy already rode `EntityContext`; re-derive whether this lands before, with, or
   after the pipeline via side-discovery (it is Facet 3's core, tenancy is its flagship slice).
6. **Adapter Forge** (ARCH-0094) — impl; the **Conformance Gate = the pipeline's structural check + the
   tenancy P7 isolation kit**; agent-authored conformance-gated adapters.
7. **Facet 4** — scope via side-discovery → ADR(s) → impl + tests.
8. **Cross-cutting completion** — observability/measured-cost (tenancy §11), the durable-carrier schema
   (messaging outbox `TenantId`, DLQ classified-stripping), the read-guard collapse (tenant filter +
   SEC-0004 `Constrain` + WEB-0068 into one ordered chain), the compliance-posture boot report.

## Side-discovery checkpoints (the convergence harvest)

At each facet boundary, before the ADR, survey for "fewer but more meaningful parts": the latent twins the
storage pipeline already revealed (the 4 identifier-composing surfaces; the 3 read-guard mechanisms; the
durable-carrier stampers; the per-entity manifest serving hot-path + boot-report + Forge fingerprint). Each
opportunity that clears the dogfood gate (≥2 existing consumers) folds into the facet's ADR as a deletion.

## Test objective (exhaustive)

Every capability: an ARCH-0079 real-store integration spec + a mutation-check; the isolation/no-leak proofs
across **every** adapter (not a sample); the 0-bytes-per-op allocation benchmarks; the capability-honesty +
conformance kit; the green ratchet stays green throughout. "Done" = the capability the ADR describes is
implemented and its tests pass on real stores.

## Status ledger (running — update as work proceeds)

- ✅ **Tenancy design** — ARCH-0095 (3 external review rounds, unanimous ship). Slices **1a** (ambient
  `Tenant` carrier) + **1b** (fail-closed chokepoint gate) committed (TDD+mutation, data-core suite 174/174).
- ✅ **Pivot** to the storage-composition contributor pipeline; exhaustive inventory + memoization survey.
- ✅ **DATA-0105** drafted + **two** full adversarial review rounds; **cross-pillar decision ratified** (full
  cross-pillar axis-composition primitive). **Design principles → [[koan-design-principles]].**
- ✅ **Foundational ADRs DONE** — empirical survey (6 Explores) re-grounded the cross-pillar scope (vector
  already converged onto `StorageNameGenerator`; the real dogfood-2 is data-name + cache-key; a 5th instance
  `JobTypeBinding.CoalesceKey` + blob-binding are same-shape follow-ons; tenant is a cache-key particle, not a
  name particle). **ARCH-0096** authored (identifier-composition primitive, `Koan.Core.Naming`, descriptor
  model, 3-plane memo, layering verified acyclic). **DATA-0105** finalized (6 stages, Key dropped, descriptor
  model, all must-fixes i–xi + upgrades A–D). **ARCH-0095 §2 erratum** filed. 3-lens spot-check (coherence /
  layering / adversarial-factual) = unanimous ship-ready, 0 blocking.
- ✅ **Phase 0a DONE** — the 4 extracted standalone memo/determinism fixes, each TDD'd green-ratchet:
  `IndexMetadata` determinism (`:35` Guid group-key → attribute-position; `:41` Dict iteration → explicit
  insertion order) + Type-cache (`11e4439f`); `ProjectionResolver` Type-cache (`f2638aa0`);
  `AdapterNaming.GetOrCompute` per-`ServiceProvider` factory-lookup cache via `ConditionalWeakTable`
  (`81f2f2e1`); base-anchor split off partition in `StorageNameGenerator` (`28b30a42`). Data-core suite
  174 → **190/190** (16 new specs). Each behaviour-preserving except the determinism fix (a correctness win).
- ✅ **Phase 0b ARCH-0096 engine DONE** (`309e8482`) — `IdentifierComposer` + `Particle`/`CompositionPolicy`
  readonly structs + `IParticleFormatter` in `Koan.Core.Naming`: deterministic order, per-particle omission,
  byte-limit clamp (mirrors `NamingUtils` byte-for-byte), allocation-free fast paths (13 specs, 56/56 core unit).
  The descriptor-*discovery* seam (`IParticleContributor`/`[KoanDiscoverable]`) is deferred to its second
  cross-cutting consumer (tenant, phase 4) per dogfood discipline.
- ✅ **Phase 2 name-particle DONE** (`66c9470d`) — `StorageNameGenerator` composition delegates to
  `IdentifierComposer`; partition is the first particle; `PartitionParticleFormatter` bridges the adapter token
  policy. Deleted the hardcoded concat + local clamp. **Byte-identical (data-core 190/190); vector untouched.**
  The ARCH-0096 engine is now proven against the real DATA-0104 grammar + adapter capabilities.
- **Sequencing decision (2026-06-22):** the DATA-0105 pipeline's other stages (write-stamp/schema/read-filter)
  are **independent of ARCH-0096**, and the **tenant no-leak proof flows through those, not identifier
  composition** (tenant ≠ name particle; tenant-in-cache-key is a phase-4 coherence concern via the cache
  template engine — a different shape, bundled there). So **cache-key delegation is deferred to phase 4** and the
  critical path is the `IStorageContributor` pipeline.
- ✅ **Phase 1 write-stamp re-home DONE** (`6df31235`) — the Write-stamp stage is now a per-type memoized
  `StorageWritePlan` (the seam tenant registers on in phase 4): `IWriteStamp` + `IdentityWriteStamp` +
  `TimestampWriteStamp` (reuses `TimestampPropertyBag`). `RepositoryFacade` applies the plan; deleted the inline
  `EnsureIdAsync`/`UpdateTimestamp` calls + the facade's manager dependency (both ctor sites updated). The
  `BatchFacade` no-timestamp invariant is now **structural** (`TimestampWriteStamp.AppliesInBatch = false`).
  id-gen unified into `AggregateIdentity.Ensure` (shared with the transaction-path manager — can't drift).
  Behaviour-identical: 7 white-box specs (incl. the batch invariant); data-core **197/197**.
  *Empirical note:* write-stamp + read-filter are **chokepoint** concerns (one place); the genuinely
  **per-adapter** value is the **schema-column** seam (phase 3) — where the tenant discriminator joins DDL.
- ⚠️ **ARCHITECT CORRECTION (2026-06-22): tenancy must be a separate `Koan.Tenancy` module.** Slices 1a/1b put
  tenancy code *in* `Koan.Data.Core`; I then spread a reference into `AggregateConfig` (reverted, `a9ba7417`).
  Decision: **full extraction now** — the data core exposes generic tenancy-agnostic seams (the contributor
  pattern); `Koan.Tenancy` provides the contributors + owns the developer surface (`.WithTenant` etc.).
  **Docs realigned to canon** (this commit): **ARCH-0097** (axis-generic carrier) authored; **DATA-0105 §0**
  defines the contributor pattern + where contributors live (modules); **ARCH-0095** reframes the kernel as the
  `Koan.Tenancy` module + the `IStorageGuard`/contributor seam; tenancy-design.md packaging callout.
- ◐ **`Koan.Tenancy` extraction IN PROGRESS** (incremental, green each step):
  - ✅ **A** (`1c9cfa23`) axis-generic carrier — `EntityContext.WithSlice`/`GetSlice` + slice map; removed the
    named `Tenant` field/param + the data-core `using …Tenancy`; `Tenant.cs` on the slice API; 7 specs; 204.
  - ✅ **B** (`773c03a5`) generic `IStorageGuard` seam — facade resolves `IStorageGuard[]` (no-op if none);
    `TenantEnforcer→IStorageGuard`; deleted `ITenantEnforcer`; both ctor sites resolve the seam; 204.
  - **CARRIER + FACADE NOW TENANCY-AGNOSTIC (code).** Remaining coupling = exactly **7 files to move** +
    **1 registration to relocate** (the rest are agnostic doc comments).
  - ✅ **C+D+E DONE** (`9c718b53`) — created `src/Koan.Tenancy` (in `Koan.sln`); moved the 7 files
    (ns → `Koan.Tenancy`, `TenantEnforcer`→`TenantStorageGuard`); added the **`.WithTenant`** surface;
    `KoanAutoRegistrar` binds options + registers the guard (Reference = Intent); removed the data-core
    registration. New `tests/Suites/Tenancy/Koan.Tenancy.Tests` (specs moved + `TenancyRuntimeFixture`).
    **Tenancy proven through a real `AddKoan()` boot with the module referenced — registrar discovered, gate
    wired via the generic seam: 17/17. Data-core tenancy-free: 188/188. INVARIANT HOLDS: 0 non-comment tenant
    lines in `Koan.Data.Core`.**
- ✅ **`Koan.Tenancy` EXTRACTION COMPLETE.** The data core owns generic seams; tenancy is a separate module
  providing contributors under Reference = Intent — the contributor pattern (DATA-0105 §0) is now real.
- ✅ **MANAGED-FIELD DESIGN + ADVERSARIAL REVIEW DONE (2026-06-22).** Empirical re-derivation (5 Explores)
  **falsified the ratified "sibling column" premise** — relational adapters persist **only `(Id, Json)`**
  (`SqliteRepository.cs:822`); a non-POCO discriminator rides neither a (unpopulated) sibling column nor the
  JSON without a hook, and `FieldPathResolver` throws on synthetic fields. Design memo
  [tenancy-managed-field-design.md](./tenancy-managed-field-design.md): the **managed-field seam** — an
  invisible field injected into the persisted record via a **Serialize-stage `ContractResolver`** hook,
  filtered by making the **shared `FieldPathResolver` managed-aware** (one change reaches translator +
  pushability splitter), with an optional indexed **computed column** (Schema stage). **4-lens adversarial
  review (wf_d547d3d1-0fe) = ship-after-blocking-fixes; ALL 6 blockers folded:** (1) chokepoint surface
  incomplete → enumerate EVERY write/delete member (RemoveAll/DeleteAll/ConditionalReplace scoped-or-fail-closed);
  (2) cache decorator wraps OUTSIDE the facade with a tenant-blind key → the managed axis enters `CacheKey.For`
  (phase 3d); (3) vector `VectorService` bypasses the facade → fail-closed v1; (4) raw/Direct uncovered →
  out-of-scope + **RLS named backstop** (lands with the capability); (5) write **stamp-AND-verify** not deferred
  → conflict-aware upsert rejects cross-tenant id-keyed takeover; (6) residual managed predicate silently empty
  → structural fail-closed gate (predicate MUST be Pushable; `RequiredCapability` requires isolation-cap +
  `IQueryRepository` + Eq-pushability). **Errata filed:** DATA-0105 §1/§4 + tenancy-design.md §12 + ARCH-0095 §5
  (the "facade is the universal gateway" overstatement corrected; enforcement spans planes). Capability token =
  axis-free **`DataCaps.Isolation.RowScoped`**.
- ✅ **Phase 3a DONE** (`677004bb`) — converged column-name/exclusion onto `ProjectionResolver`
  (`ColumnNameOf`/`IsExcludedFromStorage`, honoring `[Column]`+`[StorageName]` / `[NotMapped]`+`[IgnoreStorage]`);
  **deleted the dead `src/Koan.Data.Relational/Schema/` cluster** (11 files, zero callers). Byte-identical; 192.
- ✅ **Phase 3b DONE** (`8fb23fe8`) — the managed-field seam: `ManagedFieldRegistry`/`ManagedFieldDescriptor`/
  `ManagedFieldWriteScope` + `DataCaps.Isolation.RowScoped` (Abstractions) · `ResolvedField.IsManaged` +
  managed-aware `FieldPathResolver` (one change reaches translator + splitter; fail-loud `GetValue`) ·
  `ManagedFieldContractResolver` Serialize-stage injection wired into the relational trio's
  `ComparableScalarEncoding.Apply` · `RepositoryFacade` full chokepoint surface (read predicate, key-op→query
  IDOR, RemoveAll/DeleteAll scoped, ConditionalReplace/raw fail-closed, capability gate) · SQLite conflict-aware
  upsert (write-verify). Proven on real SQLite with a **generic non-tenant descriptor**, MUTATION-CHECKED (drop
  read-predicate ⇒ isolation RED; drop conflict-guard ⇒ takeover RED). 204 + 5.
- ✅ **Phase 3d DONE** (`59af99ac`) — the managed scope partitions the cache key (`CachedRepository.TryBuildEntityKey`
  appends every registered managed field's value), closing blocker #2 (the cache wraps OUTSIDE the facade).
- ✅ **PHASE 4 — THE TENANCY CAPABILITY DELIVERED + PROVEN** (`38ef15fe` core, `b5a57491` vector). `Koan.Tenancy`
  registers the `__koan_tenant` `ManagedFieldDescriptor` (value=`Tenant.Current?.Id`, applies !`[HostScoped]`,
  requires `Isolation.RowScoped`) — tenancy is now PURE REGISTRATION; the data core stays tenancy-agnostic. The
  capability gate is use-time (ctor-bool + throw only when a managed value is in scope) so Off/unscoped is a true
  no-op. Vector plane fails closed (`VectorData.Search`). **Flagship `AssertNoTenantLeak` on real SQLite (no-Docker,
  Enforce), 6 cases ALL green + MUTATION-CHECKED** (cache-key neuter reopens the [Cacheable] leak): read isolation ·
  get-by-id IDOR · cross-tenant upsert **takeover REJECTED** · scoped RemoveAll · `[HostScoped]` exempt · `[Cacheable]`
  cache-key partition · raw fail-closed · non-isolating-adapter (JSON) fail-closed · tenancy-OFF zero regression.
  Tenancy suite 23/23. **All 6 adversarial-review blockers closed.**
- ✅ **PHASE 4 RELATIONAL FAN-OUT DONE** (`29d39e37`, Docker-verified) — the managed-field write-verify extended to
  **Postgres + SqlServer** (read pushdown + Serialize injection already worked via the shared resolver). Both
  announce `Isolation.RowScoped`: PG `ON CONFLICT … DO UPDATE … WHERE (<table>."Json" #>> '{field}') = @scope`
  (table-qualified — the Docker run caught a real `Json is ambiguous` bug); SqlServer `MERGE … WHEN MATCHED AND
  JSON_VALUE = @scope` (0 rows ⇒ reject). Shared generic oracle **`ManagedFieldNoLeak`** (AdapterSurface TestKit,
  non-tenant) run per adapter: **Postgres 1/1 · SqlServer 1/1 on real containers** (isolation · IDOR · cross-scope
  upsert-takeover REJECTED · scoped RemoveAll). **The relational trio (SQLite+PG+SqlServer) all enforce isolation.**
- ✅ **MONGO ISOLATION DONE** (`0720a729`, Docker-verified) — the **first bare-store / non-Newtonsoft** realization.
  Read path needed NO change (MongoFilterTranslator already rides the managed-aware `FieldPathResolver`;
  `IgnoreExtraElements` drops the injected element on read). Write: inject the managed BSON element into
  `model.ToBsonDocument()` + `ReplaceOne` via a `BsonDocument` view with a conflict-aware filter `{_id, <managed>}`
  ⇒ foreign doc ⇒ insert-same-_id ⇒ **E11000 ⇒ reject**. Announces `RowScoped`. Mongo no-leak 1/1 + full suite
  21/21 (unscoped path byte-identical). **Tenant isolation now spans relational + document stores.**
- ✅ **ADAPTER-COMPATIBILITY SWEEP DONE** — full `Koan.sln` build clean (all 15 adapters); no-regression suites
  green post-change: SQLite 5 · PG 9 · SqlServer 19 · Mongo 21 · InMemory(data) 33 · Json 7 · Redis 3 ·
  InMemory(vector) 29 · Qdrant 35 · data-core 206 · cache 109 · tenancy 23. **The non-isolating adapters
  (Couchbase/Redis/Json/InMemory + all vector) FAIL CLOSED for a tenant-scoped entity — secure, never leaky.**
- ◐ **CLASSIFICATION (ARCH-0098) — phases 0 + 1 + 2a + 2b-1 + 3 DONE (encrypt-at-rest works end-to-end); searchable + prod-key-tier + leak-guards remain.** ADR reviewed (`wf_6a0f0278-5b5`) + amended:
  - ✅ **Phase 1 (facts)** `cc486781` — `[Classified]`/`[Pii]`/`[Phi]`/`[Pci]`/`[Secret]` + `ClassificationCategory` +
    `ClassifiedFieldDescriptor` (facts-only, no Kind) + `ClassifiedPropertyBag` + `ClassifiedFieldRegistry`
    (two-gate off-model), no crypto; 16/16 + 2 mutations killed.
  - ✅ **Phase 0 (open the slot)** `3e80e010` — `IWriteStamp` made public + `int Priority` (identity 0 / `[Timestamp]`
    100); `WriteStampContributor` + `StorageWriteContributorRegistry` (off-gated, idempotent); `StorageWritePlan.Build`
    stable-priority merge + memo-invalidation; `ManagedFieldDescriptor.Priority` + ordered `ManagedFieldRegistry`.
    Behavior-preserving: data-core 233/233, tenancy 23/23, 2 mutations killed. The "one opening, two consumers" slot
    is open. The field-transform is **write-stamp clone-then-encrypt + net-new read-reverse** (NOT a serialize hook).
  - ◐ **Phase 2 (crypto seam)** — new `Koan.Classification` module (added to `Koan.sln`); crypto CORE done:
    - ✅ **2a (cipher)** `62a8bdac` — `FieldDataKey` + `FieldCipherEnvelope` (magic-prefixed, bounds-safe `TryParse`
      so the read-reverse tells ciphertext from legacy plaintext) + `IFieldCipher`/`AesGcmFieldCipher` (AES-256-GCM,
      fresh nonce, fails closed) + `FieldDecryptionException`. 25 specs, 2 mutations killed (fail-open, static nonce).
    - ✅ **2b-1 (key provider, dev tier)** `ada40eb1` — `IKeyProvider` (GetActiveKey/GetForDecrypt-by-owning-tenant/
      DestroyKeyAsync; host bucket so classification ⊥ tenancy) + `KeyUnavailableException` + `EphemeralKeyProvider`
      (per-tenant, count-aware rotation w/ retired-key survival, ZeroMemory crypto-shred, tenant-isolated + idempotent).
      12 specs, shred mutation killed.
    - ☐ **2b-2 (persisted prod tier)** — `Entity<TenantDataKeyRecord>` + `IDataProtector` dev-wrap / KMS-prod seam +
      fail-closed boot guard (§3a). DEFERRABLE (ephemeral suffices for dev/test). · ☐ **2c (IBlindIndex)** — keyed-HMAC,
      tenant-local, FixedTimeEquals; build in phase 4 where searchable uses it. · ✅ **2d (request-scoped plaintext map)**
      — turned out UNNECESSARY as a separate component: the read-reverse sets plaintext directly back on the POCO
      property (the caller's entity is the plaintext carrier), and the cache L2-exclusion handles the distributed-cache
      leak. No AsyncLocal map needed.
  - ✅ **Phase 3 (THE integration, the ADR's #1 risk) DONE** — a generic round-trip seam, classification wired onto it,
    exhaustively proven on a real `AddKoan()` + SQLite boot (ARCH-0079). The clone-then-encrypt is NOT an in-place
    `IWriteStamp` (in-place corrupts the caller), so it became a new generic seam: `IFieldTransform`
    (ApplyOnWrite/ApplyOnRead) + `FieldTransformContributor` + `StorageFieldTransformRegistry` (off-gated) +
    `StorageFieldTransformPlan` (per-type memo, `CloneForWrite`) + `EntityCloner` (MemberwiseClone open-delegate). The
    facade: **clone-then-encrypt** on Upsert/UpsertMany/batch/`ConditionalReplaceAsync` (caller keeps plaintext+id+ts);
    **read-reverse in place** on Get/GetMany/Query/QueryRaw (every entity-returning path; Count + delete-helpers
    correctly excluded). `CachedRepository` excludes field-transform types from L2 (generic `HasTransformsFor` gate —
    no plaintext cached). `Koan.Classification`: `ClassificationFieldTransform` (string-only, legacy-plaintext-tolerant,
    idempotent, shred→null tombstone; resolves crypto from the running host per-op so multi-host tests are correct) +
    `IClassificationTenantAccessor` (null=host bucket) + a `KoanModule` registrar (Register DI + Start Activates +
    registers the contributor). **Tests: 9 seam + 46 crypto/transform unit + 8 real-store integration** (round-trip ·
    caller-keeps-plaintext · at-rest ciphertext [raw SQLite read AND crypto-shred→null] · every read path decrypts ·
    every write path incl. batch encrypts at rest [Blocker 1] · `[Cacheable]` no-stale-plaintext-after-shred [Blocker 3]
    · unclassified untouched). **3 security mutations killed** (no-read-reverse, no-encrypt-on-write, cache-exclusion-off).
    **Zero regression**: data-core 233 · tenancy 23 · sqlite-cache 2 · sqlite-connector 5 (off-gate byte-identical).
    **Adapter fan-out DONE** (`a8273ebf`): a generic `FieldTransformRoundTrip` oracle (AdapterSurface TestKit, flag-gated
    reversible wrap — observes at-rest without a per-adapter raw read) proves the round-trip on **Mongo (bare-store/BSON)
    · Postgres (jsonb) · SqlServer ([Json])** via Docker — with SQLite that is **all four store families**. The transform
    runs ABOVE the adapter, so adapter-universality (the §4 capability asymmetry — base path needs no adapter token) is
    empirical, not asserted.
    Note: the phase-0 `IWriteStamp` contributor seam stays a generic in-place-stamp extension point (classification uses
    the new field-transform seam); the priority field was the load-bearing phase-0 deliverable.
- ◐ **TENANCY ARCH-0099 BUILD — posture seam (step 1) IN PROGRESS.** ADR §6 (the **Tenancy Dev Console**, a
  dev-only TestProvider-styled control-plane page in a new `Koan.Web.Tenancy` connector) folded in + the build
  order **reordered** so the durable control-plane + admin endpoints + console precede the particle/container
  wiring (1 posture-seam → 2 durable control-plane + admin endpoints → 3 dev console → 4 particle → 5 P6 → 6
  native-container). Now building step 1: `TenancyPosture` (Open/Closed from `KoanEnv`, kills the `Mode=Off`
  default) + migrate `Koan.Tenancy` to `KoanModule` (DI-available `Start`) + gate-reads-posture + prod-boot
  pre-flight (HARD-FAIL active+no-resolver/branded-marker/no-secret; WARN census) + dev auto-seed (smart-named
  dev tenant + loopback Owner + dev-fallback resolution + branded ephemeral key) + Redis-style refusal diagnostic.
- ☐ **THEN:** Phase 3c schema-column DDL indexability (Indexed descriptors → computed/expression index; PG/SqlServer;
  SQLite JSON-only) + Mongo/bare-store managed serialization injection + in-memory managed `GetValue` · classification
  phases 4–7 (searchable blind-index · vector/messaging leak guards · crypto-shred+rotation · masked-read) · then
  control-plane keyed entities / state machine / sagas / erasure cert · ambient unification · Adapter Forge · Facet 4.

> Full per-area detail + the DATA-0105 review punch-list (must-fixes i–xi, upgrades A–D, opportunities):
> memory **[[facet3-tenancy-design]]** (the anchor). Tenancy spec: [tenancy-design.md](./tenancy-design.md).
