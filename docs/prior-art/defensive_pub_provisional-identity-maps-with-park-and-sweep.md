# Defensive Publication: Provisional Identity Maps with Alias Compression and Park-and-Sweep Recovery

**Publication Type:** Defensive Patent Publication (Prior Art Establishment)
**Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
**Date of Disclosure:** 2026-03-24
**Framework:** Koan Framework v0.6.3 (.NET), Flow/Canon Pillar
**Status:** Published for prior art purposes — not patent-pending

---

## 1. Title and Abstract

**Title:** Provisional Identity Maps with Alias Compression and Park-and-Sweep Recovery for Out-of-Order Data Ingestion Pipelines

**Abstract:**

A method and system for resolving entity identity in data ingestion pipelines where records arrive out of order from heterogeneous source systems. The system introduces three coordinated mechanisms: (1) provisional identity maps that assign temporary canonical identifiers (ULIDs with configurable TTL) to incoming records that lack a resolved identity, promoting them to confirmed status when authoritative records arrive; (2) alias compression that rewrites identity mapping chains to always point directly to the terminal canonical identifier, enforcing a hard cap of chain length one to eliminate transitive lookups; and (3) a park-and-sweep recovery pattern where records that fail identity resolution due to missing dependencies are placed in a visible, queryable side-set with structured rejection reports, then periodically re-evaluated by a background sweeper that re-injects resolved records into the pipeline or escalates them to dead-letter storage after exceeding a retry threshold. Together, these mechanisms allow a data integration framework to process records optimistically upon arrival rather than blocking on resolution order, while maintaining eventual consistency guarantees and full observability over unresolved records.

---

## 2. Technical Problem

### 2.1 Core Problem: Ordering Assumptions in Data Integration

Modern data integration pipelines ingest records from multiple source systems (CRMs, ERPs, SaaS APIs, IoT streams, internal databases) through adapters that operate independently. A fundamental assumption in most existing systems is that records arrive in a processable order — that is, when record B references record A, record A has already been ingested and resolved. This assumption fails routinely in practice.

**Specific failure scenarios:**

1. **Cross-system reference lag.** System Alpha emits an order record referencing a customer entity that has not yet been synced from System Beta. The pipeline either rejects the order (data loss), blocks until the customer arrives (throughput collapse), or creates a dangling reference (integrity violation).

2. **Adapter clock skew.** Two adapters polling the same source system with different schedules produce interleaved records. A child record may arrive minutes or hours before its parent because the adapter that captured it ran first.

3. **Replay and backfill overlap.** During historical backfill operations, records from a bulk export interleave with real-time CDC (Change Data Capture) events. The same logical entity appears under different external identifiers across the two streams, and the merge order is non-deterministic.

4. **Entity merges across systems.** Two source systems each have their own identifier for the same real-world entity. When a merge event arrives, all prior references under the old identifier must be rewritten — but downstream consumers may have already cached the old mapping.

5. **Transient dependency unavailability.** A lookup service (e.g., a classification taxonomy or reference data API) is temporarily unavailable. Records requiring that lookup cannot be resolved, but should not be permanently lost.

### 2.2 Why Existing Approaches Are Insufficient

| Approach | Limitation |
|----------|-----------|
| **CDC frameworks (Debezium, Maxwell)** | Assume ordered delivery within a partition. Cross-partition and cross-source ordering is left to the consumer. No identity resolution layer. |
| **Stream processing (Kafka Streams, Flink)** | Provide windowing and join semantics, but require the developer to implement identity resolution logic. No built-in provisional identity concept. Windowed joins discard late-arriving records outside the window. |
| **ETL platforms (Fivetran, Airbyte)** | Connector-level deduplication. Identity resolution is per-connector, not cross-system. No mechanism for provisional identifiers that get promoted. |
| **Master Data Management (MDM)** | Matching and merging capabilities exist, but MDM systems are heavyweight, batch-oriented, and operate as separate platforms rather than inline pipeline components. They do not provide TTL-based provisional identifiers or inline park-and-sweep. |
| **Event sourcing / CQRS** | Handles append-only event ordering within a single aggregate but does not address cross-aggregate, cross-system identity resolution for value objects arriving out of order. |
| **Idempotency keys** | Prevent duplicate processing of the same record but do not resolve the identity of different records referring to the same real-world entity across systems. |

No existing framework or system combines inline provisional identity assignment, single-hop alias compression, and a visible park-and-sweep recovery mechanism within the data ingestion pipeline itself.

---

## 3. Solution Description

### 3.1 System Overview

The invention introduces three coordinated subsystems within a data ingestion pipeline framework:

```
┌─────────────────────────────────────────────────────────────────┐
│                      Ingestion Pipeline                         │
│                                                                 │
│  ┌──────────┐    ┌──────────────┐    ┌───────────────────────┐  │
│  │  Adapter  │───▶│  Identity    │───▶│  Downstream           │  │
│  │  (source) │    │  Resolution  │    │  Processing           │  │
│  └──────────┘    │  Engine      │    │  (confirmed or        │  │
│                  │              │    │   provisional ID)      │  │
│                  └──────┬───────┘    └───────────────────────┘  │
│                         │                                       │
│                         │ MISS + unresolvable                   │
│                         ▼                                       │
│                  ┌──────────────┐    ┌───────────────────────┐  │
│                  │  Park Set    │◀──▶│  Sweep Service        │  │
│                  │  (per entity │    │  (background,         │  │
│                  │   type)      │    │   periodic)           │  │
│                  └──────────────┘    └───────────────────────┘  │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  IdentityMap Store (per entityType)                      │   │
│  │  ┌────────────────────────────────────────────────────┐  │   │
│  │  │ Key: {entityType, system, adapter, externalId}     │  │   │
│  │  │ Value: canonicalId, status, createdAt, updatedAt,  │  │   │
│  │  │        provenance, ttl                             │  │   │
│  │  └────────────────────────────────────────────────────┘  │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Alias Compression (inline, on merge)                    │   │
│  │  Invariant: no mapping chain exceeds length 1            │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Provisional GC (background, TTL-based)                  │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 IdentityMap Entity

The IdentityMap is the central data structure. Each entry represents a mapping from an external identifier in a specific source system to a canonical identifier within the pipeline.

**Schema:**

| Field | Type | Description |
|-------|------|-------------|
| `entityType` | string | The logical entity type (e.g., `Customer`, `Order`, `Product`) |
| `system` | string | The source system identifier (e.g., `salesforce`, `shopify`) |
| `adapter` | string | The specific adapter instance (e.g., `salesforce-us-west`, `shopify-eu`) |
| `externalId` | string | The identifier as known in the source system |
| `canonicalId` | ULID | The framework-assigned canonical identifier |
| `status` | enum | `provisional` or `confirmed` |
| `createdAt` | DateTimeOffset | When the mapping was first created |
| `updatedAt` | DateTimeOffset | When the mapping was last modified |
| `provenance` | string | Origin metadata (adapter run ID, batch ID, event offset) |
| `ttl` | TimeSpan | Time-to-live for provisional entries (default: 2 days) |

**Composite key:** `(entityType, system, adapter, externalId)` — uniquely identifies a mapping.

**Invariants:**
- A confirmed mapping is never downgraded to provisional.
- A provisional mapping that is not confirmed within its TTL is eligible for garbage collection.
- The `canonicalId` field always points to a terminal identifier (never to another mapping entry).

### 3.3 Identity Resolution Flow

The resolution flow executes inline during record ingestion, as a pipeline stage:

```
PROCEDURE ResolveIdentity(record, entityType, system, adapter, externalId):

  1. LOOKUP IdentityMap WHERE
       entityType = record.entityType
       AND system = record.system
       AND adapter = record.adapter
       AND externalId = record.externalId

  2. IF HIT:
       a. RETURN mapping.canonicalId
       b. (downstream processing uses this ID)

  3. IF MISS:
       a. newId = ULID.Generate()          // Monotonic, sortable, unique
       b. CREATE IdentityMap entry:
            entityType  = record.entityType
            system      = record.system
            adapter     = record.adapter
            externalId  = record.externalId
            canonicalId = newId
            status      = provisional
            createdAt   = now
            updatedAt   = now
            provenance  = record.sourceMetadata
            ttl         = configuration.defaultTtl  // default 2 days
       c. RETURN newId
       d. (downstream processing proceeds with provisional ID)

  4. LATER, when authoritative record arrives:
       a. LOOKUP existing provisional mapping
       b. IF found:
            UPDATE status = confirmed
            UPDATE updatedAt = now
            UPDATE provenance = authoritativeRecord.sourceMetadata
       c. IF not found (direct arrival):
            CREATE confirmed mapping directly
```

**Key design decision:** Processing continues immediately with a provisional ID rather than blocking. This converts a synchronous ordering dependency into an eventually-consistent resolution, allowing the pipeline to maintain throughput regardless of arrival order.

**ULID choice rationale:** ULIDs are lexicographically sortable (unlike UUIDv4), contain an embedded timestamp (aiding TTL enforcement and debugging), and are globally unique without coordination. They are used here as provisional identifiers rather than GUIDs to enable time-range queries over provisional mappings (e.g., "find all provisionals created in the last hour").

### 3.4 Alias Compression

When two external identifiers are discovered to refer to the same real-world entity (a merge event), the system must update all mappings. Naive approaches create chains: ExternalA -> Canonical1, Canonical1 -> Canonical2. Traversing chains adds latency proportional to chain length and creates fragile indirection.

**Algorithm:**

```
PROCEDURE CompressAliases(survivorCanonicalId, mergedCanonicalId):

  // survivorCanonicalId is the canonical ID that persists after merge
  // mergedCanonicalId is the canonical ID being retired

  1. FIND all IdentityMap entries WHERE canonicalId = mergedCanonicalId

  2. FOR EACH entry:
       a. UPDATE entry.canonicalId = survivorCanonicalId   // Direct rewrite
       b. UPDATE entry.updatedAt = now
       c. UPDATE entry.provenance = "alias-compression:{mergeEventId}"

  3. (No intermediate chain is ever stored; all entries point to terminal ID)
```

**Hard invariant:** The maximum alias chain length is 1. Every mapping entry points directly to the terminal canonical identifier. This invariant is enforced at write time — there is no deferred chain resolution or lazy compression.

**Merge conflict resolution:** When both sides of a merge have confirmed mappings, the survivor is determined by a configurable policy:
- Default: the older `createdAt` wins (first-seen authority).
- Alternative: explicit merge directive from source system takes precedence.
- Alternative: higher-cardinality side wins (the canonical ID with more mappings pointing to it).

### 3.5 Park-and-Sweep Recovery

Records that cannot complete identity resolution due to missing dependencies are not rejected or discarded. Instead, they are moved to a park set — a visible, queryable side-set scoped by entity type.

**Park operation:**

```
PROCEDURE ParkRecord(record, entityType, reason, missingDependencies):

  1. MOVE record to parallel set: {entityType}#flow.park

  2. ATTACH RejectionReport:
       reason              = reason          // e.g., "missing_parent", "lookup_unavailable"
       missingDependencies = [...]           // list of specific unresolved references
       timestamp           = now
       retryCount          = 0
       lastRetryAt         = null
       originAdapter       = record.adapter
       originBatch         = record.batchId

  3. EMIT observability event: flow.record.parked {entityType, reason, externalId}
```

**Sweep operation (background, periodic):**

```
PROCEDURE Sweep(entityType, configuration):

  // Runs on configurable interval (default: 60 seconds)

  1. FETCH all records from {entityType}#flow.park

  2. FOR EACH parked record:
       a. CHECK if missingDependencies are now available
          (query IdentityMap for each dependency)

       b. IF all dependencies resolved:
            REMOVE from park set
            RE-INJECT into pipeline at identity resolution stage
            EMIT event: flow.record.unparked {entityType, externalId, retryCount}

       c. IF dependencies still missing:
            INCREMENT retryCount
            UPDATE lastRetryAt = now
            EMIT event: flow.record.sweep_miss {entityType, externalId, retryCount}

       d. IF retryCount > configuration.maxRetries (default: 48):
            MOVE to dead-letter: {entityType}#flow.dead
            EMIT event: flow.record.dead_lettered {entityType, externalId, reason}
            (dead-lettered records require manual intervention or policy-driven action)

  3. EMIT sweep summary event:
       flow.sweep.completed {entityType, evaluated, unparked, still_parked, dead_lettered}
```

**Visibility guarantees:**
- Parked records are queryable at any time. An operator can inspect the park set, read rejection reports, and understand exactly why each record is parked and what dependencies are missing.
- The park set is scoped per entity type (`{entityType}#flow.park`), preventing cross-entity interference and enabling entity-type-specific retry policies.
- Dead-letter records are preserved indefinitely (or per retention policy) for forensic analysis.

### 3.6 Provisional Garbage Collection

A background job periodically scans for expired provisional mappings:

```
PROCEDURE ProvisionalGC(configuration):

  // Runs on configurable interval (default: 6 hours)

  1. FIND all IdentityMap entries WHERE
       status = provisional
       AND (now - createdAt) > ttl

  2. FOR EACH expired entry:
       a. CHECK if any downstream records reference this canonicalId
       b. IF referenced:
            EXTEND ttl by configuration.extensionPeriod (default: 1 day)
            EMIT event: flow.provisional.extended {canonicalId, newExpiry}
       c. IF not referenced:
            DELETE entry (or MARK as gc_collected for audit)
            EMIT event: flow.provisional.collected {canonicalId, age}

  3. EMIT GC summary event:
       flow.provisional_gc.completed {scanned, extended, collected}
```

**Safety mechanism:** The GC checks for downstream references before deleting a provisional mapping. If any processed record still points to the provisional canonical ID, the TTL is extended rather than the mapping deleted. This prevents orphaning downstream data that was processed optimistically.

---

## 4. Novel Aspects

The following aspects, individually and in combination, constitute the novel contribution of this invention:

### 4.1 Inline Provisional Identity Assignment with Optimistic Processing

**What is novel:** The system assigns a provisional canonical identifier (ULID) immediately at ingestion time and allows downstream processing to continue with that provisional identifier, rather than blocking until identity is authoritatively resolved. The provisional status is tracked as a first-class attribute of the identity mapping, with explicit lifecycle transitions (provisional -> confirmed) and TTL-based expiry.

**Distinction from prior art:** Existing systems either (a) reject records with unresolved identities, (b) buffer them until resolution completes, or (c) assign permanent identifiers without distinguishing provisional from confirmed status. The explicit provisional/confirmed lifecycle with TTL enables the system to be optimistic about identity while maintaining a cleanup path for identities that are never confirmed.

### 4.2 Single-Hop Alias Compression with Hard Chain-Length Cap

**What is novel:** When identity merge events occur, all affected mappings are rewritten in place to point directly to the surviving canonical identifier. The system enforces a hard invariant that no alias chain exceeds length 1 — there are never transitive mappings where entry A points to entry B which points to the terminal ID. This is enforced at write time, not as a deferred optimization.

**Distinction from prior art:** MDM systems and identity graphs typically allow chains or graphs of identity relationships that are resolved at query time. DNS-style alias resolution (CNAME chains) explicitly allows multi-hop chains. The hard cap of chain length 1, enforced at write time via in-place rewriting, eliminates an entire class of consistency bugs and lookup performance degradation.

### 4.3 Park-and-Sweep as a Visible, Entity-Scoped Recovery Mechanism

**What is novel:** Records failing identity resolution are placed in a named, queryable side-set (`{entityType}#flow.park`) with structured rejection reports detailing the specific missing dependencies. A background sweeper periodically re-evaluates parked records against current state and re-injects them into the pipeline when dependencies become available. The park set is a first-class operational concept, not a hidden retry queue.

**Distinction from prior art:** Dead-letter queues (DLQs) in message brokers store failed messages but do not periodically re-evaluate them against changing state. Retry mechanisms in stream processors (e.g., Kafka consumer retries) operate at the message delivery level, not at the semantic identity resolution level. No existing system provides entity-type-scoped parking with structured dependency tracking and automatic re-injection upon dependency availability.

### 4.4 Coordinated Three-Mechanism System

**What is novel:** The three mechanisms (provisional identity maps, alias compression, park-and-sweep) operate as a coordinated system with shared state (the IdentityMap store) and complementary lifecycle semantics:
- Provisional maps enable optimistic processing.
- Alias compression maintains lookup efficiency as merges occur.
- Park-and-sweep handles records that cannot even reach the provisional stage (missing dependencies).
- Provisional GC cleans up identities that were never confirmed, closing the lifecycle.

No existing framework combines these three mechanisms into a single coherent identity resolution subsystem within a data ingestion pipeline.

---

## 5. Implementation Guidance

### 5.1 Framework Integration Point

Within the Koan Framework's Flow/Canon pillar, the identity resolution engine is registered as a pipeline stage. Using Koan's "Reference = Intent" pattern, adding a reference to the `Koan.Flow.Canon` package enables the identity resolution pipeline stage automatically via `KoanAutoRegistrar`.

### 5.2 Storage Backend Independence

The IdentityMap store is implemented behind a provider-agnostic interface, consistent with Koan's multi-provider pattern:

```csharp
public interface IIdentityMapStore
{
    Task<IdentityMapping?> Lookup(
        string entityType, string system, string adapter, string externalId,
        CancellationToken ct = default);

    Task Upsert(IdentityMapping mapping, CancellationToken ct = default);

    Task<int> CompressAliases(
        string entityType, Ulid survivorId, Ulid mergedId,
        CancellationToken ct = default);

    Task<IReadOnlyList<IdentityMapping>> FindExpiredProvisionals(
        TimeSpan ttl, CancellationToken ct = default);

    Task<bool> HasDownstreamReferences(Ulid canonicalId, CancellationToken ct = default);
}
```

Concrete implementations exist for SQL (PostgreSQL, SQL Server), document stores (MongoDB), and in-memory (testing). The store selection follows existing Koan provider resolution: the configured connection string determines which provider is used.

### 5.3 Park Set Storage

Park sets are stored as parallel collections/tables scoped by entity type. The naming convention `{EntityType}#flow.park` maps to:
- **SQL:** Table `flow_park_{entity_type}` with the record payload, rejection report columns, and indexes on `retryCount` and `lastRetryAt`.
- **Document store:** Collection `flow.park.{entityType}` with embedded rejection report.
- **In-memory:** `ConcurrentDictionary<string, ParkSet>` keyed by entity type.

### 5.4 Configuration Surface

```json
{
  "Koan": {
    "Flow": {
      "Canon": {
        "DefaultProvisionalTtl": "2.00:00:00",
        "TtlExtensionPeriod": "1.00:00:00",
        "SweepInterval": "00:01:00",
        "MaxSweepRetries": 48,
        "GcInterval": "06:00:00",
        "MergePolicy": "first-seen",
        "EnableObservabilityEvents": true
      }
    }
  }
}
```

All values are overridable per entity type via `Koan:Flow:Canon:{EntityType}:*` keys.

### 5.5 Observability Integration

All lifecycle events (parked, unparked, dead-lettered, provisional-extended, provisional-collected, sweep-completed, gc-completed) are emitted as structured events compatible with Koan's self-reporting infrastructure. These events surface in boot reports and operational dashboards, providing full visibility into the identity resolution pipeline's health.

### 5.6 Concurrency Considerations

- **IdentityMap upsert:** Optimistic concurrency via `updatedAt` version check. If a concurrent writer has modified the mapping, the operation retries with the latest state.
- **Alias compression:** Executed within a transaction (or atomic bulk update for document stores) to prevent partial rewrites.
- **Sweep:** Uses distributed locking (one sweeper per entity type per deployment) to prevent duplicate re-injection. Parked records are claimed via atomic compare-and-swap on a `claimedBy` field.
- **Provisional GC:** Idempotent. Multiple concurrent GC runs produce the same result.

---

## 6. Claims (Defensive)

The following claims are published defensively to establish prior art and prevent others from patenting these techniques. They are not asserted as exclusive rights.

**Claim 1.** A computer-implemented method for resolving entity identity in a data ingestion pipeline comprising: (a) receiving a data record with an external identifier from a source system and adapter; (b) looking up the external identifier in an identity map keyed by entity type, source system, adapter, and external identifier; (c) upon a cache miss, generating a new universally unique lexicographically sortable identifier (ULID) as a provisional canonical identifier; (d) storing a mapping entry with provisional status and a configurable time-to-live (TTL); (e) continuing downstream processing with the provisional canonical identifier without blocking; and (f) upon subsequent arrival of an authoritative record, promoting the mapping entry status from provisional to confirmed.

**Claim 2.** The method of Claim 1 further comprising: upon occurrence of an entity merge event identifying a surviving canonical identifier and a merged canonical identifier, rewriting all identity map entries that reference the merged canonical identifier to directly reference the surviving canonical identifier, thereby enforcing a hard constraint that no alias chain exceeds a length of one hop.

**Claim 3.** The method of Claim 1 further comprising: (a) upon failure to resolve identity for a record due to missing dependencies, placing the record in an entity-type-scoped parallel set (park set) with an attached rejection report specifying the missing dependencies; (b) executing a background sweep process at configurable intervals that evaluates each parked record's missing dependencies against current identity map state; (c) re-injecting parked records into the pipeline when all dependencies are resolved; and (d) escalating parked records to a dead-letter set when a retry count exceeds a configurable maximum.

**Claim 4.** The method of Claim 1 further comprising a background garbage collection process that: (a) identifies provisional identity map entries whose age exceeds their configured TTL; (b) checks whether downstream processed records reference the provisional canonical identifier; (c) extends the TTL if downstream references exist; and (d) removes the mapping entry if no downstream references exist.

**Claim 5.** A system implementing the methods of Claims 1 through 4 as coordinated subsystems sharing a common identity map store, wherein provisional identity assignment, alias compression, park-and-sweep recovery, and provisional garbage collection operate as complementary lifecycle stages within a single data ingestion pipeline framework.

**Claim 6.** The method of Claim 2 wherein the merge conflict resolution policy is configurable and selected from: first-seen authority (oldest mapping wins), explicit source system directive, or higher-cardinality selection (the canonical identifier with the most existing mappings persists).

**Claim 7.** The method of Claim 3 wherein the park set is queryable as a first-class operational entity, exposing rejection reports with structured fields including reason classification, missing dependency list, retry count, and timestamp history, enabling operational inspection of pipeline resolution state without accessing internal queues or logs.

---

## 7. Antagonist Analysis

This section subjects each claimed novel aspect to adversarial scrutiny, identifying the strongest arguments against novelty and responding to each.

### Challenge 1: "Provisional identifiers are just temporary IDs — every staging table uses them."

**Strongest form of the argument:** ETL systems routinely assign surrogate keys during staging. A staging table row with an auto-incremented ID that later gets mapped to a production ID is functionally equivalent to a provisional-to-confirmed lifecycle.

**Response:** The distinction is structural, not superficial. Staging surrogate keys are (a) scoped to a single load batch, (b) not used for downstream processing (they are replaced before data leaves staging), and (c) not shared across systems or adapters. Provisional canonical IDs in this invention are (a) globally unique (ULIDs), (b) immediately used for downstream processing before confirmation, (c) shared across the entire pipeline and potentially across multiple adapter streams, and (d) governed by an explicit TTL-based lifecycle with garbage collection. The provisional ID is not a staging artifact — it is a first-class identifier that participates in production processing and is later promoted, not replaced.

### Challenge 2: "Alias compression is just DNS CNAME flattening or pointer compression."

**Strongest form of the argument:** DNS CNAME flattening (as implemented by Cloudflare and others) rewrites CNAME chains to direct A records. Graph databases perform pointer compression during compaction. The concept of eliminating indirection chains is well-established.

**Response:** The technique of chain elimination is indeed known in other domains. The novel contribution is not chain compression in isolation, but (a) applying it to identity maps in a data ingestion context where merge events trigger compression, (b) enforcing it as a hard write-time invariant (chain length capped at exactly 1, not as a deferred optimization), and (c) integrating it with the provisional identity lifecycle such that compression operates correctly on both provisional and confirmed mappings. DNS CNAME flattening is a read-time optimization; this is a write-time invariant. Graph compaction is a background maintenance operation; this is an inline transactional guarantee.

### Challenge 3: "Park-and-sweep is just a retry queue with extra metadata."

**Strongest form of the argument:** Message brokers (RabbitMQ, SQS) support delayed retry with exponential backoff. Temporal/Durable Functions provide durable retry with state. Adding a reason field and a dependency list to a retry queue does not constitute a novel mechanism.

**Response:** The distinction lies in what triggers re-evaluation and what is visible. A retry queue re-attempts delivery after a time delay — it does not evaluate whether the specific missing dependencies have been resolved. The sweep process performs a semantic check: it queries the IdentityMap for each missing dependency and only re-injects when all are present. This is dependency-aware recovery, not time-delayed retry. Furthermore, the park set is entity-type-scoped and queryable as a first-class operational entity with structured rejection reports — it is designed for observability and operational inspection, not just mechanical retry. Temporal/Durable Functions could implement similar logic, but they are general-purpose workflow engines; the contribution here is a domain-specific pattern integrated into an identity resolution pipeline.

### Challenge 4: "The combination is obvious — anyone building a data pipeline would arrive at these mechanisms."

**Strongest form of the argument:** Each individual mechanism (temporary IDs, alias rewriting, retry queues) is known. Combining them is an obvious engineering choice for anyone facing out-of-order ingestion.

**Response:** If the combination were obvious, it would exist in at least one of the major data integration frameworks (Debezium, Kafka Connect, Fivetran, Airbyte, dbt, Apache NiFi, AWS Glue, Azure Data Factory, Google Dataflow). As of the date of this publication, none of these systems implement provisional identity maps with TTL lifecycle, write-time alias compression with a chain-length-one invariant, or entity-scoped park-and-sweep with semantic dependency resolution. The fact that each mechanism is individually known does not make their specific integration into a coordinated identity resolution subsystem obvious — the design choices (ULIDs over GUIDs for sortable provisionals, TTL with downstream-reference-aware GC, write-time chain cap rather than read-time resolution, entity-type-scoped parking rather than global retry) reflect non-trivial architectural decisions that differ from how existing systems have approached the same problem space.

### Challenge 5: "MDM systems already do identity resolution with merge and survivorship rules."

**Strongest form of the argument:** Informatica MDM, Reltio, Tamr, and similar platforms perform entity resolution, merge, survivorship, and identity management as their core function. They handle out-of-order data, multiple source systems, and identity chains.

**Response:** MDM systems operate as separate platforms with their own storage, UI, and batch processing. They are not inline pipeline stages. Key differences: (a) MDM systems do not assign provisional identifiers that participate in downstream processing — they resolve identity in a separate matching/merging phase before data proceeds; (b) MDM systems maintain identity graphs with arbitrary chain lengths that are resolved at query time, not write-time compressed to chain length one; (c) MDM systems do not implement park-and-sweep — records that cannot be matched are either rejected or held in a quarantine that requires manual intervention, not automatic re-evaluation against changing state. The invention described here is designed as a lightweight, embeddable pipeline stage, not a standalone platform.

### Challenge 6: "The TTL-based provisional GC with downstream reference checks is just reference counting."

**Strongest form of the argument:** Reference counting is a well-known garbage collection technique. Checking for downstream references before deleting is standard reference counting.

**Response:** The mechanism is not reference counting in the traditional sense. Traditional reference counting tracks the exact count of references and deallocates when the count reaches zero. This system does not maintain a count — it performs an existence check at GC time ("does any downstream record reference this canonical ID?"). The check is TTL-gated (only performed for expired provisionals) and the response is TTL-extension rather than immediate deallocation. This is closer to a lease-renewal pattern than reference counting: the provisional mapping's lease is extended if it is still in use, regardless of how many references exist. The combination of TTL-based expiry with use-based lease extension for provisional identity mappings is specific to this domain.

### Antagonist Conclusion

The individual techniques draw from established computer science concepts (temporary identifiers, alias rewriting, retry mechanisms, TTL-based expiry). The novelty lies in their specific formulation for the identity resolution domain (provisional-to-confirmed lifecycle with TTL, write-time chain cap at exactly one, semantic dependency-aware sweep with entity-scoped parking) and their integration into a coordinated subsystem within a data ingestion pipeline framework. This publication establishes prior art for this specific combination and formulation, preventing others from patenting these techniques individually or in combination within this domain.

---

**End of Defensive Publication**

*This document is published to establish prior art under the provisions of defensive patent publication. It is intended to prevent the patenting of the described techniques by any party. The described methods, systems, and their combinations are hereby disclosed to the public.*
