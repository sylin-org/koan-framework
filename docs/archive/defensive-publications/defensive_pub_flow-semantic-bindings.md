# Defensive Publication: Semantic Binding System for Value Object-to-Aggregate Association with External ID Translation

## Header Block

- **Title:** Attribute-Driven Semantic Binding System for Value Object Association, External Identity Envelope, and Contractless Normalized Bag Ingestion in Data Pipeline Frameworks
- **Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
- **Disclosure Date:** 2026-03-24
- **Field of Invention:** Data integration pipeline infrastructure, specifically methods for associating ingested value objects with canonical aggregates using semantic binding hints, external identity envelopes, and schema-free normalized bag ingestion.
- **Keywords:** semantic binding, value object, aggregate, canonical ID, external ID, adapter identity, normalized bag, contractless ingestion, parent key, flow adapter, ETL, data pipeline, identity envelope

---

## 1. Problem Statement

Data integration pipelines ingest records from multiple external systems (Shopify, Salesforce, ERP systems). Each system uses its own identifier scheme — Shopify uses `shop_12345`, Salesforce uses `00Q1234567890AB`. The pipeline must correlate these external identifiers with canonical internal entities while keeping domain models clean of source-specific identity fields.

Existing ETL tools tightly couple models to source systems. Debezium records contain source-specific metadata within the event payload. Fivetran schemas mirror source schemas. Airbyte uses JSON schema contracts that must be predefined. In all cases, external identifiers are embedded in the model or require explicit schema contracts.

Additionally, when ingesting value objects (e.g., order line items) that reference parent aggregates (e.g., orders), the parent may not yet exist in the canonical store (out-of-order arrival). The pipeline must resolve this dependency without failing the ingest.

What is needed is a system where: (a) external IDs live in an envelope outside the model, (b) adapters are automatically identified by attribute, (c) parent relationships are declared and auto-resolved via canonical IDs, and (d) ingestion can proceed without a predefined schema contract.

---

## 2. Prior Art Summary

**Debezium:** CDC connector that embeds source metadata (`source.table`, `source.ts_ms`) within the event payload. Models are tightly coupled to the source schema. No envelope separation of external IDs.

**Fivetran:** Connector-based ETL that mirrors source schemas into destination tables. Each source has its own table schema. No canonical ID resolution. No adapter identity system.

**Airbyte:** Uses JSON schema contracts (catalog) defining source schemas. Schemas must be predefined and versioned. No contractless bag ingestion. No semantic binding hints.

**Apache Kafka Connect:** Transforms via SMT (Single Message Transforms) operate on records but don't provide semantic binding, external ID envelopes, or parent key resolution.

**Specific gaps:**
1. No ETL framework separates external IDs into an envelope outside the domain model.
2. No framework provides `[FlowAdapter]` identity stamping on every message from a source.
3. No framework supports `[ParentKey]` attribute-driven parent resolution via canonical IDs.
4. No framework provides contractless "normalized bag" ingestion with automatic property validation.

---

## 3. Detailed Description of the Invention

### 3.1 Adapter Identity via [FlowAdapter] Attribute

```
[FlowAdapter("shopify", "main")]
public class ShopifyMainAdapter : BackgroundService
{
    // Every message emitted by this adapter is automatically stamped with:
    //   adapter.system = "shopify"
    //   adapter.name = "main"
}
```

The attribute is discovered during auto-registration (described in a related disclosure). The system/name pair uniquely identifies the source adapter and is propagated through the entire pipeline.

### 3.2 External Identity Envelope

External IDs are stored in message envelope metadata, NOT in model properties:

```
Envelope:
  identifier.external.shopify = "shop_12345"
  identifier.external.woocommerce = "woo_67890"
  adapter.system = "shopify"
  adapter.name = "main"

Model (clean, transport-agnostic):
  Product { Name = "Widget", Price = 29.99, Description = "..." }
```

A single canonical entity can have multiple external IDs from different systems. The model remains free of source-specific fields. The envelope carries zero or more external identifiers as key-value pairs.

### 3.3 External ID Index

```
Index structure:
  (entityKey, system, externalId) → canonicalId

Example entries:
  (product, shopify, "shop_12345") → "01HZXYZ..."
  (product, woocommerce, "woo_67890") → "01HZXYZ..."
```

The index enables bidirectional lookup: given an external ID, find the canonical entity; given a canonical entity, find all external IDs.

### 3.4 [ParentKey] Attribute for Declarative Parent Linking

```
public class OrderLine : Entity<OrderLine, string>
{
    [ParentKey]
    public string OrderId { get; set; }
    // Auto-resolved: OrderLine.OrderId → Order.CanonicalId
}
```

When an `OrderLine` is ingested with `OrderId = "ext_order_123"`:
1. Pipeline looks up `(order, {adapter.system}, "ext_order_123")` in the ExternalId index
2. If found: resolves to canonical Order ID and sets `OrderLine.OrderId`
3. If not found: parks the record for later resolution (park-and-sweep, described in related disclosure)

### 3.5 Contractless Normalized Bag Ingestion

```json
{
  "model": "product",
  "data": {
    "name": "Widget Pro",
    "price": 29.99,
    "category": "electronics"
  },
  "reference.category.external.shopify": "cat_electronics",
  "identifier.external.shopify": "prod_12345"
}
```

- `model` key maps to canonical entity type (no predefined schema needed)
- `data` contains field values validated against known entity properties
- `reference.<entityKey>.external.<system>` declares relationships via external IDs
- Unknown fields in `data` are logged and ignored (forward-compatible)
- No JSON Schema contract, no Avro schema, no explicit schema version required

### 3.6 Semantic Binding Hints (FlowBindingKeys)

Binding hints declared via attributes or registry:

```
FlowBindingKeys:
  ReferenceKey    — aggregation/association key for parent resolution
  PartitionKey    — storage partitioning hint
  MetadataKeys    — fields to propagate as metadata (not model properties)
```

These hints guide the Associate step (canonical ID resolution) without requiring explicit resolution code per entity.

### 3.7 Adapter Auto-Registration

```
Discovery:
  [FlowAdapter] attribute on BackgroundService types → auto-discovered

Configuration:
  Koan:Canon:Adapters:AutoStart = true (containers) | false (local)
  Koan:Canon:Adapters:Include = ["shopify:main", "salesforce:prod"]
  Koan:Canon:Adapters:Exclude = ["test:mock"]
```

Adapters are automatically started in container environments and manually started locally. Include/Exclude lists use "system:adapter" identifiers for filtering.

---

## 4. Claims-Style Disclosure

1. An external identity envelope mechanism for data pipeline ingestion wherein external system identifiers (e.g., Shopify product IDs) are carried in message envelope metadata (`identifier.external.<system>`) rather than in domain model properties, keeping models transport-agnostic and enabling multiple external IDs per canonical entity.

2. An `[FlowAdapter]` attribute that stamps every message from a source adapter with system/name identity metadata, enabling automatic provenance tracking throughout the pipeline without manual stamping.

3. A `[ParentKey]` attribute for declarative parent relationship resolution wherein child entity properties are automatically resolved from external IDs to canonical IDs via the ExternalId index, with unresolvable references parked for later retry rather than causing ingestion failure.

4. A contractless normalized bag ingestion mechanism wherein data is ingested as key-value dictionaries with a `model` key identifying the canonical entity type, with field validation against known properties but no requirement for predefined schema contracts, enabling forward-compatible ingestion from evolving source systems.

5. An ExternalId index with the structure `(entityKey, system, externalId) → canonicalId` that enables bidirectional lookup between external system identifiers and canonical entities, supporting multiple external IDs per entity and multiple systems per ID.

6. A semantic binding hint system (FlowBindingKeys) that declares aggregation keys, partitioning hints, and metadata propagation rules via attributes or registry entries, guiding canonical ID resolution without per-entity resolution code.

7. An adapter auto-registration mechanism via `[FlowAdapter]` attribute discovery on `BackgroundService` types, with environment-aware auto-start (enabled in containers, disabled locally) and configurable include/exclude lists using "system:adapter" identifiers.

---

## 5. Implementation Evidence

- **ADR:** `docs/decisions/FLOW-0101-bindings-canonical-ids-and-vo-ingest.md`
- **ADR:** `docs/decisions/FLOW-0105-external-id-translation-adapter-identity-and-normalized-payloads.md`
- **ADR:** `docs/decisions/FLOW-0106-flow-adapter-auto-scan-and-minimal-boot.md`
- **Framework Version:** Koan Framework v0.6.3 (proposed, pre-implementation)
- **Related:** Flow/Canon pillar architecture

---

## 6. Publication Notice

This document is published as a defensive disclosure to establish prior art. The inventor(s) dedicate this disclosure to the public domain and assert no patent rights over the described inventions. All rights to use, implement, and build upon these inventions are hereby granted to the public.

---

## Antagonist Review Log

### Pass 1
**Antagonist:** External ID envelopes are just message headers/metadata. Every message broker supports headers. Kafka has record headers. This is not novel.

**Author revision:** The invention is not message headers generically — it's the specific convention `identifier.external.<system>` with an indexed backing store `(entityKey, system, externalId) → canonicalId` integrated into a pipeline's canonical ID resolution. Message broker headers are unstructured and have no backing index. The envelope convention + index + pipeline integration form a system, not just metadata attachment.

### Pass 2
**Antagonist:** `[ParentKey]` is just a foreign key annotation. Every ORM has `[ForeignKey]`.

**Author revision:** ORM foreign keys reference internal IDs. `[ParentKey]` triggers external-to-canonical ID resolution through the ExternalId index, with automatic fallback to park-and-sweep when the parent hasn't been ingested yet. An ORM foreign key fails on missing reference; `[ParentKey]` gracefully defers resolution. The resolution-then-park chain is the inventive step, not the annotation itself.

### Pass 3
**Antagonist:** No further objections. The envelope convention + index + parent resolution with parking + contractless bag ingestion forms a sufficiently novel system.

### Final Status
✅ CLEARED — Antagonist found no further weaknesses. Safe to publish.
