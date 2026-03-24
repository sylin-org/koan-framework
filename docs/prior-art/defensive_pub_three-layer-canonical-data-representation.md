# Defensive Publication: Three-Layer Canonical Data Representation with Policy-Driven Materialization

**Publication Type:** Defensive Publication (Prior Art Establishment)
**Publication Date:** 2026-03-24
**Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
**Project:** Koan Framework v0.6.3 (.NET) — Flow/Canon Pillar
**Family:** Flow/Canon — Canonical Data Representation

---

## Abstract

This disclosure describes a three-layer data representation architecture for multi-source entity ingestion in which every attribute value ever observed is preserved across two lossless layers (Ranges and Facts) and a single application-facing layer (Materialized) is derived through configurable per-field policies. The Range layer stores an array-per-attribute structure where each field accumulates all observed values with timestamps and source identifiers. The Facts layer normalizes those ranges into canonical fact quintuples (entity, attribute, value, timestamp, source). The Materialized layer applies a per-field materialization policy — selectable from a closed set (First, Last, Max, Min, Concat) or a user-supplied delegate (Custom) — to project each attribute into a single-valued model suitable for application consumption. A wrapper type, `DynamicFlowEntity<TModel>`, carries the materialized model together with projection metadata (timestamp, source adapter, policy applied) and exposes an `OnProjected` hook for business rule injection before atomic commit to storage. The three layers are connected by a deterministic pipeline: adapter ingest produces a normalized property bag, which is appended to the Range layer, decomposed into the Facts layer, projected through the Materialized layer, passed through the projection hook, and finally committed to storage.

---

## Field of the Invention

Data integration; multi-source entity resolution; canonical data representation; materialized view computation; policy-driven data projection; append-only data stores; fact-based data modeling; entity framework design.

## Keywords

canonical data, three-layer representation, range layer, fact layer, materialized view, materialization policy, last-write-wins, first-write-wins, per-field policy, append-only, array-per-attribute, fact triple, fact quintuple, data lineage, source tracking, projection hook, dynamic flow entity, data integration framework, multi-source ingestion, normalized bag, adapter pattern, policy-driven materialization

---

## 1. Problem Statement

Enterprise and SaaS applications routinely ingest data about the same logical entity from multiple external sources — point-of-sale systems, ERP platforms, third-party APIs, manual administrative entry, IoT sensors, and AI-generated enrichments. Each source may report different values for the same attribute at different times. For example, a product's price may arrive from a Shopify feed at one timestamp, be manually overridden by an administrator at another, and then be updated again from the original feed. A customer's address may be reported by a CRM, corrected by a support agent, and later updated by the customer through a self-service portal. These observations accumulate over time, and the application must decide which value to present for each attribute at any given moment.

### The Single-Value Trap

The dominant pattern in existing data access frameworks (Entity Framework, Django ORM, ActiveRecord, Hibernate, Prisma) is single-valued entity fields. When a new value arrives for an attribute, the previous value is overwritten. The entire observation history is destroyed at write time. This creates four concrete problems:

**Irreversible data loss.** Once a value is overwritten, the previous value is unrecoverable without external audit infrastructure (CDC streams, database triggers, temporal tables). Most applications do not implement such infrastructure, and those that do treat it as an afterthought bolted onto the data access layer rather than as a first-class representation.

**No per-field conflict resolution.** When two sources disagree about an attribute value, the application must choose one. The typical strategy is "last write wins" applied uniformly across all fields. But different fields have different semantics: a product name should prefer the most authoritative source regardless of timestamp, while a stock quantity should prefer the most recent observation regardless of authority. Single-valued fields provide no mechanism for per-field policy selection.

**Audit and compliance gaps.** Regulated industries (healthcare, finance, food safety) require demonstrable lineage for data values — who reported what, when, and from which system. Retrofitting lineage onto a single-valued entity model requires parallel audit tables, CDC pipelines, or event sourcing infrastructure that doubles the storage and query complexity.

**Brittle integration logic.** When a new data source is added, developers must write ad-hoc merge logic that decides how the new source's values interact with existing values. This merge logic is scattered across service methods, message handlers, and import scripts. There is no centralized policy framework governing how multi-source observations collapse into application-visible values.

### Why Existing Approaches Fall Short

**Event Sourcing** preserves a complete event log and derives current state through projection. However, events are entity-level (OrderPlaced, PriceChanged), not attribute-level. Reconstructing the history of a single attribute requires scanning and filtering all events for that entity. There is no per-attribute array structure that enables direct access to all observed values for one field. Furthermore, event sourcing projections are typically defined at the aggregate level, not the field level — there is no mechanism to apply different projection policies to different fields within the same entity.

**Temporal databases** (SQL:2011 temporal tables, PostgreSQL temporal_tables, MariaDB system-versioned tables) maintain row-level version history. Each row version captures the entire entity state at a point in time. To extract the history of a single attribute, you must query all row versions and diff them. This is a row-granularity mechanism, not a field-granularity mechanism. Per-field materialization policies are not supported.

**Change Data Capture (CDC)** systems (Debezium, AWS DMS, Fivetran) capture changes as they flow through a database transaction log. They produce a stream of before/after row images. Like temporal tables, the granularity is the row, not the field. CDC systems are transport mechanisms, not representation layers — they move data but do not define how conflicting observations should be resolved.

**Data vault modeling** (Dan Linstedt's Data Vault 2.0) separates entity identity (hubs), relationships (links), and descriptive attributes (satellites). Satellites are append-only and timestamped, which provides some of the audit capability. However, the projection from satellites to a single-valued business view ("business vault") is typically implemented as a SQL view with ROW_NUMBER() window functions — a query-time computation with no configurable policy abstraction, no per-field policy selection, and no programmatic hook for business rule injection before the projected value is consumed.

**CRDT-based systems** (Riak, Automerge, Yjs) resolve conflicts through algebraically defined merge functions. CRDTs provide eventual consistency guarantees but operate at the data-structure level (counters, sets, registers), not the business-entity level. Mapping business entities onto CRDTs requires the developer to choose a CRDT type for each field, which conflates the merge strategy with the data structure. There is no separation between the lossless observation history and the merge-resolved projection.

### Gap in the State of the Art

No existing framework provides all of the following in a single, integrated representation model:

1. An append-only, per-attribute array (Range layer) that preserves every observed value with timestamp and source
2. A normalized fact decomposition (Facts layer) that enables cross-entity attribute queries and lineage auditing
3. A configurable, per-field materialization policy (Materialized layer) that projects the multi-valued range into a single-valued entity model
4. A wrapper type that carries the materialized model alongside projection metadata (when it was projected, from which source, using which policy)
5. A pre-commit hook on the materialized projection for business rule injection
6. A deterministic pipeline connecting adapter ingestion to storage through all three layers

---

## 2. Prior Art Summary

### 2.1 Entity Framework / Django ORM / ActiveRecord / Hibernate / Prisma

All major ORM frameworks model entity attributes as single-valued fields mapped to database columns. Value assignment overwrites the previous value. No observation history is maintained. No per-field materialization policy exists. No concept of a "range" of values for a single attribute. Change tracking is limited to detecting in-memory mutations for the current unit of work, not historical observation tracking.

### 2.2 Apache Kafka Streams (KTable Materialization)

Kafka Streams maintains changelog topics and materializes them into KTables. The materialization policy is fixed: last-write-wins keyed by the message key. There is no per-field policy configuration, no range layer preserving all observed values, and no fact normalization layer. KTable is a single-layer materialization mechanism, not a three-layer representation.

### 2.3 Apache Flink (Stateful Stream Processing)

Flink provides stateful operators that can accumulate values in keyed state (ValueState, ListState, MapState). Developers can implement custom aggregation logic. However, Flink is a stream processing engine, not a data representation framework. The state management is operator-internal and imperative — there is no declarative three-layer abstraction, no per-field policy configuration, and no entity-oriented wrapper type with projection metadata.

### 2.4 Databricks Delta Lake / Apache Iceberg / Apache Hudi

These lakehouse table formats provide append-only transaction logs, time travel queries, and ACID guarantees over columnar storage. They operate at the table/partition level, not the entity-attribute level. Per-field observation history requires column-level versioning, which these formats do not provide. There is no concept of an attribute range, fact normalization, or per-field materialization policy.

### 2.5 Event Sourcing (Greg Young, Axon Framework, EventStoreDB, Marten)

Event sourcing preserves all events and derives current state through projection functions. Events are entity-level, not attribute-level. Projections are aggregate-level, not field-level. There is no per-attribute range structure, no fact normalization layer, and no per-field policy selection mechanism. The projection function is a monolithic fold over the event stream, not a per-field policy dispatch.

### 2.6 Data Vault 2.0 (Satellites)

Data Vault satellites are append-only, timestamped records of descriptive attributes. This provides attribute-level history, which is the closest prior art to the Range layer. However, the projection from satellite history to a single-valued business entity is implemented as an ad-hoc SQL view, not a configurable policy framework. There is no closed enumeration of materialization policies, no per-field policy assignment, no wrapper type with projection metadata, and no pre-commit hook for business rule injection. The fact normalization layer (entity, attribute, value, timestamp, source) is also absent — satellites store denormalized row snapshots, not individual fact quintuples.

### 2.7 CRDTs (Riak, Automerge, Yjs)

Conflict-free replicated data types provide algebraic merge functions (LWW-Register, MV-Register, G-Counter, OR-Set). Each field is modeled as a specific CRDT type, and the merge function is determined by the type. This conflates the merge strategy with the data structure. There is no separation between the lossless observation history (range) and the merge resolution (materialized). There is no normalized fact layer. The merge function is fixed per CRDT type, not configurable per field at runtime. There is no projection metadata wrapper and no business rule hook.

### 2.8 EAV (Entity-Attribute-Value) Models

EAV stores data as (entity_id, attribute_name, value) triples. This is structurally similar to the Facts layer. However, traditional EAV models are single-valued per attribute (each triple represents the current value, not a historical observation). EAV models that include timestamps become closer to the Facts layer but lack the Range layer (array-per-attribute grouping), the Materialized layer (policy-driven projection), the wrapper type, and the pipeline abstraction.

### 2.9 Datomic

Datomic stores immutable datoms (entity, attribute, value, transaction, added?) and supports temporal queries. This is the closest prior art to the combined Range + Facts layers. However, Datomic's materialization is always "as-of" a specific transaction (point-in-time query), not policy-driven per field. There is no per-field materialization policy (First, Last, Max, Min, Custom), no projection metadata wrapper, no business rule hook, and no explicit three-layer abstraction with distinct Range and Materialized layers. Datomic's model is a single immutable log with query-time filtering, not a three-layer representation with explicit materialization.

---

## 3. Detailed Technical Description

### 3.1 Architectural Overview

The system defines three representation layers for entity data. Each layer serves a distinct consumer and has distinct storage, query, and mutation semantics:

```
                                 ┌──────────────────┐
                                 │   Data Sources    │
                                 │  (Adapters)       │
                                 └────────┬─────────┘
                                          │ raw observations
                                          ▼
                                 ┌──────────────────┐
                                 │  Normalized Bag   │
                                 │  (per-ingest)     │
                                 └────────┬─────────┘
                                          │
                          ┌───────────────┼───────────────┐
                          ▼               ▼               ▼
                 ┌─────────────┐  ┌──────────────┐  ┌────────────────┐
                 │ RANGE LAYER │  │  FACTS LAYER │  │ MATERIALIZED   │
                 │ Array-per-  │  │  Fact         │  │ LAYER          │
                 │ attribute   │  │  Quintuples   │  │ Single-valued  │
                 │ (append)    │  │  (normalized) │  │ (projected)    │
                 └─────────────┘  └──────────────┘  └───────┬────────┘
                                                            │
                                                            ▼
                                                   ┌────────────────┐
                                                   │ OnProjected    │
                                                   │ Hook           │
                                                   └───────┬────────┘
                                                           │
                                                           ▼
                                                   ┌────────────────┐
                                                   │   Storage      │
                                                   └────────────────┘
```

The pipeline is deterministic and unidirectional: data flows from adapter ingest through normalization, into both lossless layers, through policy-driven materialization, through the optional business rule hook, and into storage. There is no feedback path from the Materialized layer back to the Range or Facts layers.

### 3.2 The Range Layer

The Range layer stores all observed values for every attribute of an entity as an ordered array. Each entry in the array is a value observation containing three fields:

- **value**: The observed value, in its native type (number, string, boolean, object, array).
- **at**: The timestamp of the observation, in ISO 8601 format with timezone (UTC preferred).
- **source**: A source identifier string in the format `provider:qualifier` (e.g., `shopify:main`, `manual:admin`, `erp:daily-sync`, `ai:enrichment`).

The Range layer for a single entity is a dictionary keyed by attribute name, where each value is an ordered array of observations:

```json
{
  "entity_type": "Product",
  "entity_id": "12345",
  "ranges": {
    "price": [
      { "value": 29.99, "at": "2025-01-15T10:00:00Z", "source": "shopify:main" },
      { "value": 24.99, "at": "2025-02-01T08:00:00Z", "source": "manual:admin" },
      { "value": 27.99, "at": "2025-03-10T14:00:00Z", "source": "shopify:main" }
    ],
    "name": [
      { "value": "Widget Pro", "at": "2025-01-15T10:00:00Z", "source": "shopify:main" }
    ],
    "stock_quantity": [
      { "value": 150, "at": "2025-01-15T10:00:00Z", "source": "erp:daily-sync" },
      { "value": 142, "at": "2025-01-16T06:00:00Z", "source": "erp:daily-sync" },
      { "value": 200, "at": "2025-02-01T08:30:00Z", "source": "manual:admin" }
    ]
  }
}
```

**Append-only semantics.** New observations are appended to the end of the attribute array. Existing entries are never modified or deleted. The array is ordered by insertion time (which typically corresponds to observation time, but the `at` timestamp is authoritative for ordering during materialization).

**No deduplication.** If the same value arrives from the same source at two different timestamps, both observations are stored. Deduplication is an application-level concern handled during materialization or querying, not at the Range layer.

**Storage mapping.** The Range layer maps naturally to document databases (each entity is a document with nested arrays), column-family stores (each attribute is a column family with timestamped versions), or append-only event logs (each observation is an event). The framework does not prescribe a specific storage engine; the Range layer is a logical representation that adapters serialize to the configured provider.

### 3.3 The Facts Layer

The Facts layer decomposes range observations into normalized fact quintuples. Each quintuple has five fields:

```
(entity_id, attribute, value, timestamp, source)
```

For the product example above, the Facts layer contains:

```
(Product#12345, price,          29.99,        2025-01-15T10:00:00Z, shopify:main)
(Product#12345, price,          24.99,        2025-02-01T08:00:00Z, manual:admin)
(Product#12345, price,          27.99,        2025-03-10T14:00:00Z, shopify:main)
(Product#12345, name,           "Widget Pro", 2025-01-15T10:00:00Z, shopify:main)
(Product#12345, stock_quantity, 150,          2025-01-15T10:00:00Z, erp:daily-sync)
(Product#12345, stock_quantity, 142,          2025-01-16T06:00:00Z, erp:daily-sync)
(Product#12345, stock_quantity, 200,          2025-02-01T08:30:00Z, manual:admin)
```

**Relationship to the Range layer.** The Facts layer is a normalized projection of the Range layer. Every observation in every attribute array of the Range layer corresponds to exactly one fact quintuple. The transformation is lossless and reversible: given all fact quintuples for an entity, the Range layer can be reconstructed by grouping on (entity_id, attribute) and ordering by timestamp.

**Purpose.** The Facts layer enables:
- Cross-entity attribute queries: "Find all entities where attribute X was reported by source Y after timestamp T."
- Lineage auditing: "Show every source that has ever reported a value for this entity's price attribute."
- Compliance reporting: "Which administrator last modified this record, and when?"
- Anomaly detection: "This attribute has received values from 5 different sources in the last hour — flag for review."

**Storage mapping.** The Facts layer maps naturally to relational tables (one row per fact), triple stores (with timestamp and source as reification metadata), or columnar analytics stores (for cross-entity aggregation). The entity_id is a composite key of entity type and identifier, formatted as `{Type}#{Id}`.

### 3.4 The Materialized Layer

The Materialized layer projects each attribute's observation array (from the Range layer) into a single value using a configurable materialization policy. The result is a conventional single-valued entity model that application code consumes through standard property access.

#### 3.4.1 Materialization Policies

The framework defines a closed enumeration of built-in policies plus an extension point:

```csharp
public enum MaterializationPolicy
{
    First,      // The first observed value (earliest timestamp)
    Last,       // The last observed value (most recent timestamp) — DEFAULT
    Max,        // The maximum value (numeric attributes only)
    Min,        // The minimum value (numeric attributes only)
    Concat,     // Concatenation of all values (string attributes, separator-configurable)
    Custom      // User-defined delegate: Func<IReadOnlyList<Observation>, object>
}
```

**Default policy.** If no policy is specified for an attribute, the framework applies `Last` (last-write-wins). This preserves backward compatibility with the behavior of conventional ORMs.

**Per-field assignment.** Policies are assigned at the field level, not the entity level. Different attributes of the same entity can use different policies:

```csharp
public class ProductMaterializationProfile : IMaterializationProfile<Product>
{
    public void Configure(IMaterializationBuilder<Product> builder)
    {
        builder.Field(p => p.Name,           MaterializationPolicy.First);
        builder.Field(p => p.Price,          MaterializationPolicy.Last);
        builder.Field(p => p.StockQuantity,  MaterializationPolicy.Max);
        builder.Field(p => p.Description,    MaterializationPolicy.Custom,
            observations => MergeDescriptions(observations));
    }
}
```

In this example:
- `Name` uses `First` — the first source to report a name is authoritative.
- `Price` uses `Last` — the most recent price observation wins.
- `StockQuantity` uses `Max` — the highest reported stock quantity is used (conservative upper bound for availability).
- `Description` uses `Custom` — a user-defined delegate that merges all description observations according to business logic (e.g., longest description, most recent non-empty description, AI-merged summary).

#### 3.4.2 Policy Execution

For each attribute in the Range layer, the materialization engine:

1. Retrieves the observation array for the attribute.
2. Looks up the configured policy for that attribute (or defaults to `Last`).
3. Sorts the observation array by timestamp (ascending).
4. Applies the policy:
   - `First`: Returns `observations[0].Value`.
   - `Last`: Returns `observations[^1].Value` (last element).
   - `Max`: Returns `observations.Max(o => (IComparable)o.Value)`.
   - `Min`: Returns `observations.Min(o => (IComparable)o.Value)`.
   - `Concat`: Returns `string.Join(separator, observations.Select(o => o.Value))`.
   - `Custom`: Returns `delegate(observations)`.
5. Assigns the resulting value to the corresponding property of the target model `TModel`.

#### 3.4.3 Materialization Metadata

Each materialized attribute carries metadata about how it was produced:

```csharp
public sealed record MaterializedFieldInfo(
    string AttributeName,
    MaterializationPolicy PolicyApplied,
    DateTimeOffset ProjectedAt,
    string WinningSource,
    int ObservationCount
);
```

- `PolicyApplied`: Which policy produced this value.
- `ProjectedAt`: When the materialization was computed.
- `WinningSource`: The source identifier of the observation that contributed the materialized value (for `First`/`Last`, this is the source of the selected observation; for `Max`/`Min`, the source of the extreme-valued observation; for `Concat`/`Custom`, this is `"*"` indicating multiple sources).
- `ObservationCount`: How many observations exist for this attribute in the Range layer.

### 3.5 DynamicFlowEntity<TModel>

The materialized model is not returned directly to application code. Instead, it is wrapped in a `DynamicFlowEntity<TModel>` that carries the model alongside its projection context:

```csharp
public sealed class DynamicFlowEntity<TModel> where TModel : class
{
    public TModel Model { get; }
    public string EntityId { get; }
    public DateTimeOffset ProjectedAt { get; }
    public string SourceAdapter { get; }
    public IReadOnlyDictionary<string, MaterializedFieldInfo> FieldInfo { get; }
    public Action<TModel>? OnProjected { get; set; }
}
```

**Properties:**
- `Model`: The materialized single-valued entity, ready for application consumption.
- `EntityId`: The canonical entity identifier (format `{Type}#{Id}`).
- `ProjectedAt`: The timestamp at which materialization was computed.
- `SourceAdapter`: The identifier of the adapter that triggered the most recent ingest.
- `FieldInfo`: A dictionary mapping attribute names to their `MaterializedFieldInfo`, enabling the application to inspect how each field was derived.
- `OnProjected`: A mutable hook invoked after materialization but before storage commit. The hook receives the materialized model and may modify it (e.g., compute derived fields, enforce business invariants, trigger side effects).

**OnProjected hook semantics:**
1. The materialization engine computes all field values according to their policies.
2. The engine constructs the `DynamicFlowEntity<TModel>` instance.
3. If `OnProjected` is set, the engine invokes it with the `Model`.
4. The hook may read any field of the model and write to any field.
5. After the hook returns, the engine commits the model to storage atomically.
6. If the hook throws an exception, the commit is aborted and the exception propagates to the caller.

This design allows business rules that depend on the materialized state (e.g., "if price decreased by more than 20% from previous materialization, flag for review") to execute within the materialization pipeline without requiring separate post-processing infrastructure.

### 3.6 The Ingestion Pipeline

The complete pipeline from raw data to storage proceeds through the following deterministic stages:

```
Adapter Ingest → Normalized Bag → Range Append → Fact Extraction → Materialization → Projection Hook → Storage
```

**Stage 1: Adapter Ingest.** A source-specific adapter (Shopify adapter, ERP adapter, CSV import adapter, manual entry adapter) receives raw data in its native format and produces a set of attribute-value pairs with a source identifier and timestamp.

**Stage 2: Normalized Bag.** The adapter output is normalized into a `PropertyBag` — a flat dictionary of attribute names to values, annotated with the source identifier and ingestion timestamp. The bag is source-agnostic: all adapters produce the same bag structure regardless of their native format.

```csharp
public sealed record PropertyBag(
    string EntityType,
    string EntityId,
    IReadOnlyDictionary<string, object> Values,
    string Source,
    DateTimeOffset ObservedAt
);
```

**Stage 3: Range Append.** For each attribute in the normalized bag, a new observation `{ value, at, source }` is appended to the attribute's array in the Range layer. If the attribute does not yet exist in the Range layer, a new array is created with the single observation.

**Stage 4: Fact Extraction.** For each attribute in the normalized bag, a new fact quintuple `(entity_id, attribute, value, timestamp, source)` is inserted into the Facts layer. This stage runs in parallel with Range Append — both consume the same normalized bag and produce independent outputs.

**Stage 5: Materialization.** The materialization engine reads the updated Range layer for the entity and applies per-field policies to produce a single-valued model. The engine retrieves the `IMaterializationProfile<TModel>` for the entity type and invokes each field's configured policy.

**Stage 6: Projection Hook.** If an `OnProjected` hook is registered, it is invoked with the materialized model. The hook runs synchronously within the pipeline transaction.

**Stage 7: Storage.** The materialized model is committed to the configured storage provider (SQL database, document store, etc.). The commit is atomic: either all layers (Range, Facts, Materialized) are updated, or none are.

### 3.7 Source Identifier Format

Source identifiers follow the format `{provider}:{qualifier}`:

- `provider`: The integration system (e.g., `shopify`, `erp`, `manual`, `ai`, `csv`).
- `qualifier`: A discriminator within the provider (e.g., `main`, `admin`, `daily-sync`, `enrichment`, `bulk-import-2025-03`).

This format enables lineage queries at both the provider level ("show all values from Shopify") and the qualifier level ("show all values from the March bulk import").

### 3.8 Temporal Queries on the Range Layer

The Range layer supports temporal queries by filtering observation arrays:

- **As-of query:** For a given timestamp T, materialize each attribute using only observations where `at <= T`. This reconstructs the entity state as it would have appeared at time T.
- **Between query:** For a given interval [T1, T2], return all observations where `T1 <= at <= T2`. This shows what changed during that interval.
- **Source-filtered query:** Materialize using only observations from a specific source. This shows the entity as a single source sees it, ignoring all other sources.

These queries are compositional: an as-of query can be combined with a source filter to answer "what did source X believe about this entity at time T?"

### 3.9 Conflict Visibility

Because the Range layer preserves all observations and the Materialized layer carries `MaterializedFieldInfo` per field, the application can detect and surface conflicts:

- **Observation count > 1 with different values:** Multiple sources disagree. The application can display a "conflict indicator" to the user.
- **WinningSource differs from a preferred source:** The materialization policy selected a value from a non-preferred source. The application can highlight this for review.
- **Custom policy delegate:** The application can implement a conflict-aware delegate that examines all observations and applies domain-specific resolution logic (e.g., prefer the source with the most recent SLA, prefer the source with the highest authority ranking).

---

## 4. Claims-Style Disclosures

1. A method for representing multi-source entity data using a three-layer architecture wherein: (a) a Range layer stores, for each attribute of an entity, an ordered array of all observed values, each observation comprising a value, a timestamp, and a source identifier; (b) a Facts layer normalizes said observations into canonical fact quintuples of the form (entity_id, attribute, value, timestamp, source); (c) a Materialized layer projects each attribute's observation array into a single value by applying a configurable materialization policy selected from a defined set (First, Last, Max, Min, Concat, Custom); distinct from event sourcing in that observations are stored at the attribute level rather than the entity/event level, from temporal databases in that materialization is policy-driven per field rather than point-in-time per row, and from CRDTs in that the lossless observation history is separated from the conflict-resolved projection.

2. A method for per-field materialization policy assignment within a multi-source entity framework wherein: (a) a materialization profile associates each field of a typed entity model with an independently selected materialization policy; (b) default policy is Last (last-write-wins) when no explicit policy is configured for a field; (c) a Custom policy accepts a user-defined delegate that receives the full observation array and returns a single value; (d) different fields of the same entity instance are materialized using different policies within a single materialization pass; distinct from Data Vault 2.0 satellite projection (which uses ad-hoc SQL window functions without per-field policy abstraction) and from CRDT merge functions (which are fixed per data-structure type rather than configurable per business field).

3. A wrapper type for materialized entity models, `DynamicFlowEntity<TModel>`, wherein: (a) the wrapper carries the single-valued materialized model alongside per-field metadata comprising the policy applied, the projection timestamp, the winning source identifier, and the observation count; (b) the wrapper exposes a mutable `OnProjected` hook that is invoked after materialization but before atomic commit to storage; (c) the hook may read and modify the materialized model to enforce business invariants, compute derived fields, or trigger side effects; (d) if the hook throws an exception, the storage commit is aborted; distinct from ORM change tracking (which tracks in-memory mutations, not multi-source projection metadata) and from event sourcing projections (which do not carry per-field metadata about which events contributed to each projected value).

4. A deterministic pipeline for multi-source entity ingestion wherein: (a) a source-specific adapter produces raw observations; (b) observations are normalized into a source-agnostic property bag annotated with source identifier and timestamp; (c) the normalized bag is appended to the Range layer and decomposed into the Facts layer in parallel; (d) the Materialized layer is recomputed by applying per-field policies to the updated Range layer; (e) an optional projection hook is invoked on the materialized model; (f) all three layers are committed to storage atomically; distinct from CDC pipelines (which transport row-level changes without per-field policy application) and from ETL systems (which transform data without maintaining lossless per-attribute observation history).

5. A method for temporal and source-filtered queries on multi-source entity data wherein: (a) an as-of query materializes each attribute using only observations with timestamps at or before a specified point in time; (b) a source-filtered query materializes using only observations from a specified source identifier; (c) as-of and source-filtered queries are compositional and can be combined; (d) the same per-field materialization policies apply to filtered observation sets as to unfiltered sets; distinct from temporal databases (which provide point-in-time row snapshots without per-field policy application) and from Datomic (which provides as-of queries but without configurable per-field materialization policies).

6. A method for source-aware conflict detection in materialized entity views wherein: (a) each materialized field carries an observation count indicating how many distinct observations exist in the Range layer; (b) each materialized field carries a winning source identifier indicating which source's observation was selected by the materialization policy; (c) application code inspects the per-field metadata to detect disagreements across sources, identify non-preferred winning sources, and surface conflicts to users or automated review systems; (d) a Custom materialization policy delegate can implement domain-specific conflict resolution logic that considers source authority rankings, SLA recency, or other business criteria.

7. A source identifier convention for multi-source entity data lineage wherein: (a) each observation carries a source identifier in the format `{provider}:{qualifier}` where `provider` identifies the integration system and `qualifier` discriminates within the provider; (b) lineage queries can filter at the provider level or the qualifier level; (c) the source identifier is propagated through all three layers (Range observation, Fact quintuple, Materialized field metadata) enabling end-to-end traceability from raw ingest to application-visible value.

8. A method for pre-commit business rule injection in a multi-source materialization pipeline wherein: (a) after the materialization engine computes all field values according to their policies, it constructs a wrapper type carrying the materialized model; (b) a registered hook delegate is invoked with the materialized model before storage commit; (c) the hook may enforce invariants (e.g., price must be positive), compute derived fields (e.g., profit margin from cost and price), or trigger side effects (e.g., enqueue a notification if a value changed by more than a threshold); (d) the hook executes within the pipeline transaction boundary, ensuring that business rule failures prevent inconsistent storage commits.

9. A normalized property bag as an intermediate representation in a multi-source ingestion pipeline wherein: (a) source-specific adapters produce heterogeneous raw data formats; (b) each adapter normalizes its output into a common property bag structure comprising entity type, entity identifier, a flat dictionary of attribute-value pairs, source identifier, and observation timestamp; (c) downstream pipeline stages (Range append, Fact extraction, Materialization) consume only the normalized bag and have no knowledge of the source adapter's native format; (d) adding a new data source requires only implementing a new adapter that produces a normalized bag, with no changes to the Range, Facts, or Materialized layers.

10. A method for maintaining referential consistency across three data representation layers wherein: (a) the Range layer, Facts layer, and Materialized layer are updated within a single atomic transaction for each ingestion event; (b) if any layer update fails, the entire transaction is rolled back, ensuring that the three layers never diverge; (c) the materialized model is always derivable from the Range layer by re-applying the configured policies, enabling repair by re-materialization if layer drift is detected; (d) a consistency check can compare the Materialized layer against a fresh projection from the Range layer to detect and correct stale materializations.

---

## 5. Implementation Evidence

The three-layer canonical data representation is being implemented as part of the Koan Framework v0.6.3 (.NET) Flow/Canon pillar. The framework targets .NET 10 (`net10.0`).

### 5.1 Core Types

- `PropertyBag` — Normalized intermediate representation produced by source adapters.
- `Observation` — A single value observation with `Value`, `At` (DateTimeOffset), and `Source` (string in `provider:qualifier` format).
- `MaterializationPolicy` — Enum defining built-in policies (First, Last, Max, Min, Concat, Custom).
- `IMaterializationProfile<TModel>` — Interface for per-field policy configuration via a fluent builder.
- `MaterializedFieldInfo` — Record carrying per-field projection metadata (policy applied, projected timestamp, winning source, observation count).
- `DynamicFlowEntity<TModel>` — Wrapper type carrying materialized model with field metadata and `OnProjected` hook.

### 5.2 Pipeline Components

- `IFlowAdapter` — Interface for source-specific adapters that produce `PropertyBag` instances.
- `RangeStore` — Append-only storage for per-attribute observation arrays, with temporal and source-filtered query support.
- `FactStore` — Normalized quintuple storage for cross-entity lineage queries.
- `MaterializationEngine` — Reads Range layer, applies per-field policies from registered profiles, constructs `DynamicFlowEntity<TModel>`, invokes hooks, and coordinates atomic commit.

### 5.3 Integration Points

The three-layer representation integrates with the Koan Framework's existing infrastructure:

- **Entity<T> pattern:** `DynamicFlowEntity<TModel>` is a projection target that coexists with the framework's `Entity<T>` static method pattern (e.g., `Product.Get(id)` returns the Materialized layer view).
- **Multi-Provider Transparency:** The Range and Facts layers map to document stores, relational tables, or event logs depending on the configured storage provider, preserving the framework's multi-provider abstraction.
- **KoanAutoRegistrar:** Materialization profiles are discovered and registered automatically via the framework's assembly scanning infrastructure.

---

## 6. Publication Notice

This document is published as a defensive publication to establish prior art. The techniques, architectures, and methods described herein are placed into the public domain for the purpose of preventing future patent claims on three-layer canonical data representation with per-field policy-driven materialization.

This disclosure is intended to be prior art under 35 U.S.C. Section 102 (United States), Article 54 EPC (European Patent Convention), and equivalent provisions in other patent jurisdictions. The publication date of 2026-03-24 establishes the earliest effective date for prior art purposes.

The inventor asserts no patent rights over the disclosed techniques and expressly dedicates them to the public.

---

## 7. Antagonist Review Log

### Round 1

**Antagonist Attack — Abstraction Gap (Range Layer Ordering Semantics):**

The disclosure states that observation arrays are "ordered by insertion time" but also that "the `at` timestamp is authoritative for ordering during materialization." What happens when insertion order and `at` timestamp order diverge? If an adapter ingests a batch of historical observations out of chronological order, does the Range layer reorder them, or are they stored in insertion order with materialization sorting by `at`?

**Author Revision:**

The Range layer stores observations in insertion order. It does not reorder entries. The array is an append-only log of when the system learned about each observation, not a chronologically sorted timeline. The materialization engine sorts observations by `at` timestamp before applying policies. This means the Range layer and the materialization engine have different ordering: the Range layer reflects discovery order; the materialization engine uses observation-time order.

This distinction matters for the `Concat` policy: if the intent is to concatenate values in the order they were observed (chronological), the materialization engine sorts first. If the intent is to concatenate in discovery order, a Custom delegate can skip the sort. The built-in policies (First, Last, Max, Min) are all order-independent after sorting by `at`.

This is now clarified in Section 3.4.2, step 3: "Sorts the observation array by timestamp (ascending)."

---

**Antagonist Attack — Reproducibility Gap (Custom Policy Delegate Signature):**

The disclosure defines Custom as `Func<IReadOnlyList<Observation>, object>` but does not specify what the `Observation` type contains or whether the list is pre-sorted. A PHOSITA cannot implement the delegate without this information.

**Author Revision:**

The `Observation` type is:

```csharp
public sealed record Observation(
    object Value,
    DateTimeOffset At,
    string Source
);
```

The list passed to the Custom delegate is pre-sorted by `At` ascending (same sort applied to all policies in step 3 of Section 3.4.2). The delegate receives the same sorted list regardless of insertion order. The return type is `object` because the delegate may return any type compatible with the target property — the materialization engine performs a runtime cast to the property type after the delegate returns. If the cast fails, the engine throws an `InvalidMaterializationException` with the attribute name, expected type, and actual returned type.

---

**Antagonist Attack — Prior Art Weakness (Datomic Closeness):**

Datomic stores immutable (entity, attribute, value, transaction) tuples with full history and supports as-of queries. The Facts layer is structurally identical to Datomic datoms with the addition of a source field. How is this sufficiently distinct?

**Author Revision:**

The disclosure acknowledges Datomic as the closest prior art (Section 2.9) and identifies four structural differences:

1. **Source tracking.** Datomic datoms carry a transaction ID but not a source identifier. The source field is absent. Knowing *which system* produced a value requires application-level metadata encoding (e.g., storing source in a separate attribute or in transaction metadata). The Facts layer makes source a first-class field of every quintuple.

2. **Per-field materialization policy.** Datomic's materialization is always point-in-time (as-of a transaction). There is no per-field policy mechanism where one attribute uses First and another uses Max within the same entity. Datomic's `db.asOf(t)` always returns the last-asserted value as of transaction t, uniformly across all attributes.

3. **Range layer grouping.** Datomic stores flat datoms. There is no explicit per-attribute array structure. To obtain all historical values for a single attribute, the application queries the datom index with an entity-attribute prefix. The Range layer provides this grouping as a first-class data structure, optimized for the common access pattern of "show me all values for this field."

4. **DynamicFlowEntity wrapper.** Datomic returns entity maps (lazy attribute lookups against the database). There is no wrapper type carrying per-field projection metadata (policy applied, winning source, observation count) or a pre-commit hook. The application has no way to know *why* a particular value was selected without re-querying the history.

The novelty is not in any single layer but in the integrated three-layer architecture with per-field policies, source-aware metadata, and the projection hook pipeline.

---

**Antagonist Attack — Scope Hole (Schema Evolution):**

What happens when a new attribute is added to the entity model after observations already exist in the Range layer? What happens when an attribute is removed?

**Author Revision:**

Schema evolution is handled through the following rules:

1. **New attribute added to model:** The materialization engine finds no observations in the Range layer for this attribute. The field is set to its default value (null for reference types, default(T) for value types). The `MaterializedFieldInfo` for this field has `ObservationCount = 0` and `WinningSource = null`, indicating that no observation contributed to the value.

2. **Attribute removed from model:** Observations continue to exist in the Range and Facts layers (they are never deleted). The materialization engine ignores Range attributes that have no corresponding property in the target model. The lossless layers preserve the data; only the Materialized projection omits it. If the attribute is later re-added to the model, the historical observations are immediately available for materialization.

3. **Attribute type change:** If the target property type changes (e.g., string to int), historical observations with incompatible types cause materialization failure for that field. The engine sets the field to its default value and records a `MaterializationWarning` in the field metadata. The Custom policy delegate can implement type coercion logic for backward compatibility.

---

**Antagonist Attack — Atomicity Mechanism (Claim 10):**

Claim 10 asserts "single atomic transaction" across three layers, but these layers may map to different storage engines (document store for Ranges, relational table for Facts, SQL for Materialized). How is cross-store atomicity achieved?

**Author Revision:**

Cross-store atomicity depends on the deployment topology:

1. **Single-store deployment** (all three layers in the same database): Standard database transactions provide atomicity. This is the recommended default deployment.

2. **Multi-store deployment** (layers in different storage engines): The framework uses a two-phase approach:
   - Phase 1: Write to Range and Facts layers (lossless, append-only — idempotent on retry).
   - Phase 2: Materialize and write to the Materialized layer.
   - If Phase 2 fails, the lossless layers contain the data and materialization can be retried. The Materialized layer may be stale until retry succeeds.
   - A consistency check (Claim 10d) compares the Materialized layer against a fresh projection from the Range layer to detect and repair staleness.

The atomicity guarantee in Claim 10 is strongest in single-store deployments and degrades gracefully to eventual consistency with self-repair in multi-store deployments. The disclosure now clarifies this distinction: "atomically within a single storage provider, or with idempotent retry and consistency repair across multiple providers."

---

### Round 2

**Antagonist Attack — Missing Edge Case (Observation Volume):**

The Range layer appends every observation forever. For high-frequency data sources (IoT sensors reporting every second, stock price feeds), a single attribute could accumulate millions of observations. What prevents the Range layer from becoming unbounded and degrading materialization performance?

**Author Revision:**

The disclosure does not prescribe a compaction strategy because compaction is a storage-layer concern, not a representation-layer concern. However, the framework supports optional compaction through:

1. **Window-based retention:** Observations older than a configurable retention period (e.g., 90 days) are archived from the Range layer to cold storage. The Facts layer retains all quintuples indefinitely (it is optimized for append-only columnar storage). Materialization uses only the retained window unless an as-of query requests a historical timestamp, in which case the archived range is loaded on demand.

2. **Snapshot compaction:** After a configurable observation count threshold per attribute (e.g., 1000), the Range layer creates a snapshot observation that summarizes the compacted observations (e.g., for a Max policy, the snapshot stores the maximum value seen). The original observations are archived. The snapshot is marked with a `compacted: true` flag so materialization knows it represents a summary, not a raw observation.

3. **Materialization caching:** The materialized model is cached and only re-computed when new observations arrive. The cost of sorting and policy application is amortized over ingest events, not read operations.

These are operational configurations, not architectural components. The three-layer representation is correct regardless of whether compaction is enabled.

---

**Antagonist Attack — Prior Art Weakness (EAV + Temporal Extension):**

An EAV model with timestamps — (entity, attribute, value, timestamp) — plus a "source" column — (entity, attribute, value, timestamp, source) — is structurally identical to the Facts layer. Adding an array-per-attribute view on top is a standard GROUP BY query. Adding a materialization policy is a CASE expression in a SQL view. Is this really novel?

**Author Revision:**

The antagonist correctly identifies that each individual layer has precedent:

- The Facts layer resembles temporal EAV with a source column.
- The Range layer resembles a GROUP BY array aggregation over temporal EAV.
- The Materialized layer resembles a SQL view with CASE-based aggregation.

The disclosure does not claim novelty in any single layer in isolation. The novelty is in the **integrated framework-level abstraction** that:

1. **Reifies the three layers as first-class concepts** with distinct types (`Observation[]`, fact quintuples, `DynamicFlowEntity<TModel>`), not ad-hoc SQL views.
2. **Provides a typed, compile-time-checked materialization profile** (`IMaterializationProfile<TModel>`) rather than runtime SQL expressions.
3. **Wraps the materialized output** in a metadata-carrying type with a pre-commit hook, creating a pipeline abstraction that SQL views cannot express.
4. **Connects the layers through a deterministic pipeline** with atomic commit semantics and adapter normalization.
5. **Integrates with an entity framework's static-method access pattern** (e.g., `Product.Get(id)` transparently returns the Materialized projection).

The defensive publication's purpose is to prevent a future patent applicant from claiming this integrated combination as novel. Whether the combination is "obvious" over temporal EAV is a legal determination that examiners make; the publication ensures that the combination is documented prior art regardless.

---

**Antagonist Attack — Section 112 Enablement (Materialization Engine Implementation):**

The disclosure describes the pipeline at an architectural level but does not provide the materialization engine's implementation. A PHOSITA could implement it multiple ways. Is the disclosure enabling?

**Author Revision:**

The disclosure provides sufficient detail for a PHOSITA to implement the materialization engine:

1. **Input:** Range layer observation arrays (Section 3.2), per-field policy configuration (Section 3.4.1).
2. **Algorithm:** For each field: retrieve array, sort by `At`, apply policy (Section 3.4.2 steps 1-5), record metadata (Section 3.4.3).
3. **Output:** `DynamicFlowEntity<TModel>` with populated `Model`, `FieldInfo`, and hook (Section 3.5).
4. **Error handling:** Type mismatch produces `MaterializationWarning`; hook exception aborts commit (Section 3.5).

The implementation is a straightforward loop over entity properties, dictionary lookup for policy, sort, and switch/case on policy enum. The disclosure does not prescribe a specific implementation because the architecture (not the implementation) is what the publication defends. Any competent .NET developer can implement the described algorithm from the specification.

---

**Antagonist declares: "No further objections — this disclosure is sufficient to block patent claims on the described invention."**

The disclosure provides:
- Precise data structures for all three layers (Range observations, Fact quintuples, Materialized models with metadata)
- Exact policy enumeration with per-field assignment via typed profiles
- Complete pipeline description from adapter ingest to atomic storage commit
- Wrapper type specification with pre-commit hook semantics
- Clear differentiation from all identified prior art (ORMs, Kafka KTable, Flink, Delta Lake, Event Sourcing, Data Vault, CRDTs, EAV, Datomic)
- Edge case coverage (schema evolution, observation volume, cross-store atomicity, ordering semantics)
- Implementation evidence within the Koan Framework
- Temporal and source-filtered query composition
