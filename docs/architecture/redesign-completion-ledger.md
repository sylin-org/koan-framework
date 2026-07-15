---
type: ARCHITECTURE
domain: core
title: "Redesign Completion Ledger — the grand autonomous run"
audience: [architects, ai-agents]
status: active
last_updated: 2026-06-22
---

# Redesign Completion Ledger

> **Historical ledger (superseded 2026-07-15):** This file records the earlier autonomous redesign run;
> live initiative status now belongs to [`koan-v1/PROGRESS.md`](../initiatives/koan-v1/PROGRESS.md).
> [ARCH-0113](../decisions/ARCH-0113-entity-capability-communication.md) moved typed logical-flow context
> and durable carriage from Data to `Koan.Core.Context`, removed `EntityContext.GetSlice`/`WithSlice`
> and Data-axis `.Carries(...)`, and replaced the old carrier type names. Entries below remain
> historical evidence, not current API guidance.

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
  **[[persona-separation-private-downstream]]** (never private downstream names or identifying details). Commit on `dev`, **don't push**.
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
- ✅ **TENANCY ARCH-0099 BUILD — posture seam (step 1) DELIVERED.** ADR §6 (the **Tenancy Dev Console**, a
  dev-only TestProvider-styled control-plane page in a new `Koan.Web.Tenancy` connector) folded in (`d4de06d4`)
  + the build order **reordered** so the durable control-plane + admin endpoints + console precede the
  particle/container wiring (1 posture-seam → 2 durable control-plane + admin endpoints → 3 dev console → 4
  particle → 5 P6 → 6 native-container). Posture seam built in 4 green+mutation-verified slices:
  - **1a** (`385d0ee5`) — `TenancyPosture {Closed=0,Open=1}` (fail-safe default) + pure `TenancyPostureResolver`
    (KoanEnv.IsDevelopment + override) + `TenancyRuntime` (resolve-once) + gate reads posture + **retire
    `TenancyMode.Off`** + migrate `Koan.Tenancy` → `KoanModule`. 2 mutations killed.
  - **1b** (`18637c96`) — `ITenantResolver` seam + `TenancyDevBrand` (`koan-dev-insecure-`) + pure
    `TenancyPreflight` (Production hard-fail: forced-Open / no-resolver / branded-artifact; soft-warn else) +
    `TenancyBootException` + `Start` runs it (throws → aborts boot). Prod signal = per-host `IHostEnvironment`
    (testable, dodges the KoanEnv latch). 2 mutations killed.
  - **1c** (`c0b50c81`) — dev auto-seed under Open: `TenancyDevSeed` (smart-name `leo@acme.dev`→`Acme`, Owner
    `koan:owner`, branded per-machine key) + `TenancyDevState` (in-memory singleton) + `TenancyAmbient` (one
    resolution point; unset slice → dev fallback via AppHost.Current). 2 mutations killed.
  - **1d** (`d526f239`) — `TenancyRefusal` Redis-protected-mode-quality diagnostic (names entity + 3 exact
    remediations + dev-open reminder).
  - **Adversarial review** (`wf_baf637e1-c5b`, 5 lenses → verify) confirmed **7 findings** (2 HIGH security, 3
    ADR-conformance, 2 test-adequacy), all one root cause: the pre-flight reconstructed the override from the
    config string + gated on `IsProduction`, while the operative posture (which the gate/seed honor) was the
    resolved value → blind to the fail-open signal. Three holes: Staging+override, KoanEnv process-latch (Prod
    inherits a Dev snapshot), programmatic `Configure<TenancyOptions>`. **FIXED (`7edca2e2`):** posture now
    derives from the **per-host `IHostEnvironment`** (not the latched `KoanEnv`); the pre-flight is **authoritative
    over the resolved posture** with the invariant **"Open is legal only in Development"** (refuses a resolved Open
    in any non-Dev env — closes all three holes in one check); branded-artifact hard-fail extended to all non-Dev;
    seed gated on `env.IsDevelopment()` too; Report over-claim + census comment corrected; ARCH-0099 §1 refinement
    note added. Dev-open is now exercised by booting a **Development** host (not forcing Open in Test).
  - **Koan.Tenancy 62/62** (real `AddKoan` boots, ARCH-0079); **12 mutations killed total** (incl. the invariant
    flip → 11 fail, per-host dev-flag → false → 5 fail); blast radius contained (no external project references
    the module). All 7 review findings addressed. Posture seam (ARCH-0099 build-order step 1) COMPLETE.
  - **ADR §1 realigned** (`3d337583`) — the per-host posture correction promoted into the canonical decision text
    (not an erratum).
- ◐ **TENANCY ARCH-0099 BUILD — step 2 (durable control-plane) IN PROGRESS.** Data-layer half DONE:
  - **2a** (`06bbb16e`) — the durable registry entities, dogfooded `[HostScoped]` `Entity<T>`: `TenantRecord`
    (immutable GUID-v7 id + mutable `Name` + `TenantStatus` + `[Timestamp]`), `Membership` (the identity↔tenant
    bridge; **roles ON the membership**, StackExchange model; `IsOwner`/`HasRole`), `Invite` (opaque token +
    expiry; `IsRedeemable` false once revoked/expired). Naming: the static `Tenant` keeps the ambient API, so the
    row is `TenantRecord`. 65/65; 1 mutation killed.
  - **2b** (`c5ebb77b`) — first-owner onboarding: pure `TenantBootstrapPolicy` (dev → anyone; prod → allowlist OR
    constant-time one-time token) + durable `TenantBootstrap` (one-shot `ClaimOwnerAsync` — first claimant is the
    only Owner, later claims ignored; `EnsureDevAsync` graduates the in-memory dev seed onto durable rows,
    idempotent). Durable seeding stays **lazy** (not at module `Start` — a multi-host test can leave a stale
    `AppHost.Current`). 72/72; 2 mutations killed.
- ◐ **PORTAL + CONFIG-PLANE DESIGN CAPTURED (architect, 2026-06-22); build is the WEB half (NEXT).**
  - **ARCH-0099 §6 reshaped** (`af9b624e`) — the web surface is **`Koan.Tenancy.Web`** (tenancy family, keeps
    `Koan.Tenancy` web-free), **one project hosting the self-service SITE + the admin APIs**, **posture-governed**
    (dev auto-opens, loopback = seeded Owner; **prod serves the same surface Owner-gated** — caller identity →
    `Membership` → `koan:owner`; first claim via the bootstrap allowlist/token). A real shippable portal, not
    dev-only. The site is a projection over the APIs (dogfood). (Renames the earlier `Koan.Web.Tenancy`.)
  - **ARCH-0099 §7 added** (`29a8b100`) — **the tenant configuration plane**: §7a the governed-config primitive
    (app declares typed key + default + **mutability lock**; layered resolution; locked overrides degrade
    honestly — generalizes the ARCH-0098 classification lock); §7b framework-axis configs riding the same
    governance with ENFORCED invariants (captured domains → `ITenantResolver`, DNS-TXT verified · registration
    posture open/invite/domain → invite flow · auth providers → `Koan.Web.Auth` · classification posture →
    ARCH-0098); §7c the governance lattice. Config-as-declaration discipline.
  - **▶ NEXT (the WEB build) — 2c-1 scaffold `Koan.Tenancy.Web`:** csproj (`Microsoft.NET.Sdk`, refs
    `Koan.Tenancy` + `Koan.Web` + `Koan.Core`; ASP.NET via Koan.Web transitive; `wwwroot/**` Content) + add to
    `Koan.sln` · `KoanModule` registrar `[After(Koan.Web's registrar)]` (so `UseRouting` is in the pipeline) →
    `AddKoanOptions<TenancyWebOptions>` (RouteBase `/.tenancy`) + register an `IStartupFilter` that
    `UseEndpoints(MapGet/MapPost …)` · `GET /.tenancy/api/diagnostics` (posture from `TenancyRuntime` + dev state
    from `TenancyDevState`) · lazy `TenantBootstrap.EnsureDevAsync` on first dev request. **Mirror
    `src/Connectors/Web/Auth/Test/`** (csproj + `Initialization/KoanAutoRegistrar` [`AddKoanControllersFrom` or
    minimal endpoints] + `Hosting/*StartupFilter` + `wwwroot/`). Then **2c-2** (caller-identity resolver + Owner
    authz gate + read endpoints), **2c-3** (mutation endpoints w/ authz), **step 3** (the site page, model
    `src/Connectors/Web/Auth/Test/wwwroot/testprovider-login.html`). Web tests = TestServer/WAF (ARCH-0091).
  - **PRIOR-ART SWEEP DONE** (`wf_25fb6f0c-992`, 29 frameworks → `docs/architecture/tenancy-prior-art-findings.md`).
    Verdict: §6/§7 validated (plane split = AWS/Nile/Neon canon; role-on-membership = unanimous B2B; fail-closed =
    "most defensible in the field"); **§7's lockable per-tenant config primitive is genuinely novel** (nobody ships
    it). Keystone: **Windows Group Policy** = the 1:1 ancestor of the lock + a checklist of 3 gaps. **§7 RESHAPED to
    Group-Policy-grade** (`7e6ac91e`, architect-chosen "reshape core + roadmap rest"): tri-state override · the lock
    carries its bound (`TenantMayChangeWithin(envelope)`, out-of-bounds unrepresentable) · clean-revert + RSoP
    effective-value explainer · framework-axis invariants as a separate higher PLANE (Intune) · explicit-data
    precedence. §7d roadmap = graduated-enforcement · structural plane-split enforcement · durable-carrier ·
    revocation contract · time-boxed bootstrap credential · intent-scoped portal links. Additive, not a redesign.
- ◐ **ENTITLEMENTS/TIERS (§8) + DELIGHT + SNAPVAULT DOGFOOD — DESIGN CAPTURED, build pending.**
  - **Two more prior-art sweeps** (61 platforms): tier/quota CORE (`wf_7c331efc-09c`, 25) + entitlement DIMENSIONS
    (`wf_9c953f4e-e6c`, 36) → `docs/architecture/tenancy-entitlements-findings.md`. Verdict: add the tier as ONE more
    resolution layer (ABP Edition, "not a new subsystem"); the **moat** = Koan owns resolution (§7) AND enforcement
    (§1b), the billing/auth incumbents punt on numeric-quota enforcement.
  - **ARCH-0099 §8 authored** (entitlements & tiers): 8a tier-as-layer (`solution→tier→tenant→user`, tier-as-data,
    versioned) · 8b typed KIND taxonomy · 8c declared numeric OPERATOR (Replace/MaxWith/Add/Pool, bound-on-lock,
    independent-buckets-most-restrictive) · 8d metering ledger (limit⊕meter join, domain idempotency, local reserve
    for hard caps, cache-limit-never-balance) · 8e enforce-at-chokepoint (Limitable, hard-admission-default,
    observe-mode, hard-backstop) · 8f over-limit FREEZE-not-delete + never-lock-remediation · 8g deferred/single-
    pending/RSoP-visible plan-change · 8h billing-decoupled (one grantor, gate-on-status) · 8i capacity≠quota +
    seat/resource aggregation · 8j RSoP+audit extension.
  - **5-persona DELIGHT harvest** (`wf_0d56fd46-7ae`) → `docs/architecture/tenancy-delight-synthesis.md`. Unanimous
    flagship = "the cross-tenant leak you literally cannot write" (BUILT). The moat (4/5). **Honesty layer:** the
    two highest-leverage delights (quota enforcement + erasure certificate) are DESIGNED-not-built; the **async-hop/
    durable-carrier hole most threatens the flagship**. Delight-killer: never ship a proof artifact before its
    fan-out is exhaustive.
  - **SnapVault dogfood** — studied `samples/S6.SnapVault` (photo SaaS, single-tenant, no auth) →
    `docs/architecture/snapvault-tenancy-proposal.md` (its domain IS the tier example; "add nothing to the
    entities") + `docs/architecture/snapvault-conversion-plan.md` (the **break-and-rebuild** plan: dogfood +
    framework-first-per-feature; the big break = in-memory worker → `Koan.Jobs` to close the async-hop hole).
    SnapVault exposes 3 real framework gaps: vector(Weaviate) tenant-isolation, storage blob per-tenant prefix, the
    async-hop carrier. **The conversion is the pull that orders the remaining tenancy build.**
  - **▶▶ CONVERSION STARTED (2026-06-24).** Phase-0 lead chosen by empirical grounding (`wf_ad703a7e-6a1`, 5 readers):
    the **durable ambient carrier** leads, NOT the portal shell. Reason: SnapVault's headline break-and-rebuild
    (in-memory worker → `Koan.Jobs`) is *structurally unsafe* without it — the carrier is the keystone the flagship's
    async path depends on; portal/vector/storage sit downstream. Grounding facts: `EntityContext` slice carrier is
    complete (ARCH-0097, 72/72) but **no snapshot/restore across the hop exists** — `JobRecord` has only `CorrelationId`,
    `JobOrchestrator.ExecuteClaimedAsync` builds a fresh `JobContext` with zero ambient restore; the work-item is
    `binding.Load`-ed at the TOP (before any context setup) so restore must wrap load-through-settle, which forces the
    ledger to be `[HostScoped]`. **ARCH-0100 DRAFTED** (`docs/decisions/ARCH-0100-durable-ambient-carrier.md`, axis-
    generic — ratifies ARCH-0099 §7d durable-carrier bullet): `IAmbientSliceCarrier`+`AmbientCarrierRegistry` in
    `Koan.Data.Core` (Capture→bag→Restore, fail-closed on unregistered-axis); tenancy registers `TenantContextCarrier`
    reusing `Tenant.Use`'s existing `IDisposable`; opaque sparse bag on `JobRecord`; `JobRecord`/`JobMetric` `[HostScoped]`;
    messaging/outbox = same-mechanism follow-on. 7-phase TDD impl plan in the ADR.
  - **▶▶ ARCH-0100 ADR RATIFIED + IMPLEMENTED — phases 1–6 GREEN (2026-06-24).** 3-lens adversarial review of the ADR
    (`wf_9daa6690-4b9`, all ratifiable-with-fixes) folded: generic `IAmbientExempt` marker in `Koan.Data.Abstractions`
    (NOT `[HostScoped]` — avoids a `Koan.Jobs→Koan.Tenancy` edge); ALL FOUR ledger entities exempt
    (JobRecord/JobMetric/**JobGateRecord/JobClaimTicket** — review CRITICAL-1); DI-enumerable discovery; chain-successor
    bag propagation; fail-closed *composed* with the §1b guard (3 null-states); `Capture()`→null-not-empty; `Clone()`
    deep-copy; mandatory carrier versioning; deleted-tenant ghost deferred to the P8 cancel-on-delete saga. Build (TDD,
    real-`AddKoan` specs, mutation, green-ratchet per phase):
    · **p1** `c0b8d435` — `AmbientCarrierRegistry`+`IAmbientSliceCarrier`+`IAmbientExempt` (10/10; fail-closed mutant killed)
    · **p2** `0186c110` — `TenantScopeMetadata` unions `[HostScoped]`|`IAmbientExempt` (flagship AssertNoTenantLeak gains the proof)
    · **p3** `31e33558` — 4 ledger entities `IAmbientExempt`; new `Koan.Jobs.Tenancy.Tests` (real SQLite boot + live Closed tenancy: full submit-under-tenant→claim→gate-backoff→settle, no leak)
    · **p4** `bfb03570` — `TenantContextCarrier` (versioned tri-state; reuses `Tenant.Use`/`None`); DI-enumerable reg + `AmbientCarrierRegistry` singleton
    · **p5+p6** `64f6ae0a` — capture at the 3 submit paths + restore-at-execute wrapping load→execute→settle + chain propagation + `DeadReason.CarrierRestoreFailed` fail-closed. `DurableCarrierSpec` 8/8 incl. a **barrier-forced 2-tenant concurrency** isolation proof; restore mutant killed (4 specs fail). No regressions: jobs in-memory 74/74, **SQLite durable 76/76**, data-core 252/252, tenancy 78/78.
    **NEXT: fold the impl-diff adversarial review (`wf_84072b03-11c`) → then phase 7 = the SnapVault async-path proof
    arrives during the conversion (SnapVault worker → `Koan.Jobs`). Messaging/outbox carrier = named follow-on. The
    durable-carrier keystone is DONE; the conversion's headline break-and-rebuild is now safe.**
  - **▶▶ THE DATA-AXIS MODEL — shape ratified by the architect (2026-06-24), ADRs written, IMPL pending (post-compaction).**
    Driven by the contributor-purity audit (`wf_8a2f7cf4-758` → `tenancy-contributor-purity-assessment.md`: tenancy ~90%
    golden; the wall = read-filter is equality-shaped) + an architect negotiation that generalised tenancy into a full
    **data-axis model** ([[tenancy-golden-contributor-standard]]). **The law:** all data-segmentation = registered
    contributors over composition **planes**; each plane = 1 engine + 1 DI-enumerable seam; the data core names no axis;
    an axis registers into the planes its MODE requires (Shared→field+filter · Container→name-particle · Database→routing).
    Along the way I shipped a conformity fix the architect caught: the bespoke `JobCoalesce` fold → the ONE ARCH-0096
    `AmbientAxisComposer` (`670476ba`). **ADRs:** **DATA-0106** (`9edddb6e`, read-filter contributor seam — predicate-
    generic; adversarially reviewed `wf_b3a46d8f-310` → folded a CRITICAL [pure-predicate-contributor bypasses fail-closed]
    + pushdown-not-residual + all-8-call-sites + cache-exclusion-ships-here) · **ARCH-0101** (`docs/decisions/ARCH-0101-
    data-axis-model.md`, the cohesive model: plane catalog + the 2 NEW planes [container-name particle
    `IStorageNameParticleContributor`; operation-semantics override `.OnDelete(Logical.SetTrue)` + plane-specific bypass
    `.HardDelete()`] + the premium **`[DataAxis]`** authoring layer [sugar over the seams] + safety-by-construction
    [boot-refuses-leaky-axis] + `.Explain()` query-RSoP + `DataAxis.AssertNoLeak<T>()`). Soft-delete = the canonical
    operation-plane reference module; moderation = the canonical predicate-plane one. **IMPL SEQUENCE (post-compaction,
    TDD+ARCH-0079+per-seam-review+mutation+green-ratchet):** A DATA-0106 read-filter · B name-particle seam · C operation-
    override + `Koan.Data.SoftDelete` · D `[DataAxis]` premium layer · E `.Explain()`+boot-refuse · F `AssertNoLeak<T>` ·
    THEN gap B (cache fold→AmbientAxisComposer + out-of-band evict-key bug) · gap C (storage 0.4 + vector 0.3,
    `tenancy-storage-vector-isolation-design.md`) · THEN the SnapVault conversion.
    - **✅ Phase A — DATA-0106 read-filter seam DONE (2026-06-24, `dev`, uncommitted→committed this session).** `IReadFilterContributor`
      + built-in `ManagedEqualityReadContributor` (re-home, byte-identical) + `ManagedFieldDescriptor.AutoReadFilter` + `ReadScopeFilter`
      at all 8 sites (grep-zero `ManagedReadFilter`) + fail-closed over the managed+contributor union + §4b `FilterSplitter` pushability
      + non-equality cache-exclusion. **4-lens impl-diff adversarial review (`wf_f75b6f5a-ae1`) → 2 HIGH + 1 MEDIUM, all verified + folded,
      both HIGH RED-verified:** (1) raw/CAS fail-closed now rides the contributor union via `IsReadScoped()` (a pure predicate axis with no
      managed field was bypassing QueryRaw/CountRaw/CAS); (2) cache-exclusion now rides the contributor set via `IReadFilterContributor.ExcludesFromCache`
      (a `[Cacheable]` entity scoped only by a pure predicate contributor was leaking across viewers); (3) per-read Split memoized per (type,adapter)
      so the equality hot path stays byte-identical to pre-DATA-0106. **Proven adapter-agnostic on SQLite (relational) AND MongoDB (document/BSON)** —
      the same fake moderation axis folds identically through the shared `FieldPathResolver`. Green: data-core 264, tenancy 81, sqlite 10, mongo 25,
      cache 50, json 7, inmemory 33. ▶ NEXT = Phase B (name-particle seam).
    - **✅ Phase B — container-name particle seam (ARCH-0101 §3) DONE (2026-06-24, `dev`).** `IStorageNameParticleContributor`
      + `StorageNameParticleRegistry` (static, the `ManagedFieldRegistry`/DATA-0105 §4 deviation from the ADR's "DI-enumerable"
      — the name composer is static+cached, reached in data AND vector naming with no DI scope) folded into `StorageNameGenerator`
      via the ONE ARCH-0096 `IdentifierComposer`. A separate-container axis (mode-3 tenant) emits `T1-Todo#partition` (leading
      particle, anchor untouched — "the axis is never in the spine"); host emits `Todo` byte-identical. **Security-critical fold:
      the ambient axis value is in the name cache key** `(provider,entity,partition,axisKey)` so a per-container name never caches
      across tenants (mutation-verified). Tests: data-core naming unit (name shapes + the cache-key leak pin) + SQLite integration
      (per-container isolation: write T1 / read T2 → not found; host sees a third container). **3-lens impl-diff review
      (`wf_1c43dcc1-d57`) folded an injectivity guard + 2 conformity fixes (all latent — no production name-particle
      contributor yet):** (1) particle value must be injective under the adapter policy (`PartitionTokenPolicy.IsInjective`, the
      ONE rule `PartitionNameValidator` now delegates to) — lossy/case-fold value fails closed (parity with the partition
      front-door, ARCH-0101 §8); (2) lock-free `Gather` (volatile snapshot, mirrors ManagedFieldRegistry); (3) `Register`
      dedups by logical `Axis` not CLR type. Green: data-core 271 (byte-identical re-home+delegation), SQLite 11, Mongo naming 2.
      ▶ NEXT = Phase C (operation-override plane `.OnDelete` + `Koan.Data.SoftDelete` reference module).
    - **✅ Phase C — operation-semantics override plane (ARCH-0101 §4) + `Koan.Data.SoftDelete` DONE (2026-06-24, `dev`).** A new
      **unguarded operation-override write channel** (`ManagedFieldWriteScope.Overrides`; injected but NOT conflict-guarded since a
      mutable state field changing by design must not be guarded; **isolation `Current` WINS on key collision** so an override can
      never clobber a tenant stamp — relational injects from `Effective`/guard unchanged on `Current`; Mongo splits inject/guard) +
      `OperationOverrideDescriptor`/`Registry` + a **target-scoped** `OperationOverrideBypass` ((type,id), bounded). The facade rewrites
      Delete/DeleteMany/DeleteAll/RemoveAll: load the VISIBLE (read-scoped, IDOR-retained) rows, re-persist with the field set;
      `.HardDelete()` (target-scoped bypass + WithDeleted) physically purges a visible-or-soft-deleted row. `Koan.Data.SoftDelete` =
      the canonical reference module (`[SoftDelete]`, invisible `__deleted` managed field, NULL-safe hide-deleted read contributor
      `AnyOf(IS NULL, != true)`, the override, `.HardDelete()`/`.Restore()`/`T.WithDeleted()` extension members). **4-lens impl-diff
      review (`wf_17a6b8a6-538`) folded 4 fixes** (abuse-vector REFUTED via isolation-wins): RowScoped⇒clean fail-closed on
      in-memory adapters · HardDelete purges soft-deleted · target-scoped bypass (no cascade leak) · `_deleteOverride` memoized.
      Green: SoftDelete 7, tenancy 84 (+ tenant×soft-delete two-axis isolation proof + JSON fail-closed), data-core 271, sqlite 11,
      mongo 25 (all byte-identical regression). ▶ NEXT = Phase D (`[DataAxis]` premium layer) → E (`.Explain()`+boot-refuse) → F (`AssertNoLeak<T>`).
    - **✅ Phase D — the `[DataAxis]` premium authoring layer (ARCH-0101 §7) DONE (2026-06-24, `dev`).** `src/Koan.Data.Core/Axes/`:
      a `[KoanDiscoverable]` **`IDataAxis`** (the `IKoanJob` pattern) + an **accumulative `Axis` builder** (`.Named`/`.AppliesTo`/`.Mode`/
      `.Field`/`.Reads`/`.OnDelete`/`.Carries`; all derivation in one post-`Declare` resolve pass ⇒ verb-order-independent) + `Logical`
      + **`DataAxisExpander`** (hosted in `RegisterKoanDataCoreServices`, once per SC). It validates → batch-collision-checks → **EXPANDS
      byte-identically** to the raw seams: `ManagedFieldDescriptor` (RowScoped+indexed+auto-equality-unless-`.Reads`) · memoized
      `DelegatingReadFilterContributor` (RowScoped+cache-excluded; **plain `Add`, never `TryAddEnumerable`** — the shared-type dedup
      trap) · `OperationOverrideDescriptor` · `DelegatingNameParticleContributor` (Container) · carrier. **Mode is config** (Shared→
      field+filter · Container→leading particle · Database→carrier-only). **ADR-first → design-validation panel (`wf_25cadfc7-48e`, 3
      lenses, settled the 4 open decisions: Database=explicit-throw, keep-SoftDelete-raw, `.Reads`-only-first-class, no-`WithoutIsolation`-
      in-D) → TDD → 4-lens impl-diff adversarial review (`wf_59a7628b-f10`, 14 agents, 7 confirmed/2 refuted) → folded 3 fix-classes:**
      cross-source field-collision **ownership ledger** [HIGH] (re-entrant-safe; fails loud where the registry silently first-wins) ·
      `Validate()` OnDelete-value-type-vs-field-ClrType [MEDIUM] · Container string-token-only (Validate + fail-closed contributor) [LOW].
      Green: Phase D **51** (44 unit: off, builder/validation, byte-identical registry state, N-distinct contributors, batch+cross-source
      collisions, Container particle, discovery skip-guard; 7 integration SQLite real-boot: `[Archived]` ≡ SoftDelete + multi-axis
      compose) + regression data-core 271 (off-proof), tenancy 84, SoftDelete 7, sqlite 11, mongo 25. New suites
      `tests/Suites/Data/Axes/*`. ▶ NEXT = Phase E (`.Explain()` query-RSoP + boot-report axis listing + boot-refuses-leaky-axis) → F (`DataAxis.AssertNoLeak<T>`).
    - **◐ Phase E — self-reporting (ARCH-0101 §8/§9). §9 DONE (2026-06-24, `dev` `7d768d92`); §8 pre-flight = NEXT increment.**
      **§9:** `DataAxis.Explain(sp, type)` — the query-RSoP rendering an entity's whole isolation story in one place
      (composing planes · the read-scope active in the CURRENT ambient · adapter fail-closed satisfaction + pushdown ·
      cache-exclusion · `IsLeak`), backed by a shared facade diagnostic (`IAxisScopeDiagnostics` + `IDataService.
      GetScopeDiagnostics`, the undecorated-facade authority that holds the raw adapter) + a shared `ReadScopeFold` (one
      fold authority — facade + Explain) so the explanation can't drift from a real read; + `DataAxisReport.Summarize` →
      boot-report axis listing. Impl-diff review (`wf_807c3ae1-538`) folded 3 MEDIUM (diagnostic-surface only): cache-
      exclusion now mirrors CachedRepository's 3rd leg (field-transform/`[Classified]`); a scoped entity on an
      UNresolvable adapter reads as a leak (bias-to-strict, not swallowed-to-safe); docstrings corrected. Green: Phase E
      63 (53 unit + 10 integration real-boot RSoP) + byte-identical regression (data-core 271, tenancy 84, SoftDelete 7,
      sqlite 11). **§8 pre-flight ✅ DONE (2026-06-24, `dev`):** `DataAxisPreflight` from `KoanDataCoreModule.Start` sweeps
      `AssemblyCache` entity types; for each read-scoped-in-the-boot-ambient (always-on axis) on an adapter that can't
      enforce it (reusing the §9 diagnostic) → **the architect's Koan posture: Dev WARNS + boot continues; Prod REFUSES
      boot** (`DataAxisLeakException`). The posture dissolves the blast radius — tests run non-Production → warn → the
      tenancy runtime-fail-closed JSON proof passes UNCHANGED (no migration). Impl-diff review (`wf_8ee3d9a0-59c`, 16
      agents) confirmed 9 (all MEDIUM/LOW, deploy-time-detection gaps, every one runtime-backstopped — NO live leak) →
      folded the load-bearing (gate opens on a managed field too [catches a constant-value equality axis]; `GetTypes`
      partial-load keeps `ex.Types` not fail-open-skip; logger-less warn → console; cached MethodInfo) + DOCUMENTED the
      deliberate v1 boundary (ambient-gated tenant write-stamp + write-stamp-only field defer to the runtime fail-closed
      — DATA-0106 "off axis is a no-op"; literal §8 "read-scopes" ≠ a tenant entity at boot). Green: Axes 55 unit + 10
      integration; tenancy 85 (incl. the §8 Prod-refusal + Dev-warn); off-proof data-core 271 (gate stays closed),
      SoftDelete 7, sqlite 11, Json 7, InMemory 33. **▶ PHASE E COMPLETE.** → then F (`DataAxis.AssertNoLeak<T>`).
    - **✅ Phase F — `DataAxis.AssertNoLeak<T>()` (ARCH-0101 §10) DONE (2026-06-24, `dev`).** The one-assertion cross-axis
      isolation proof, generalizing the flagship `AssertNoTenantLeak` to ANY value-isolation axis: `AssertNoLeak<TEntity,
      TKey>(withContext, a, b)` takes the axis's scope-enter `Func<string,IDisposable>` (e.g. `Tenant.Use`) and rides the
      matrix through the booted ambient host — read · get-by-id IDOR · scoped delete · async-hop carrier round-trip
      (`AmbientCarrierRegistry` capture→restore, when a carrier exists) · cache-key partition (when `[Cacheable]`) —
      throwing `DataAxisLeakDetectedException` on the first leak. Re-expressed `AssertNoTenantLeak` over it (Note +
      CachedNote, one call each) + a generic `RegionAxis` proof + a NEGATIVE proof (no-axis entity + no-op scope → throws
      on `read`, so the assertion is never vacuous). On `DataAxis` (parallel to `Explain`; throws, no test-framework dep).
      Focused adversarial review (`wf_3680d99b-eb0`, 3 lenses). Green: Axes 55 unit + 12 integration, tenancy 86,
      SoftDelete 7; purely additive ⇒ byte-identical regression preserved by construction. **▶▶ ARCH-0101 (the data-axis
      model, Phases A–F) COMPLETE.** ▶ NEXT = gap B (cache scope-key convergence + out-of-band evict bug) · gap C
      (storage blob-key 0.4 + Weaviate vector 0.3) → the SnapVault Phase-0 dogfood conversion.
- ✅ **GAP B — cache scope-key convergence + the out-of-band evict BUG — DONE (2026-06-24, `dev` unpushed).** Design note
  [cache-scope-key-convergence.md](./cache-scope-key-convergence.md). **The bug:** the managed equality scope (tenant) was
  in the cache key on ONLY the read path (`CachedRepository.AppendManagedScope`); the out-of-band evict sites built a
  scope-less, partition-less `"{TypeName}:{id}"` (`Uncache`/`Flush`) that matched nothing — a SILENT no-op against the
  read-path key `"{Type}:{Partition}:{Id}::__koan_tenant=<t>"`, and broader still: the `{Partition}` miss made evict a
  no-op for EVERY default-templated `[Cacheable]` entity. **The fix:** ONE canonical `Koan.Cache.Keys.ScopedEntityCacheKey`
  (public) — `AppendScope(base,type)` (read path) + `For(type,id,partition)` (evict path) both fold the equality axes
  through the ONE ARCH-0096 `AmbientAxisComposer` (`base::__koan_tenant=t`, byte-identical to the old fold for the lone
  tenant field). **Layering deviation from the ledger's first phrasing:** `CacheKey.For` could NOT gain the scope segment
  (`Koan.Cache.Abstractions` references only MS packages — can't see `ManagedFieldRegistry`/`AmbientAxisComposer`), so the
  fold lives one layer up in `Koan.Cache`; `CacheKey.For` stays the base-key primitive (now consumed by the evict path).
  DATA-0106 §5 non-equality cache-EXCLUSION kept. **TDD:** `CacheEvictKeyConvergenceSpec` (5, ARCH-0079) RED→GREEN
  (same-tenant evict hits · cross-tenant survives · Flush · non-axis partition-aware · default-id no-op). **Adversarial
  review** `wf_6de73ebb-7fe` (4 lenses + verify, 6/11 confirmed) folded: null-id no-op guard (MEDIUM), culture-invariant id
  token + verbatim (untrimmed) partition in `CacheKey.For` (LOW×2 — structural read/evict convergence for int/DateTime keys
  + whitespace partitions); documented out-of-scope-evict-no-op (contract), `_`-tenant collision (pre-existing/dev-only),
  multi-axis ordering (latent). Green: convergence 5, tenancy 91, cache abstractions 60, topology 50, crossengine 14, Axes
  55+12, SoftDelete 7, data-core off-proof 271. Byte-identical read-path key preserved.
- ✅ **GAP C — storage (0.4) + vector (0.3) axis isolation DONE. The tenancy data-plane is now isolated across data,
  cache, blobs, AND vectors.**
  **0.3 VECTOR SHIPPED (`dev`, `0db14602` impl + `e2a33932` canon + `c2d5c2ec` review-fold):** a `ScopedVectorRepository`
  decorator (the STOR-0011 twin) at `VectorService.TryGetRepository` (covers both `Vector<T>`/`VectorData<T>` facades +
  direct-`Repo` writes) — write-stamps the equality axes into vector metadata + ANDs `Filter.Eq(StorageName,value)` into
  every search; reuses `ManagedFieldRegistry` + (after the review) `IStorageGuard`. Off = byte-identical (InMemory surface
  29/29). Proof `VectorTenantIsolationSpec` (3 cases, no-Docker InMemory): KNN returns only the active tenant's vectors
  even when the query is nearer the other tenant's point · user filter composes (AND) · **no-tenant-in-scope FAILS CLOSED**.
  **Impl-diff review `wf_cc13952a-197` (12/21 confirmed) FOLDED the load-bearing security gap:** vector ran NO guard, so a
  no-tenant-in-scope search fell through UNFILTERED (a fail-OPEN leak, newly exploitable once writes are stamped) — fixed by
  reusing `IStorageGuard` (parity with data + storage) + non-equality fail-closed (§3). **▶ DOCUMENTED VECTOR FOLLOW-ONS
  (review):** embedding-worker async-hop writes unstamped (needs ARCH-0100 carrier on the embed hop, a `Koan.Data.AI` fix) ·
  `_canFilter` should check `FilterSupport.CanPush(Eq)` not just token presence · Weaviate drops the `__koan_tenant`
  property-name on its gate (Docker) · Delete/GetEmbedding/ExportAll by-id IDOR · production-adapter Testcontainers specs.
  **▶▶ THE DEEPER CONVERGENCE — DONE (contributor-agnostic realignment, `dev`: `4c5a6807` vector + `b2570c2e` selection).**
  The 3 hand-copies that re-derived axis logic are gone: (1) **vector** (`ScopedVectorRepository`) now resolves
  `IReadFilterContributor[]` + `IStorageGuard[]` from DI and folds reads via the shared **`ReadScopeFold`** (made public) +
  stamps ALL applicable managed fields on write — so a **non-equality moderation `IReadFilterContributor` now isolates a vector
  KNN with ZERO vector-specific code** (proven: `VectorTenantIsolationSpec.Vector_honors_a_non_equality_read_contributor`,
  `__vis != hidden` hides a hidden doc, moderator sees both); the bespoke `ScopeFilter` + the §3 band-aid are deleted; fail-closed
  only when the fold yields a predicate but the adapter lacks `VectorCaps.Filters`. (2) The equality-axis **SELECTION** ("which
  managed descriptors are an equality scope") is extracted to **`ManagedFieldRegistry.EqualityFields(type)`** (the ONE selection,
  type-memoized, invalidated on Register/Reset) and consumed by `ManagedEqualityReadContributor` (the `Filter.Eq`),
  `ScopedEntityCacheKey` (the cache-key segment), and `StorageKeyScoper` (the leading blob particle via the shared
  `ComposeEqualityParticles`; the §3 non-equality fail-closed factored to `RefuseNonEqualityScope` — storage's capability
  boundary, not a re-derivation). No plane re-implements the selection; each only RENDERS its plane-specific encoding.
  **Byte-identical** (data-core 269 [−2 retired obsolete vector specs] · tenancy 103 · cache topology 50 / abstractions 60 ·
  storage core 3 · InMemory vector 29). Obsolete `ManagedFieldVectorFailClosedSpec` (pinned the retired pre-decorator
  blanket-fail-closed) retired; `VectorAdapterResolutionSpec` unwraps the always-applied decorator via the new
  `IDecoratedVectorRepository` introspection seam. **Focused impl-diff review FOLDED (`fe685742`): a HIGH — the vector fold
  pulled in `SoftDeleteReadContributor`, but `[SoftDelete]`'s `__deleted` is set on the DATA row's delete (an
  `OperationOverrideDescriptor`), never stamped into the independent vector store on write, so its predicate references a
  field absent from every vector record (vacuous at best, OVER-FILTERS a KNN to zero at worst). Fix `FoldReadScope` = the
  shared fold MINUS any operation-override axis (detected generically via `OperationOverrideRegistry.ForDelete` + a
  closed-hierarchy `Filter` field walk) — so the STAMPED axes (tenant, moderation) stay enforced while the unsynced
  override axis is excluded (TDD RED→GREEN; off ⇒ byte-identical). + a LOW: the metadata dict copy was `OrdinalIgnoreCase`
  (THROWS on a user's case-colliding keys) → `Ordinal`.** Review cleared the EqualityFields memo (invalidated on
  Register+Reset), the storage particle-ordinal (single-particle ⇒ no key bytes change; the index-base is now consistent
  across both paths), and `ManagedEqualityReadContributor` co-registration. **▶ NEW FOLLOW-ON: vector lifecycle-sync of an
  operation-override** — a soft-deleted entity's vector stays searchable (the delete doesn't propagate to the independent
  vector store); until a vector-delete hook lands, `[SoftDelete]`+vector visibility is NOT enforced on the vector plane
  (a visibility gap, not a security one — tenant isolation IS enforced).
  Media `GET /media/{id}` + presign integration specs DEFERRED (app-authored `IMediaSource` + ASP.NET plumbing /
  Docker-gated S3; the framework byte-read isolation is proven via `MediaEntity.OpenRead`).
  ADR [STOR-0011](../decisions/STOR-0011-storage-blob-key-axis-isolation.md) **v2 = Accepted** (impl-diff review `wf_03ef19e6-88c`,
  18/21 confirmed, FOLDED `a22c5e5e` — the missed `StorageObjectExtensions` parallel surface scoped + logical-key/Name fixed +
  ScopeAmbient §3 fail-closed/guard-order; 8 isolation cases green, tenancy 99/99; LOW perf/presign/list-shard/host-scoped items
  documented in the ADR).
  **v1 (a `StorageEntity<T>` funnel) was REJECTED by a 35-agent design panel (`wf_ac5a1e07-54a`, 5 CRITICAL + 6 HIGH)** — the
  media surface (`MediaController`/`IMediaSource`/`MediaEntity.OpenRead`), presign, the type-erased extension helpers, the
  backup services, and `From/To` all bypass `StorageEntity<T>`. **v2 SHIPPED (`dev`, commits `9cff2a45` impl + `a7bb9029`
  proof + `9a1b89e3` media proof):** a `ScopedStorageService` DECORATOR over `IStorageService` (the one boundary every blob
  path funnels through) composes the leading axis particle + runs the guard, using an ambient `StorageScope` type-carrier
  (typed → `ManagedFieldRegistry.ForType` + typed `IStorageGuard` + `[HostScoped]` exemption; raw → fail-safe
  `ManagedFieldRegistry.All` clean values + value-guard; infra → `HostScoped()` opt-out [backup services]) + a mandatory
  sanitizing `StorageKeyParticleFormatter` + the logical(`StorageEntity.Key`)/physical(`StorageObject`) boundary via
  `From(obj, logicalKey)`. Reuses EXISTING seams — no new seam, no `Koan.Tenancy`→`Koan.Storage` edge. **Off = byte-identical**
  (Storage Core 3, Media Core 567 green). **Real-boot proof `StorageTenantIsolationSpec` (6 cases): StorageEntity ·
  MediaEntity (the OpenRead-override fix, PhotoAsset's base) · raw `IStorageService` · unscoped fail-closed · `[HostScoped]`
  shared · hostile-value rejected** — the integration test caught a real bug (the fail-safe used the carrier's versioned
  token `v1:id:acme` as a path segment → fixed to read `ManagedFieldRegistry.All` clean values). Tenancy suite 96/96.
  **SnapVault dogfood:** the MediaEntity proof exercises PhotoAsset's exact surface; full SnapVault runtime-wiring deferred
  (needs Docker + background-worker tenant-threading) and documented in the tenancy how-to instead. **Tenancy promoted to a
  first-class PILLAR (`9d7296de`):** card `reference/cards/tenancy.md` + how-to `guides/tenancy-howto.md` + skill
  `koan-tenancy` (compile-gated) + CLAUDE.md/skills-README/SURFACES registration. **0.3 vector** = the remaining sibling
  (needs Docker/Weaviate; axis-generic with ARCH-0098). NEXT = fold `wf_03ef19e6-88c` → mark STOR-0011 Accepted; then 0.3 vector.
- ◐ **ARCH-0102 — THE ACCESS OVERLAY DEFINITION BLOCK (AODB): PHASES 1 + 2 SHIPPED (2026-06-25, `dev`, unpushed).**
  The generalization of ARCH-0101: isolation intent composed once into an inspectable `Aodb` + pushed down + adapter-realized;
  the break-and-rebuild collapse of the per-plane scoping forks onto ONE composer. ADR `docs/decisions/ARCH-0102-...md`
  (decision + Addendum I corrections + Addendum II the delight inversion) + plan `docs/architecture/aodb-implementation-plan.md`.
  **Phase 0 `a391e4d9`** — `OverlayNamingRule` (framework-owned, adapter-declared, override-only) closes the CONFIRMED
  Weaviate vector leak (GraphQL reserves `__`; Weaviate declares `__`→`koan_`; the framework renames write-stamp + read-filter
  from the one declaration; live RED→GREEN `WeaviateOverlayIsolationSpec`). **Phase 1a `b9bf3792`** — `[Flags] FieldProvenance`
  derived onto `ManagedFieldDescriptor`. **Phase 1b `bb34c995`** — `ReadScopeFold.Compose` → the one provenance-tagging
  inspectable `Aodb`; vector `FoldReadScope`/`FilterMentions` DELETED, collapsed in (store-aware push `CombineWriteStamped`).
  *Honest scope (critic-confirmed): facade keeps `Fold`, storage/cache keep `EqualityFields`, `ManagedEqualityReadContributor`
  stays a source — the genuine collapse is the vector fork → the one composer, not the plan's over-stated facade/storage/cache
  migration.* **Phase 1c `a016207e`+`255fefa9`** — soft-delete & tenancy (the flagship) migrated to discovered `[DataAxis]`
  declarations; **both hand-registrars DELETED**; tenancy guard/posture/pre-flight/dev-seed/Report STAY (policy, not plane).
  Tenancy is now the golden contributor example the standard always demanded. Byte-identical: tenancy 104, data-core 273,
  Weaviate 30, InMemory vector 29, SoftDelete 7, Axes 56; full-solution build clean. **▶ PHASE 2 SHIPPED — Database-mode
  AUTO-ROUTING.** The carrier-only contract relaxed: a Database axis now declares `.Field` (the per-op SOURCE-KEY provider)
  + `.Carries`, forbids `.Reads`/`.OnDelete`. The expander registers a `DatabaseRouteDescriptor` into the new
  `DatabaseRouteRegistry` (copy-on-write, lock-free reads); `AdapterResolver.ResolveForEntity` consults it at **Priority 1.5**
  (after an explicit `EntityContext.Source` — the caller override always wins), gated by `IsEmpty` ⇒ off = byte-identical
  (FC-5). Unconfigured-source routing fails closed, self-explaining (FC-7) = `ProvisioningPosture.ExternalOnly`. Proven by a
  Docker-free multi-DB SQLite gate (`MultiDatabaseRoutingSpec`): RED (no hook ⇒ both shards in Default, no error — the footgun)
  → GREEN (physical isolation across two DB files) + fail-closed. Adversarial routing-core review 0 CRITICAL; the under-lock /
  per-call-lock findings fixed by the copy-on-write registry. Green: axes unit 58, axes integration 14, data-core off-proof 273
  (FC-5), tenancy 104. **Phase-3 follow-ons (pinned):** strict-isolation `NullKeyBehavior.FailClosed` opt-in; overlapping-route
  boot detection; per-tenant placement = P6 broker. Remaining for ARCH-0102: Tier-2 visibility (`.Explain` full-Aodb + boot
  report). Generalizes the contributor-agnostic realignment above. **▶ FLEET MANDATE (ARCH-0103, architect canon):** every
  Koan-shipped data adapter (all 15) MUST implement all three AODB modes (Shared/Container/Database), realization bar =
  native-or-emulated NO EXCEPTIONS, ZERO debt carried (harvest+rebuild). Realized via storage-model **family bases**
  (`RelationalStore` exists; new `DocumentStore`/`KeyValueStore`; `ScopedVectorRepository`+new `HttpVectorStore`) + a
  **helper-module layer** (`ManagedFieldJsonInjector`, `FilterAstWalker`, `HttpVectorStore`) so adapters get THIN
  (feasibility eval: fleet ≈ −45% LOC + debt cleared). ONE `IAdapterFactory` marker + `RoutedSource` (kills the vector/record
  routing split-brain). Build P1 Moniker-contract → P2 KeyValueStore → P3 DocumentStore → P4 vector+overlay-leak-fix → P5
  conformance ledger (`ContainerScoped`/`DatabaseScoped` tokens + `AodbConformanceSpecsBase<TFactory>`). ADR
  `docs/decisions/ARCH-0103-aodb-adapter-conformance.md`.
- ☐ **THEN:** Phase 3c schema-column DDL indexability (Indexed descriptors → computed/expression index; PG/SqlServer;
  SQLite JSON-only) + Mongo/bare-store managed serialization injection + in-memory managed `GetValue` · classification
  phases 4–7 (searchable blind-index · vector/messaging leak guards · crypto-shred+rotation · masked-read) · then
  control-plane keyed entities / state machine / sagas / erasure cert · ambient unification · Adapter Forge · Facet 4.

> Full per-area detail + the DATA-0105 review punch-list (must-fixes i–xi, upgrades A–D, opportunities):
> memory **[[facet3-tenancy-design]]** (the anchor). Tenancy spec: [tenancy-design.md](./tenancy-design.md).
