# Defensive Publication: Dual-Identifier Entity Model with Transport ULID and Business CanonicalId

**Publication Type:** Defensive Patent Publication (Prior Art Establishment)
**Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
**Publication Date:** 2026-03-24
**Framework:** Koan Framework v0.6.3 (.NET), Flow/Canon Pillar
**Status:** Published for prior art purposes

---

## 1. Abstract

This publication discloses a dual-identifier entity model for data frameworks in which every entity simultaneously carries two distinct identifiers serving complementary purposes: (a) a ULID-based transport identifier (`Id`) minted at first materialization that provides time-sortable, URL-friendly document keys for storage and API routing, and (b) a business-derived canonical identifier (`CanonicalId`) computed deterministically from domain aggregation keys that remains stable across entity lifecycle operations including merges, splits, and re-ingestion. The system enforces bidirectional lookup between both identifiers, exposes dual-route API endpoints, and defines explicit semantics for identity resolution during Associate, Merge, and Split lifecycle steps. This design eliminates the tension between storage-optimized identifiers and business-stable identifiers by making them co-primary rather than forcing a single identifier to serve both roles.

---

## 2. Technical Problem

Modern data frameworks face a fundamental identity conflict when entities must serve both operational (transport, storage, indexing) and business (deduplication, cross-system correlation, human reference) purposes:

**Problem 1 — Single-identifier trade-off.** Existing systems force a choice between identifiers optimized for storage (UUIDs, auto-increment integers, ULIDs) and identifiers meaningful to the business domain (email addresses, SKUs, composite keys). Storage-optimized identifiers are opaque to business users. Business-meaningful identifiers are often mutable, variable in format, and unsuitable as document keys in distributed storage.

**Problem 2 — Merge/split identity loss.** When entities undergo lifecycle operations such as merging two customer records or splitting a product into variants, single-identifier systems must either (a) retire old identifiers and issue new ones, breaking all external references, or (b) maintain growing alias tables without a clear primary-versus-alias distinction. Neither approach preserves both operational continuity and business meaning.

**Problem 3 — Deduplication at ingest time.** Data pipelines that ingest records from multiple sources must detect duplicates before minting new identifiers. With single-identifier designs, deduplication requires full-table scans or external deduplication services. There is no mechanism to derive a stable lookup key from the payload itself and resolve identity at the point of first materialization.

**Problem 4 — API routing ambiguity.** REST APIs that expose entities must choose between opaque identifiers (requiring consumers to store and recall arbitrary strings) and business identifiers (which may change over time). No standard pattern provides both routes with well-defined semantics and consistency guarantees.

**Problem 5 — Index proliferation.** Systems that attempt to support multiple identifiers after the fact accumulate secondary indexes, alias tables, and mapping services, each with its own consistency model. There is no framework-level primitive that treats dual identity as a first-class concern with unified indexing and lookup semantics.

---

## 3. Detailed Technical Disclosure

### 3.1 Identity Structure

Every entity in the Flow/Canon pillar carries two co-primary identifiers as first-class fields:

```
Entity<T>
├── Id: string
│   ├── Format: ULID (Universally Unique Lexicographically Sortable Identifier)
│   ├── Length: 26 characters, Crockford Base32 encoding
│   ├── Structure: 10-byte timestamp (millisecond) + 10-byte randomness
│   ├── Properties: Time-sortable, case-insensitive, URL-safe, no special characters
│   ├── Mint point: Associate step (first materialization)
│   └── Role: Document key, storage partition key, API primary route segment
│
└── CanonicalId: string
    ├── Format: Deterministic derivation from aggregation keys
    ├── Computation: Hash-based or composite-based (configurable per entity type)
    ├── Stability: Invariant across updates; changes only on explicit Recanonize operation
    ├── Constraint: Unique index enforced at storage layer
    └── Role: Business correlation key, deduplication key, API secondary route segment
```

The two identifiers are never interchangeable. `Id` is the storage-facing identity; `CanonicalId` is the business-facing identity. Both are indexed. Both are immutable under normal operations (Id is always immutable; CanonicalId is immutable unless an explicit Recanonize lifecycle event occurs).

### 3.2 Aggregation Keys and CanonicalId Derivation

Each entity type declares its aggregation keys: the set of domain fields whose values collectively determine business identity.

```
Aggregation Key Declaration (per entity type):

[AggregationKeys("email")]                          → single-field key
[AggregationKeys("sku", "warehouse")]               → composite key
[AggregationKeys("last_name", "dob", "postal_code")]→ fuzzy composite key

CanonicalId derivation strategies:
1. Direct: CanonicalId = lowercase(trim(field))
   Example: email "Leo@Example.COM" → "leo@example.com"

2. Composite: CanonicalId = join(normalize(field1), normalize(field2), ...)
   Example: sku "ABC-123" + warehouse "US-EAST" → "abc-123::us-east"

3. Hash: CanonicalId = hash(normalize(field1) + normalize(field2) + ...)
   Example: SHA-256 truncated to 32 hex chars for high-cardinality composites

4. Custom: CanonicalId = userFunction(fields)
   Example: domain-specific normalization logic
```

The derivation is deterministic: the same input fields always produce the same CanonicalId. This determinism is what enables deduplication at ingest time without consulting an external service.

### 3.3 The Associate Step (Mint Point)

Identity resolution occurs at the Associate step, which is the first lifecycle step where an ingest payload becomes a materialized entity:

```
Associate(payload) → Entity:

Step 1: Extract aggregation key values from payload
         keys = extractKeys(payload, entityType.AggregationKeys)

Step 2: Compute candidate CanonicalId
         candidateCid = deriveCanonicalId(keys, entityType.DerivationStrategy)

Step 3: Registry lookup
         existing = canonicalRegistry.Lookup(candidateCid)

Step 4a: EXISTS path (deduplication)
          entity = loadEntity(existing.Id)
          entity = applyPayload(entity, payload)  // update, not create
          return entity  // same Id, same CanonicalId, updated attributes

Step 4b: NOT EXISTS path (new entity)
          newId = ULID.Generate()  // mint new ULID
          entity = createEntity(newId, candidateCid, payload)
          canonicalRegistry.Register(candidateCid, newId)
          return entity  // new Id, new CanonicalId, new entity
```

This design guarantees that:
- No two entities share the same CanonicalId (unique constraint).
- Re-ingestion of the same business entity reuses the existing ULID rather than minting a duplicate.
- The ULID is only minted once per unique business entity, preserving its time-ordering property as a creation timestamp.

### 3.4 Merge Operation

When two entities are determined to represent the same real-world object (e.g., duplicate customer records), the Merge operation consolidates them:

```
Merge(source: Entity A, target: Entity B) → Entity B':

Step 1: Attribute consolidation
         B' = mergeAttributes(A, B, mergeStrategy)

Step 2: CanonicalId aliasing
         canonicalRegistry.AddAlias(A.CanonicalId → B.CanonicalId)
         // A.CanonicalId now resolves to B's entity

Step 3: Id audit trail
         auditLog.Record(MergeEvent {
           sourceId: A.Id,
           targetId: B.Id,
           sourceCid: A.CanonicalId,
           targetCid: B.CanonicalId,
           timestamp: now
         })

Step 4: External reference rewriting
         externalMappings.Rewrite(A.CanonicalId → B.CanonicalId)
         // All systems referencing A.CanonicalId are notified of the alias

Step 5: Source entity tombstoning
         A.Status = Merged
         A.MergedInto = B.Id
         A.Save()  // preserved for audit, not active

Result:
- B retains its Id (ULID) and CanonicalId
- A.CanonicalId becomes a permanent alias for B.CanonicalId
- Lookup by A.CanonicalId returns B (transparent redirect)
- Lookup by A.Id returns tombstone with redirect pointer
```

### 3.5 Split Operation

When a single entity must be divided (e.g., a product line splits into variants), the Split operation creates a new entity while preserving the original:

```
Split(source: Entity A, splitSpec: SplitSpecification) → (Entity A', Entity C):

Step 1: Attribute partition
         (retainedAttrs, movedAttrs) = partitionAttributes(A, splitSpec)

Step 2: New entity creation
         C.Id = ULID.Generate()  // new ULID for the split-off entity
         C.CanonicalId = deriveCanonicalId(movedAttrs, entityType.DerivationStrategy)
         C.Attributes = movedAttrs
         canonicalRegistry.Register(C.CanonicalId, C.Id)

Step 3: Source entity update
         A'.Id = A.Id  // ULID unchanged
         A'.CanonicalId = A.CanonicalId  // business key unchanged
         A'.Attributes = retainedAttrs

Step 4: Lineage recording
         auditLog.Record(SplitEvent {
           sourceId: A.Id,
           newEntityId: C.Id,
           sourceCid: A.CanonicalId,
           newCid: C.CanonicalId,
           timestamp: now
         })

Result:
- A retains its original Id and CanonicalId
- C receives a new ULID and a new CanonicalId derived from its own aggregation keys
- Parent-child lineage is recorded in the audit log
```

### 3.6 Dual-Route API Pattern

The framework exposes two API routes for every entity type, each bound to one of the two identifiers:

```
Route 1 — Transport identity (ULID):
  GET    /api/flow/{model}/{id}
  PUT    /api/flow/{model}/{id}
  DELETE /api/flow/{model}/{id}

  Semantics: Direct document lookup by storage key.
  Performance: O(1) key lookup, no index scan.
  Use case: Internal system-to-system calls, webhooks, event references.

Route 2 — Business identity (CanonicalId):
  GET    /api/flow/{model}/by-cid/{canonicalId}
  PUT    /api/flow/{model}/by-cid/{canonicalId}

  Semantics: Resolve CanonicalId (including aliases) to entity, then operate.
  Performance: Unique index lookup + optional alias resolution.
  Use case: External integrations, human-facing references, cross-system correlation.

Response headers (both routes):
  X-Entity-Id: {ulid}
  X-Canonical-Id: {canonicalId}
  X-Canonical-Aliases: {comma-separated list of aliased CanonicalIds, if any}
```

Both routes return identical response bodies. The `by-cid` route follows alias chains transparently (a lookup by a merged entity's CanonicalId returns the merge target without client awareness of the alias).

### 3.7 Canonical Registry

The Canonical Registry is a dedicated index structure that maintains the bidirectional mapping:

```
CanonicalRegistry:
  Primary index:   CanonicalId → Id (ULID)
  Reverse index:   Id (ULID) → CanonicalId
  Alias index:     AliasCanonicalId → PrimaryCanonicalId

Operations:
  Register(canonicalId, id)           → create new mapping
  Lookup(canonicalId) → id            → resolve, following aliases
  ReverseLookup(id) → canonicalId     → reverse resolve
  AddAlias(aliasCid, primaryCid)      → record alias from merge
  ListAliases(canonicalId) → [cids]   → enumerate all aliases
  Recanonize(id, newCanonicalId)      → change business key (rare, audited)
```

The registry is co-located with the entity store (same database, same transaction boundary) to ensure atomicity: an entity is never created without its registry entry, and a registry entry never exists without its entity.

### 3.8 Recanonize Operation

In rare cases, an entity's business key must change (e.g., a customer changes their primary email). The Recanonize operation handles this explicitly:

```
Recanonize(entity, newAggregationKeys) → Entity':

Step 1: Compute new CanonicalId
         newCid = deriveCanonicalId(newAggregationKeys, entityType.DerivationStrategy)

Step 2: Conflict check
         if canonicalRegistry.Lookup(newCid) exists AND != entity.Id:
           throw CanonicalIdConflict  // another entity already owns this business key

Step 3: Alias the old CanonicalId
         canonicalRegistry.AddAlias(entity.CanonicalId → newCid)

Step 4: Update registry
         canonicalRegistry.Update(entity.Id, newCid)

Step 5: Update entity
         entity.CanonicalId = newCid
         entity.Save()

Result:
- Entity retains its ULID (no storage disruption)
- Old CanonicalId becomes an alias (no broken external references)
- New CanonicalId becomes primary
- Full audit trail recorded
```

---

## 4. Novel Aspects

The following aspects of this invention are believed to be novel, either individually or in combination:

1. **Co-primary dual identity as a framework primitive.** No existing data framework treats two identifiers as co-primary with distinct, well-defined roles (transport vs. business). Existing systems use a single primary key and optionally add secondary indexes, but the secondary indexes have no framework-level lifecycle semantics.

2. **Deterministic identity resolution at the mint point.** The Associate step computes a CanonicalId from the ingest payload before consulting storage, enabling deduplication without external services or full-table scans. The ULID is only minted when the CanonicalId is genuinely new, preventing duplicate entity creation.

3. **Alias-preserving merge with transparent redirect.** The Merge operation converts the source entity's CanonicalId into a permanent alias rather than deleting it. All subsequent lookups by the retired CanonicalId transparently resolve to the merge target. No existing framework provides this as a built-in lifecycle operation.

4. **Split with derived identity.** The Split operation mints a new ULID and derives a new CanonicalId for the split-off entity from its own aggregation keys, maintaining the invariant that every entity's CanonicalId is deterministically derivable from its business attributes.

5. **Dual-route API with identity headers.** Exposing both ULID-based and CanonicalId-based routes as first-class API patterns, with response headers that always disclose both identifiers and any active aliases, is not found in existing REST framework conventions.

6. **Recanonize as an explicit, audited lifecycle event.** Changing a business key is treated as a named lifecycle operation with conflict checking, automatic aliasing of the old key, and audit recording, rather than a silent field update.

7. **Canonical Registry co-located with entity store.** The bidirectional mapping between transport and business identifiers lives in the same transactional boundary as the entities themselves, eliminating eventual-consistency gaps that arise when identity resolution is delegated to an external service.

---

## 5. Implementation Variants

The disclosed design admits multiple implementation strategies. The following variants are disclosed to broaden the prior art coverage:

### 5.1 CanonicalId Derivation Variants

| Variant | Description | Trade-off |
|---------|-------------|-----------|
| **Direct passthrough** | CanonicalId = normalized single field (e.g., email) | Simple, human-readable; limited to single-field keys |
| **Composite concatenation** | CanonicalId = field1 + separator + field2 + ... | Human-readable for low-cardinality composites; length grows with fields |
| **Truncated hash** | CanonicalId = SHA-256(fields)[0..31] | Fixed length, collision-resistant; not human-readable |
| **Hierarchical** | CanonicalId = namespace/field1/field2 | Supports prefix queries; more complex normalization |
| **Phonetic** | CanonicalId = soundex(name) + dob | Fuzzy matching built into the key; higher collision rate |
| **External resolution** | CanonicalId = externalService.resolve(fields) | Supports ML-based entity resolution; introduces external dependency |

### 5.2 Storage Variants

| Variant | ULID Storage | CanonicalId Storage | Registry Implementation |
|---------|-------------|--------------------|-----------------------|
| **Document DB** | Document `_id` field | Unique index on `canonicalId` field | Fields within same document |
| **Relational DB** | Primary key column | Unique constraint column + alias table | Same table + join table |
| **Key-value store** | Partition key | Secondary index or separate key-space | Separate key-space with cross-references |
| **Event store** | Stream ID | Projection index | Materialized view |
| **Graph DB** | Node ID | Node property with unique constraint | Relationship edges for aliases |

### 5.3 ULID Generation Variants

| Variant | Description |
|---------|-------------|
| **Standard ULID** | Millisecond timestamp + 80-bit random (as specified in this publication) |
| **Monotonic ULID** | Same millisecond entries get incrementing random component |
| **Scoped ULID** | Embeds entity type code in randomness portion for type-aware sorting |
| **Distributed ULID** | Embeds node/shard identifier to prevent coordination |

### 5.4 Merge Strategy Variants

| Variant | Attribute Conflict Resolution |
|---------|------------------------------|
| **Target wins** | Target entity attributes take precedence |
| **Source wins** | Source entity attributes take precedence |
| **Recency wins** | Most recently updated attribute value wins |
| **Union** | Multi-valued attributes are merged (e.g., tags, addresses) |
| **Manual** | Conflicts queued for human resolution |
| **Weighted** | Each source has a trust score; highest-trust value wins |

### 5.5 API Routing Variants

| Variant | Route Pattern |
|---------|--------------|
| **Prefix-based** (as disclosed) | `/api/flow/{model}/by-cid/{cid}` |
| **Header-based** | `GET /api/flow/{model}/{identifier}` with `X-Id-Type: canonical` header |
| **Query-parameter** | `GET /api/flow/{model}?cid={cid}` |
| **Content-negotiation** | `Accept: application/vnd.koan.canonical+json` |
| **Unified with auto-detection** | `GET /api/flow/{model}/{value}` where the system detects ULID format vs. CanonicalId format |

### 5.6 Alias Resolution Variants

| Variant | Description |
|---------|-------------|
| **Eager rewriting** | All references updated at merge time |
| **Lazy redirect** | Alias resolved at query time; old references never rewritten |
| **Hybrid** | Hot paths rewritten eagerly; cold paths resolved lazily |
| **TTL-based** | Aliases expire after configurable period; clients must update |
| **Versioned** | Alias carries version counter; clients cache with version awareness |

---

## 6. Defensive Scope

This publication is intended to establish prior art and prevent any party from obtaining patent protection on the following concepts, individually or in any combination:

1. A data entity carrying both a time-sortable machine-generated identifier and a deterministically derived business identifier as co-primary fields with distinct roles.

2. A framework-level lifecycle step (Associate) that computes a business identifier from ingest payload fields before minting a machine-generated identifier, using the business identifier to detect duplicates and reuse existing machine identifiers.

3. An entity merge operation that converts the source entity's business identifier into a permanent alias for the target entity's business identifier, with transparent resolution of aliases in subsequent lookups.

4. An entity split operation that mints a new machine-generated identifier and derives a new business identifier from the split-off entity's aggregation keys.

5. Dual REST API routes for the same entity, one parameterized by machine-generated identifier and one by business identifier, with response metadata disclosing both identifiers and any active aliases.

6. A canonical registry that maintains bidirectional mappings between machine-generated and business identifiers within the same transactional boundary as the entity store.

7. A recanonize operation that changes an entity's business identifier while preserving its machine-generated identifier, automatically aliasing the old business identifier, and recording the change as an audited lifecycle event.

8. Any aggregation-key declaration mechanism (attributes, configuration, convention) that specifies which entity fields contribute to business identifier derivation, combined with pluggable derivation strategies (direct, composite, hash, hierarchical, phonetic, external).

9. The combination of ULID (or any time-sortable identifier) as transport identity with any deterministic business-key derivation as canonical identity, regardless of the specific identifier format or derivation algorithm.

10. Alias chain resolution in which a lookup by any historical business identifier for an entity (including identifiers retired by merge or recanonize operations) resolves to the current active entity.

---

## 7. Antagonist Review (Red Team Analysis)

### 7.1 Attempted Workaround: Single Identifier with External Mapping Service

**Attack:** An implementer could argue that a single identifier with an external mapping service achieves the same result as the dual-identifier model.

**Rebuttal:** The disclosed invention specifically addresses the deficiencies of external mapping services: they introduce eventual-consistency gaps (the mapping service and entity store can diverge), require additional infrastructure, and do not provide framework-level lifecycle semantics (merge, split, recanonize). The co-location of the canonical registry within the same transactional boundary as the entity store is a distinguishing characteristic. An external mapping service is explicitly disclosed as a variant (Section 5.6) to block this as a separate patent avenue.

### 7.2 Attempted Narrowing: "This Only Works with ULIDs"

**Attack:** A competitor could patent the same dual-identifier concept using UUID v7 or Snowflake IDs instead of ULIDs.

**Rebuttal:** Section 5.3 explicitly discloses multiple machine-generated identifier formats, and the defensive scope (Section 6, item 9) covers "ULID or any time-sortable identifier." The invention is not bound to a specific identifier format; the novelty lies in the dual-identifier architecture and lifecycle semantics.

### 7.3 Attempted Narrowing: "This Only Applies to REST APIs"

**Attack:** A competitor could patent the dual-lookup pattern for GraphQL, gRPC, or event-driven architectures.

**Rebuttal:** The API routing variants (Section 5.5) disclose multiple transport mechanisms. The dual-route concept is transport-agnostic: GraphQL would expose `entity(id: ULID)` and `entity(canonicalId: String)` query fields; gRPC would have `GetById` and `GetByCanonicalId` RPCs; event-driven systems would include both identifiers in event payloads. The underlying dual-identity model applies regardless of API protocol.

### 7.4 Prior Art Challenge: "Composite Keys Already Exist"

**Attack:** Relational databases have supported composite primary keys and unique constraints for decades. The CanonicalId is just a unique constraint.

**Rebuttal:** Composite keys in relational databases are storage-level constructs without lifecycle semantics. They do not provide: (a) deterministic derivation from ingest payloads as an identity resolution step, (b) alias management during merge operations, (c) transparent redirect across retired identifiers, or (d) framework-level split semantics that derive new composite keys. The disclosed invention is a framework-level lifecycle model, not a storage indexing technique.

### 7.5 Prior Art Challenge: "MDM Systems Do Entity Resolution"

**Attack:** Master Data Management (MDM) systems such as Informatica MDM, IBM InfoSphere, and Reltio already perform entity resolution with match/merge capabilities.

**Rebuttal:** MDM systems perform entity resolution as an external, often batch-oriented process separate from the entity storage layer. The disclosed invention embeds identity resolution into the entity materialization lifecycle (the Associate step) as a synchronous, transactional operation. MDM systems also do not prescribe a dual-identifier model where both identifiers are co-primary framework primitives with distinct roles. MDM match/merge is a data quality operation; the disclosed merge is an identity lifecycle operation with alias preservation.

### 7.6 Attempted Workaround: "Use Natural Keys as Primary Keys"

**Attack:** An implementer could argue that using the business key as the primary key (e.g., email as the primary key) and adding a ULID as a secondary field achieves the same result with inverted roles.

**Rebuttal:** Using natural keys as primary keys is well-known to cause problems with key mutability (email changes), storage performance (variable-length keys as partition keys), and cross-system portability. The disclosed invention specifically designates the machine-generated ULID as the storage primary key for performance and immutability, while giving the business key its own lifecycle (aliasing, recanonization). Inverting the roles does not achieve the same lifecycle semantics.

### 7.7 Completeness Check: Uncovered Angles

The following additional angles are disclosed to ensure comprehensive coverage:

- **Multi-tenant isolation:** In multi-tenant systems, the CanonicalId derivation may include a tenant identifier as an implicit aggregation key, ensuring that the same business key in different tenants produces different CanonicalIds. The ULID remains globally unique regardless.

- **Temporal identity:** An entity's CanonicalId may carry a temporal component (e.g., fiscal year + account number) for time-scoped business identity while the ULID remains permanently unique across time scopes.

- **Federated identity:** In distributed systems, multiple nodes may independently compute the same CanonicalId from the same business data and use it to detect cross-node duplicates during federation/sync, without prior coordination. ULIDs minted at different nodes remain distinct until merge reconciliation.

- **Batch ingest optimization:** The Associate step can be batched: compute all CanonicalIds for an ingest batch, perform a bulk registry lookup, partition into EXISTS and NOT EXISTS groups, then bulk-mint ULIDs only for the NOT EXISTS group.

- **Event sourcing integration:** In event-sourced systems, the ULID serves as the aggregate stream ID while the CanonicalId is a projection-level index. Events reference the ULID; read models expose both identifiers.

---

**End of Defensive Publication**

*This document is published to establish prior art and is placed in the public domain for the purpose of preventing the patenting of the disclosed concepts by any party. The inventor retains all rights to implement and commercially exploit the disclosed invention.*
