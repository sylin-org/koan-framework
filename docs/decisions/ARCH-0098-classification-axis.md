# ARCH-0098: The data-classification axis — field-transform seam, layered policy, and the crypto seam

- Status: Accepted (review-amended; phase 1 landed `cc486781`)
- Date: 2026-06-22
- Siblings: ARCH-0095 (tenancy — the other Facet-3 axis), DATA-0105 (the contributor umbrella), ARCH-0084 (capabilities), ARCH-0094 (Adapter Forge — the KMS external-infra seam)
- Grounding: empirical side-discovery (workflow `wf_c4cb6674-564`, 3 source-grounded lenses) + `docs/architecture/tenancy-design.md` §5 (the settled layered-policy design) + the managed-field seam (DATA-0105 §3b, shipped)
- Review: adversarial workflow `wf_6a0f0278-5b5` (5 source-grounded lenses → adversarial verify → synthesis), verdict **RATIFIABLE-WITH-AMENDMENTS**, 9 verified HIGH+ findings — all folded below (§0 batch/cache/comparand corrections, §3a crypto contract, §5 phase-0 housekeeping, §6 leak-guard corrections, the clone-then-encrypt + messaging-out-of-scope decisions). The thesis (write-stamp plane · sibling registry · greenfield crypto on the issuer-template shape · capability asymmetry · §5 ordering) was tried and **could not be refuted**.

## Context

Classification is Facet 3's second axis (tenancy is the first). Where **tenancy** isolates *rows* by an ambient
discriminator, **classification** protects *field values*: an entity declares a FACT — a property is `[Pii]` /
`[Phi]` / `[Pci]` / `[Secret]` — and the framework applies layered HANDLING (encrypt / tokenize / mask) resolved
solution → tenant → developer-hint, with a solution-set **mutability lock** (the policy-gate-above-tenant, mirroring
tenant-gate-above-roles). The settled policy model is `tenancy-design.md` §5; this ADR is its **implementation-ready
mechanism**, grounded against current source.

**Classification is NOT the managed-field seam.** The managed field (DATA-0105 §3b) *injects* an invisible,
non-POCO discriminator (no backing property, one-way, ambient-sourced, isolation-**filtered**). A classified field
*transforms the value of an existing POCO property* (backing property, **round-trip**, entity-sourced,
value-**protected**). The shapes are structurally inverse — so classification is a **sibling** contributor under the
same DATA-0105 §0 umbrella, not a reuse of `ManagedFieldRegistry`.

## Decision

### 0. The seam is a POCO-property value-transform: write-stamp + read-reverse (NOT a serialize hook)

The encrypt/tokenize/mask transform runs **on the entity's POCO property at the data-core chokepoint**, not in any
adapter's serializer:

- **Write** — a new `IFieldTransform.ApplyOnWrite(entity)` joins the per-type `StorageWritePlan` (the same list that
  holds `IdentityWriteStamp` + `TimestampWriteStamp`). It must cover **every** write surface, and the facade's surfaces
  are *not* uniform (review-corrected against source):
  - `Upsert`/`UpsertMany` call `_writePlan.ApplyAll` — covered.
  - The **batch** path (`Entity.Batch().Save()`) calls `_writePlan.ApplyBatch`, which iterates only the stamps whose
    `AppliesInBatch` is `true` (`StorageWritePlan.cs`). `TimestampWriteStamp.AppliesInBatch => false`; the classification
    transform **MUST** set `AppliesInBatch => true`, or batch writes persist **plaintext at rest** (Blocker 1).
  - `ConditionalReplaceAsync` (the jobs/CAS path, `RepositoryFacade.cs`) forwards `model` with **no** write-plan call
    today — it must run `ApplyAll(model)` too, and a `[Classified]` property may not appear in a CAS guard predicate (the
    guard is a CLR comparison over POCO props — stored ciphertext vs caller plaintext would never match) unless it routes
    through the searchable comparand (Blocker 2).

  **Non-corrupting write (decision):** the stamp encrypts into a **shallow clone** that is persisted, *after* the
  identity/timestamp stamps have run in place — so the caller's instance keeps its assigned id + timestamp **and its
  plaintext** (`entity.Save()` does not hand back a ciphertext-corrupted object), and there is no decrypt-on-write
  (rejected alternative: encrypt-in-place + decrypt-back double-cryptos and leaves the caller's instance ciphertext if
  persistence throws between the two). Classified fields are typically immutable strings, so reassigning them on the
  clone never touches the caller. Clone cost is bounded to classified writes; unclassified types keep the byte-identical
  no-clone fast path.
- **Read** — a **net-new** `IFieldTransform.ApplyOnRead(entity)` reverse applicator runs on the facade's
  `Get`/`GetMany`/`Query`/stream results, **after** the adapter materializes the entity and **before** it leaves the
  chokepoint, restoring plaintext into a **request-scoped plaintext map** (an AsyncLocal, the classification analog of
  `ManagedFieldWriteScope`; never the distributed cache — `tenancy-design.md` §5f).

**Why the write-stamp plane, not the serialize plane** (the pivotal fork): the write-stamp is the *single
adapter-agnostic* point, so **all 8 data adapters** — including Redis / Couchbase / InMemory, which have **no Koan
serialize hook at all** — get field-encrypt/tokenize for free. The serialize plane is *not* one shared point: it is
one shared seam for the relational trio (`ComparableScalarEncoding.Apply`) **+ N separate** hooks (Json's own
resolver, Redis raw `JsonConvert`, Couchbase SDK, Mongo `BsonClassMap`, InMemory none). The write-stamp also keeps the
request-scoped-plaintext model coherent (the persisted clone is ciphertext; the caller's in-memory entity stays
plaintext).

> **Correction (review).** An earlier draft claimed the write-stamp "accidentally fixes the cache-value leak." It does
> **not**. `CachedRepository` decorates *outside* `RepositoryFacade` (`DataService` builds the facade, then wraps the
> cache around it), and it caches the entity returned by the facade's *read* — which is **post-reverse plaintext**. So a
> read-miss caches **plaintext into L2**, and a cache-hit returns the cached object to the caller **without** the
> facade's `ApplyOnRead` ever running. The resolution is a **phase-3 gate**, not a free side effect: classified entities
> are **excluded from L2** (forced L1-only / `NoCache`), because running the reverse on a hit would force
> `CachedRepository` to take a dependency on the classification module (see §6).

> **Reserved exception — searchable equality.** For `[Pii(Searchable)]` blind-equality, a deterministic keyed-HMAC
> *comparand* must be emitted on both the write (the stored blind index) and the filter leaf so equality pushes down;
> LIKE/range stay honestly denied. This is a **net-new comparand hook** — *not* a reuse of the DATA-0100 temporal
> `ComparableScalarEncoding.EncodeComparand` switch (review-corrected: that path encodes order-comparable scalars, not a
> keyed HMAC). The honestly-shared mechanism with the managed field is the **literal-identity coupling** (the same
> StorageName / JSON-access path is used by the write and the filter leaf), not the comparand encoder. This is the only
> place the serialize/comparand plane is touched.

### 1. A sibling `ClassifiedFieldRegistry` + `IFieldTransform` (mirror `IWriteStamp`/`TimestampPropertyBag`)

A property-keyed, round-trip contributor, peer to `ManagedFieldRegistry` and `IWriteStamp` under DATA-0105 §0:

```
ClassifiedFieldDescriptor(                                                       // facts-as-data (NOT handling)
    PropertyInfo Property, ClassificationCategory Category, bool Searchable,
    Func<object,object?> Getter, Action<object,object?> Setter)                   // Expression-compiled once per type
ClassifiedPropertyBag(Type)   // scan-once like TimestampPropertyBag; per-type HasClassifiedFields gate
ClassifiedFieldRegistry       // static Type-plane memo + global IsEmpty off-gate (Activate()-triggered)
```

**The descriptor carries FACTS only — no transform-kind** (review-corrected facts/handling split): `Kind`
(encrypt/tokenize/mask) is **policy-resolved per-op** (keyed by Category × tenant × hint), not an entity fact, so it is
resolved at the chokepoint at handling time — mirroring `ManagedFieldDescriptor.ValueProvider`'s read-once-per-op — and
never baked into the scan-once per-type descriptor.

It copies the managed-field registry's *mechanics* (Type-plane memo · `IsEmpty` volatile off-gate making the off path
byte-identical · descriptor-not-callback · ARCH-0084 fail-closed) but is its **own** registry with a **two-gate off
model** (review-corrected): the global `IsEmpty` is *not* flipped by per-type entity scans (facts live on entities, not
in a `Register()` call) — it is flipped **once at boot by the `Koan.Classification` registrar** (`Activate()` =
Reference = Intent), and a **per-type `HasClassifiedFields`** gate (like `HasTimestamp`) keeps the byte-identical fast
path for unclassified types. The managed-field's no-property/one-way descriptor has no slot for the property handle, the
read reverse, or per-property keying — hence sibling, not reuse. *(Phase 1 of this design is landed: the attributes +
`ClassifiedFieldDescriptor` + `ClassifiedPropertyBag` + `ClassifiedFieldRegistry`, committed, no crypto.)*

### 2. Facts in Abstractions, contributors in `Koan.Classification`

- **Facts** (property-targeted sealed attributes in `Koan.Data.Abstractions/Annotations`, alongside `[Timestamp]`):
  `[Classified(category)]` is the extensible primitive; `[Pii]` / `[Phi]` / `[Pci]` / `[Secret]` are sugar over it.
  An entity declares facts only — *no handling on the entity* (conformity-by-design: a data-core `grep -i "pii"` stays
  empty; handling is config + the module).
- **Contributors** (the transforms, the registry registration, the crypto) live in a **new `Koan.Classification`
  module** (Reference = Intent, mirroring `Koan.Tenancy`). Not referencing it = empty seam = structural no-op.
- **`[Secret]` is write-only/masked-read** (Set works, Get returns a mask) — the strongest sensitivity primitive.
- **NO `[Phi(Embeddable=true)]` entity hint** (round-3 cut): embeddability is policy/config, not an entity fact; intent
  rides the existing `[Embedding]` + solution `allowEmbedding`.

### 3. The crypto seam (greenfield; the `IIssuerKeyStore` lifecycle is the template)

No field encryption exists today (tree-wide search: zero AES/HMAC-for-encryption — all crypto is hashing). The one
proven template to copy *in shape* is the JWT-issuer key lifecycle (`IIssuerKeyStore`/`PersistedIssuerKeyStore`:
env-tiered seam · Reference = Intent · rotation-with-overlap · key persisted as an `Entity<T>` row · `ZeroMemory`).
The classification crypto seam (in `Koan.Classification`):

- **`IKeyProvider` / `IKeyRing`** — singleton, **per-tenant** (keyed by `Tenant.Current?.Id`), env-tiered (ephemeral
  dev / KMS-backed prod via `services.Replace`). Exposes `GetActiveKey`, `GetForDecrypt(keyId)`, and crucially
  **`DestroyKey(tenantId)` for crypto-shred** — the mechanism behind the unanimous flagship, the cryptographic
  **erasure certificate**.
- **`IFieldCipher`** — stateless AES-GCM, returns a `{ciphertext, keyId, nonce}` envelope (the `keyId` is embedded so
  `GetForDecrypt` survives rotation).
- **`IBlindIndex`** — keyed-HMAC for `[Pii(Searchable)]` (`FixedTimeEquals`).
- **Rotation** — an `IKoanJob` mirroring `IssuerKeyRotationService`.
- **DO NOT** use `IDataProtector` for field values (no per-tenant key, no exposable key-ID, can't crypto-shred one
  tenant). `IDataProtector` stays valid only for wrapping the master/data keys at rest. The **prod KMS** (Azure Key
  Vault / AWS KMS / Vault) is the first such dependency and sits behind the **external-infra-delegation / Adapter Forge
  (ARCH-0094)** seam, not a direct `PackageReference`. Crypto hygiene: `RandomNumberGenerator.Fill` (nonces),
  `CryptographicOperations.FixedTimeEquals` (blind-index), `ZeroMemory` (derived data keys).

### 3a. Crypto-correctness contract (review) — the parts that do NOT transfer from the issuer template

The issuer template gives the *shape* (env-tiered seam, Reference = Intent, rotation-with-overlap, `Entity<T>`-persisted
key, `ZeroMemory`). Field crypto has correctness obligations the template does **not** cover, because a JWT issuer key
protects values with a built-in expiry (tokens die) while **field ciphertext has no expiry** — a key may be needed to
decrypt a row written years ago. These are load-bearing:

- **Retiring-key retention (rotation ≠ purge).** A key referenced by the `keyId` envelope of **any extant row** is
  **never purged** by rotation. Rotation only changes the *active* key for new writes; old rows keep decrypting under
  their embedded `keyId`. v1 retention = **indefinite retiring-key retention** (no automatic purge). A background
  re-encrypt sweep is optional and out-of-v1; note that the write-stamp re-encrypts a row under the active key on its
  *next* `Upsert`, but **never-touched rows are never migrated**, so a retiring key cannot be dropped on a timer.
  *(Invariant test: rotate, then read a pre-rotation row → still decrypts.)*
- **`GetForDecrypt(keyId)` resolves the OWNING tenant's key, independent of `Tenant.Current`.** The `keyId` encodes /
  maps to its `tenantId`; decrypt must resolve by that, not the ambient tenant — otherwise background jobs, admin
  tooling, and the §8 migration-saga (which run under a different or no ambient tenant) produce silently-undecryptable
  reads. **Encrypt** stays strictly under the ambient tenant's *active* key.
- **`DestroyKey` (crypto-shred) is the INVERSE of the template's purge**, not a parameterization. The template removes a
  key *after* its dependents have died; shred removes a key *because* its dependents must become unreadable. Contract:
  scope = a whole tenant (partial-field shred is impossible under one per-tenant key); irreversible + idempotent;
  post-condition — the **erasure certificate survives the key** and is **not** encrypted under it. This is the mechanism
  behind the flagship erasure certificate.
- **AES-GCM random-nonce budget.** 96-bit random nonces have a birthday bound: rekey well before ~2³² encryptions
  **per key**. A long-lived per-tenant key that encrypts every classified field is exactly where this bites — rotation
  must be **encryption-count-aware** (not only time-based), or adopt AES-GCM-SIV / a per-message subkey. State the
  per-key budget in the boot report.
- **Master-key-at-rest must not depend on an unprovisioned `IDataProtector` keyring.** For field crypto a lost DP
  keyring = **permanent at-rest corruption** — a *third* corruption cause beyond rotation-mapping-loss and shred.
  Therefore: delegate master-key wrapping to the **KMS / Adapter-Forge external-infra seam** (ARCH-0094; §3 already
  routes prod KMS there) and treat `IDataProtector` only as the *ephemeral-dev* tier, **with a fail-closed boot guard**
  that refuses to start prod on an ephemeral keyring. DP-keyring-loss is named as a distinct risk.
- **Blind-index equality is tenant-local.** A per-tenant HMAC key means `[Pii(Searchable)]` equality pushes down **only
  within the ambient tenant's scope**. State + boot-report this boundary. The no-ambient-tenant case (`[HostScoped]` /
  `Tenant.None`) must use a dedicated host key **or fail closed** — never a silent zero-match. *(Test: a blind-index
  lookup with no ambient tenant fails closed, not silently empty.)*

### 4. Capability tokens — the deliberate asymmetry

A new axis-free `DataCaps.Classification` family (Reference = Intent). The asymmetry is load-bearing and explicit:

- **The base encrypt/tokenize/mask path needs NO adapter token** — it transforms *above* the adapter (the entity is
  ciphertext before serialization), so **every** adapter works (unlike `Isolation.RowScoped`).
- **Tokens + fail-closed apply ONLY to the searchable/pushdown subset** — native blind-index / native column
  encryption. The adapters that announce these are exactly those with a Koan-controlled per-field serialize hook
  (the relational trio + Mongo) — the same honest capability boundary the managed field already drew.

### 5. Ordering — the priority field is a prerequisite (DATA-0105 §3)

`StorageWritePlan.Build` is today a **closed positional list** and `ManagedFieldRegistry` has **no priority field**,
contradicting DATA-0105 §3's "total, stable, explicit-priority order frozen at discovery." Opening the write-plan
slot for **two** contributors (the managed-field/tenant stamp and the classification transform) **requires** an
explicit priority field first. Canonical order on one record: **tenant-stamp → classification-encrypt** on write
(stamp the owner, then protect the value); **classification-decrypt** on read (the read side has no tenant analog —
the tenant field is filtered, never read back). This is a prerequisite, not a nicety.

**Phase-0 housekeeping (review).** The priority field lands on **both** ordering surfaces that exist today and both lack
it: the `IWriteStamp` set in `StorageWritePlan` *and* `ManagedFieldDescriptor` (two distinct registries). The
`IWriteStamp` contributor interface is currently `internal` to `Koan.Data.Core` and must be **exposed** so `Koan.Tenancy`
and `Koan.Classification` can register transforms (the DATA-0105 phase-4 "open the slot" opening, now with two
consumers). **Read-reverse placement is a hard constraint:** the reverse must run **inside** the facade's
`Get`/`GetMany`/`Query` **below `Data.QueryWithCount`**, so the `FilterPushdownCoordinator` residual filter and any
in-memory sort operate on **plaintext** (a non-searchable classified field filtered/sorted in the residual must see
decrypted values). *(Mutation test: residual filter/sort over a classified non-searchable field returns correct rows.)*

### 6. Value-level leak guards (no managed-field analog)

- **Cache — a phase-3 gate, not a free side effect** (review-corrected). `CachedRepository` decorates *outside* the
  facade and caches the *read result* (post-reverse plaintext on a miss; returns it unreversed on a hit), so the L2
  value would be **plaintext**. Resolution: **exclude `[Classified]` entities from L2** (force L1-only / `NoCache`) —
  running the reverse on a hit would force the cache decorator to depend on the classification module. This is a
  **phase-3 gate**, validated before searchable/leak phases, not deferred to phase 5.
- **Vector / AI** — `[Phi]` is **excluded from embedding at embed-build time**, enforced **inside `BuildEmbeddingText`**
  (the single chokepoint) so it is *caller-independent*. R07-15 removes embedding text from the durable `EmbedJob` carrier:
  the queue stores Entity identity, signature, and opaque context, then the worker restores that context, reloads the
  current Entity, and rebuilds the text. The **sync embed hook still sees the in-memory value while the async worker
  reloads + reverses to plaintext**, so caller-independent exclusion remains mandatory even though the queue no longer
  persists business text. When `allowEmbedding` is set without a
  working scrub there is **no fail-closed backstop** on the vector *write* path (unlike tenancy's search-time
  fail-close), so **scrub-or-deny is mandatory at the chokepoint** (throw `CapabilityDeniedException`). This guard is
  embed-time exclusion, *not* the tenant isolation fail-close — classification protects values, not rows.
- **Raw / RLS** — out of scope for the transform; documented.
- **Logs / audit / durable carriers — OUT OF SCOPE for v1, with a named follow-on** (review-corrected; was overstated
  as covered). No redaction chokepoint exists, and the **largest uncovered hole is the message bus**: `myMsg.Send()`
  serializes the payload *outside* the facade (`MessagingExtensions.Send<T>` → `IMessageProxy.SendAsync`), so the
  write-stamp structurally cannot reach it. No Outbox/DLQ type exists yet. Named follow-on: a classified-field redaction
  pass at the messaging serialize seam (tracks with the tenancy durable-carrier work).

### 7. Masked-read is a PROJECTION concern (forward-referenced to SEC-0004)

`[Secret]` / masked-read is **per-caller** (admin sees masked, doctor sees plaintext, integration sees a token of the
*same* stored value) — decided at the SEC-0004 `can:[]` / `IEntityTransformer` / WEB-0068 read-predicate layer, NOT at
storage. Classification needs **both** the storage round-trip (this ADR) **and** a projection-masking hook (SEC-0004).
Conflating them (masking at storage) breaks per-caller variation; forgetting the projection hook leaks plaintext to
unprivileged callers despite correct at-rest crypto. This ADR owns the at-rest round-trip and *forward-references* the
projection mask.

### 8. Migration

Classifying an existing field is a backfill/re-encrypt migration; an effective-policy change (CoLocate→FieldEncrypt,
or rotate-then-shred) is a **bidirectional P8 migration-saga** (data moves before enforcement switches). Additive-only
v1 (per round-3); the saga is the carrier.

## Convergence (the "fewer but more meaningful parts" story)

- **One contributor umbrella (DATA-0105 §0):** managed-field injection, `IWriteStamp`, and classification
  field-transform are peer contributors composed into the same per-type plan. Classification invents no parallel
  pipeline — it joins the **same `StorageWritePlan`**. The single external-contributor slot the plan must grow serves
  **both** tenancy and classification: **one opening, two consumers.**
- **Shared discovery convention** with `[Timestamp]`: `ClassifiedPropertyBag` mirrors `TimestampPropertyBag`
  (scan-once, Expression-compile getter/setter per attributed property). Classification *is* structurally the same
  family as `[Timestamp]` — a property-keyed value-mutator — plus the deserialize-side reverse it lacks.
- **The literal-identity coupling** that makes managed-field equality push down (same StorageName / JSON-access path on
  write and filter leaf) is what makes classification blind-equality push down. *(The comparand **encoder** is **not**
  shared — classification's keyed-HMAC comparand is a net-new hook, not the DATA-0100 temporal `EncodeComparand`; review
  corrected the earlier "one mechanism, two axes" overclaim.)*
- **The crypto seam borrows the `IIssuerKeyStore` lifecycle SHAPE** (env-tiered seam · Reference = Intent ·
  rotation-with-overlap · `Entity<T>`-persisted key · `ZeroMemory`). Per-tenant keying and `DestroyKey` are **net-new
  beyond it** — `DestroyKey` is the *inverse* of the template's purge-after-dependents-die (§3a), not a parameterization.

## Consequences

Adds: the `[Classified]` fact family (Abstractions — **landed, phase 1**), the
`ClassifiedFieldRegistry`/`ClassifiedPropertyBag`/`ClassifiedFieldDescriptor` foundation (Abstractions — **landed, phase
1**), the `IFieldTransform`/read-reverse seam (data core — phase 3), and a `Koan.Classification` module (transforms +
crypto — phase 2+). Opens the `StorageWritePlan` slot (+ a priority field) — the long-planned DATA-0105 phase-4 opening,
now with a second consumer. The base encrypt/mask path is adapter-universal (every store); the searchable subset is
capability-gated to the stores with a serialize hook. Off (module not referenced) is byte-identical via the two-gate
`IsEmpty` + per-type `HasClassifiedFields` model. The data core stays classification-**handling**-free — the falsifiable
conformity invariant is "**no classification handling code in `src/Koan.Data.Core`**; the only `classif`/`pii`
occurrences are generic ambient-slice doc comments" (`grep` is already non-empty because `EntityContext` cites
"classification" as an ARCH-0097 slice *example* — that is doc, not handling).

## Phased rollout (each green-ratcheted; ARCH-0079 + mutation)

0. **Priority field** on the write-stamp slot + open `StorageWritePlan` to discovered `IWriteStamp`/`IFieldTransform`
   contributors (the DATA-0105 prerequisite; behavior-preserving for the existing built-ins).
1. **Facts + sibling registry** — *(LANDED, commit `cc486781`)* the `[Classified]`/`[Pii]`… attributes (Abstractions) +
   `ClassificationCategory` + `ClassifiedFieldDescriptor` (facts-only, no `Kind`) + `ClassifiedPropertyBag` +
   `ClassifiedFieldRegistry` (Type-plane memo, two-gate `IsEmpty`+`HasClassifiedFields`). Unit-tested + mutation-checked.
2. **Crypto seam** in `Koan.Classification` — `IKeyProvider` (env-tiered, per-tenant, `DestroyKey`, `GetForDecrypt` by
   owning-tenant) + `IFieldCipher` (AES-GCM, count-aware rekey) + `IBlindIndex` + the request-scoped plaintext map.
   Borrow the `IIssuerKeyStore` *shape*; honor the §3a contract.
3. **Write-stamp + read-reverse** at the facade chokepoint — the round-trip. Write side covers **all** surfaces:
   `Upsert`/`UpsertMany`, **batch (`AppliesInBatch => true`)**, **`ConditionalReplaceAsync`**, via **clone-then-encrypt**
   (§0). Read side: reverse inside `Get`/`GetMany`/`Query`/stream **below `Data.QueryWithCount`** (plaintext residual
   filter/sort) — **exhaustive read-path coverage** or it leaks asymmetrically. **Cache L2-exclusion gate** lands here
   (not phase 5). Generic (non-PII) descriptor proof on SQLite first, then the round-trip across adapters.
4. **Searchable** (`[Pii(Searchable)]`) — `IBlindIndex` keyed-HMAC + the net-new comparand hook (relational + Mongo);
   tenant-local equality + the no-ambient-tenant fail-closed case.
5. **Leak guards** — vector embed-time exclusion at `BuildEmbeddingText` (the classification × AI guard, scrub-or-deny) +
   the messaging durable-carrier follow-on. *(Cache moved to phase 3; logs/messaging are the named follow-on, §6.)*
6. **Crypto-shred + rotation** — the erasure certificate's `DestroyKey` (the inverse-of-purge §3a contract) + the
   count-aware rotation `IKoanJob` + retiring-key retention.
7. **Masked-read projection** — compose with SEC-0004 (or its own ADR).

## Risks / open

1. **The read-reverse is net-new** and must cover *every* read path or leak asymmetrically — the single biggest new
   mechanism. (Phase 3 gate.) *(Partial precedent, review-corrected: `EntityEventRegistry.AfterLoadHandlers` exists but
   fires only on by-id reads at the static `Entity<T>` facade — NOT `Query`/stream, NOT at `RepositoryFacade`. It proves
   the danger and is why the reverse must live at the facade, below `Data.QueryWithCount`, not at the static facade.)*
2. **Ordering** must land *before* the slot opens (priority field on **both** `IWriteStamp` and `ManagedFieldDescriptor`;
   expose the internal contributor interface — phase 0).
3. **Write-plane fork cost** is contained by the clone decision (§0): the **persisted clone** is ciphertext; the
   **caller's instance stays plaintext** (so `Save()` does not corrupt it and no read-reverse-on-write is needed). The
   residual cost is the clone allocation on classified writes only. (Accepted: adapter-universality outweighs it; the
   serialize-hook alternative re-introduces N-per-adapter + Redis/Couchbase/InMemory have no hook.)
4. **Classification × AI** (embed-time exclusion) — getting it wrong leaks PHI into the vector store or silently drops
   searchability.
5. **Mongo class-map composition hazard** — the classification serialize pass (for the searchable subset) must fold
   into the *same* registrar as the identity-serializer pass (`BsonClassMap.IsClassMapRegistered` guards once), or it
   silently no-ops.
6. **Crypto-shred + rotation** are 100% greenfield with a hard correctness bar (a rotation that loses a retiring key's
   mapping silently corrupts at-rest data; the `keyId` envelope is mandatory; see the §3a retention/shred/nonce/DP
   contract — three distinct at-rest-corruption causes to defend).
7. **The message bus is an uncovered durable carrier** (review): `myMsg.Send()` serializes payloads *outside* the
   facade, so the write-stamp cannot redact a classified field in transit. Out of scope for v1, named follow-on (§6);
   until then a classified value sent over the bus travels in plaintext. Boot-report this honestly rather than implying
   coverage.
