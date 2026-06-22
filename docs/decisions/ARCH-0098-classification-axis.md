# ARCH-0098: The data-classification axis — field-transform seam, layered policy, and the crypto seam

- Status: Proposed
- Date: 2026-06-22
- Siblings: ARCH-0095 (tenancy — the other Facet-3 axis), DATA-0105 (the contributor umbrella), ARCH-0084 (capabilities), ARCH-0094 (Adapter Forge — the KMS external-infra seam)
- Grounding: empirical side-discovery (workflow `wf_c4cb6674-564`, 3 source-grounded lenses) + `docs/architecture/tenancy-design.md` §5 (the settled layered-policy design) + the managed-field seam (DATA-0105 §3b, shipped)

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

- **Write** — a new `IFieldTransform.ApplyOnWrite(entity)` joins the per-type `StorageWritePlan` (the same closed
  list that holds `IdentityWriteStamp` + `TimestampWriteStamp`), applied at `RepositoryFacade.Upsert/UpsertMany/batch`
  via `_writePlan.ApplyAll(model)`. The entity holds **ciphertext** by the time *any* adapter serializes it.
- **Read** — a **net-new** `IFieldTransform.ApplyOnRead(entity)` reverse applicator runs on the facade's
  `Get`/`GetMany`/`Query`/stream results, **after** the adapter materializes the entity and **before** it leaves the
  chokepoint, restoring plaintext into a **request-scoped plaintext map** (an AsyncLocal, the classification analog of
  `ManagedFieldWriteScope`; never the distributed cache — `tenancy-design.md` §5f).

**Why the write-stamp plane, not the serialize plane** (the pivotal fork): the write-stamp is the *single
adapter-agnostic* point, so **all 8 data adapters** — including Redis / Couchbase / InMemory, which have **no Koan
serialize hook at all** — get field-encrypt/tokenize for free. The serialize plane is *not* one shared point: it is
one shared seam for the relational trio (`ComparableScalarEncoding.Apply`) **+ N separate** hooks (Json's own
resolver, Redis raw `JsonConvert`, Couchbase SDK, Mongo `BsonClassMap`, InMemory none). The write-stamp also makes
the design's request-scoped-plaintext model fall out naturally (the in-memory entity is ciphertext post-stamp) and
**accidentally fixes the cache-value leak** (`CachedRepository` wraps outside the facade and caches the *value* — if
that value is ciphertext, no plaintext reaches L2).

> **Reserved exception — searchable equality.** For `[Pii(Searchable)]` blind-equality, a deterministic
> classification *comparand* transform rides the existing write↔comparand coupling (`ComparableScalarEncoding.EncodeComparand`
> relational / the field's own serializer on Mongo) so a keyed-HMAC equality pushes down. LIKE/range stay honestly
> denied. This is the *only* place the serialize/comparand plane is used.

### 1. A sibling `ClassifiedFieldRegistry` + `IFieldTransform` (mirror `IWriteStamp`/`TimestampPropertyBag`)

A property-keyed, round-trip contributor, peer to `ManagedFieldRegistry` and `IWriteStamp` under DATA-0105 §0:

```
FieldTransformDescriptor(
    PropertyInfo Property, ClassificationCategory Category, TransformKind Kind,   // descriptor-as-data
    Func<object,object?> Getter, Action<object,object?> Setter)                   // Expression-compiled once per type
ClassifiedPropertyBag(Type)   // scan-once like TimestampPropertyBag: GetProperties().Where(has-attr)
ClassifiedFieldRegistry       // static boot index: IsEmpty off-gate, Type-plane memo (copy ManagedFieldRegistry mechanics)
```

It copies the managed-field registry's *mechanics* (static boot registry · Type-plane memo · `IsEmpty` volatile
off-gate making the off path byte-identical · descriptor-not-callback · ARCH-0084 fail-closed) but is its **own**
registry — the managed-field's no-property/one-way descriptor has no slot for the property handle, the read reverse,
or per-property keying.

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

### 6. Value-level leak guards (no managed-field analog)

- **Cache** — the cached *value* must be ciphertext (the write-stamp gives this free, *iff* the read-reverse runs
  strictly after cache materialization), or classified entities are excluded from L2.
- **Vector / AI** — `[Phi]` is **excluded from embedding at embed-build time** unless policy opts in (`allowEmbedding`
  + a scrub-and-embed strategy) — a tokenized value is semantically meaningless to embed (the classification × AI
  tension). This is a *different* guard from the tenant `FailClosedIfManagedScoped` (embed-time exclusion, throwing
  `CapabilityDeniedException`, not isolation fail-close).
- **Raw / RLS** — out of scope for the transform; documented.
- **Logs / audit / durable carriers** — redact-in-logs: strip/blind classified fields before durable storage.

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
- **The write↔comparand coupling** that makes managed-field equality push down makes classification blind-equality push
  down — one mechanism, two axes.
- **The crypto seam is the `IIssuerKeyStore` template** parameterized by tenant + a `DestroyKey` verb — a parameterized
  pattern, not a new one.

## Consequences

Adds: the `[Classified]` fact family (Abstractions), the `ClassifiedFieldRegistry`/`IFieldTransform`/read-reverse seam
(data core), and a `Koan.Classification` module (transforms + crypto). Opens the `StorageWritePlan` slot (+ a priority
field) — the long-planned DATA-0105 phase-4 opening, now with a second consumer. The base encrypt/mask path is
adapter-universal (every store); the searchable subset is capability-gated to the stores with a serialize hook. Off
(module not referenced / no `[Classified]` field) is byte-identical via the `IsEmpty` gate. The data core stays
classification-agnostic (the conformity invariant: `grep -i "pii\|classif" src/Koan.Data.Core` returns nothing).

## Phased rollout (each green-ratcheted; ARCH-0079 + mutation)

0. **Priority field** on the write-stamp slot + open `StorageWritePlan` to discovered `IWriteStamp`/`IFieldTransform`
   contributors (the DATA-0105 prerequisite; behavior-preserving for the existing built-ins).
1. **Facts + sibling registry** — the `[Classified]`/`[Pii]`… attributes (Abstractions) + `ClassifiedFieldRegistry` +
   `ClassifiedPropertyBag` + `FieldTransformDescriptor` (Type-plane memo, IsEmpty gate). Unit-tested.
2. **Crypto seam** in `Koan.Classification` — `IKeyProvider` (env-tiered, per-tenant, `DestroyKey`) + `IFieldCipher`
   (AES-GCM) + the request-scoped plaintext map. Mirror `IIssuerKeyStore`.
3. **Write-stamp + read-reverse** at the facade chokepoint — the round-trip. **Exhaustive read-path coverage** (Get /
   GetMany / Query / stream) or plaintext-vs-ciphertext leaks asymmetrically. Generic (non-PII) descriptor proof on
   SQLite first, then the round-trip across adapters (the write-stamp path is adapter-universal).
4. **Searchable** (`[Pii(Searchable)]`) — `IBlindIndex` + the comparand transform (relational + Mongo).
5. **Leak guards** — vector embed-time exclusion (the classification × AI guard) + cache-value verification + log
   redaction.
6. **Crypto-shred + rotation** — the erasure certificate's `DestroyKey` + the rotation `IKoanJob`.
7. **Masked-read projection** — compose with SEC-0004 (or its own ADR).

## Risks / open

1. **The read-reverse is net-new** (no AfterLoad precedent) and must cover *every* read path or leak asymmetrically —
   the single biggest new mechanism. (Phase 3 gate.)
2. **Ordering** must land *before* the slot opens (priority field; phase 0).
3. **Write-plane fork cost:** the in-memory entity is ciphertext post-stamp — every read path must reverse, and code
   touching the entity between stamp and persist sees ciphertext. (Accepted: the adapter-universality + cache-leak fix
   outweigh it; the serialize-hook alternative re-introduces N-per-adapter + Redis/Couchbase/InMemory have no hook.)
4. **Classification × AI** (embed-time exclusion) — getting it wrong leaks PHI into the vector store or silently drops
   searchability.
5. **Mongo class-map composition hazard** — the classification serialize pass (for the searchable subset) must fold
   into the *same* registrar as the identity-serializer pass (`BsonClassMap.IsClassMapRegistered` guards once), or it
   silently no-ops.
6. **Crypto-shred + rotation** are 100% greenfield with a hard correctness bar (a rotation that loses a retiring key's
   mapping silently corrupts at-rest data; the `keyId` envelope is mandatory).
