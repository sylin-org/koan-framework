# Defensive Publication: Adapter Role Attributes for Multi-Adapter Entity Routing with Priority Fallback

## Header Block

- **Title:** Attribute-Based Adapter Role Declaration for Per-Entity, Per-Role Data Provider Routing in Multi-Provider Application Frameworks
- **Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
- **Disclosure Date:** 2026-03-24
- **Field of Invention:** Multi-provider data access routing, specifically methods for declaring per-entity adapter preferences by operational role (document storage, vector search, general data access) via class-level attributes with deterministic priority fallback.
- **Keywords:** adapter role, source adapter, vector adapter, data adapter, multi-provider, entity routing, attribute-based routing, priority fallback, ISP, capability detection, polyglot persistence

---

## 1. Problem Statement

Modern applications increasingly use polyglot persistence — a single entity type may need to be stored in a relational database for CRUD operations and simultaneously indexed in a vector database for similarity search. Existing frameworks force a choice: either the entity is managed by one provider (losing the other capability), or developers must manually coordinate two separate data access layers with independent configuration, routing, and lifecycle management.

ORM frameworks like Entity Framework Core bind an entity type to a single `DbContext`, which maps to a single provider. There is no mechanism to declare that `Product` CRUD operations should use PostgreSQL while `Product` vector search should use Qdrant. Developers must create separate repository abstractions, wire them independently, and manage the routing logic manually.

What is needed is a declarative mechanism where entity classes can specify their preferred adapter for each operational role, with a deterministic fallback chain when preferences are not explicitly declared, all integrated into the framework's existing adapter resolution infrastructure.

---

## 2. Prior Art Summary

**Entity Framework Core:** Each entity maps to exactly one `DbContext` and one provider. To use multiple providers, developers create multiple DbContexts with separate entity configurations. No per-entity, per-role provider selection.

**Spring Data:** Repository interfaces (`CrudRepository`, `MongoRepository`, `ElasticsearchRepository`) are per-provider. An entity can implement multiple repository interfaces, but each must be configured independently. No attribute-based role declaration.

**Hibernate/JPA:** `@Entity` maps to one persistence unit. Multi-database requires multiple `EntityManagerFactory` instances. No role-based adapter selection.

**Django ORM:** `using()` method on querysets selects a database, but selection is per-query, not per-entity-role. No attribute-driven defaults.

**Specific gaps:**
1. No framework provides class-level attributes declaring adapter preference per operational role (source vs. vector vs. general).
2. No framework integrates per-role adapter selection into a unified resolution chain with ambient context overrides.
3. No framework supports coexistence of multiple adapter roles on the same entity type (e.g., CRUD via PostgreSQL + vector search via Qdrant).

---

## 3. Detailed Description of the Invention

### 3.1 Role Attributes

Three attributes declare adapter preferences per operational role:

```
[SourceAdapter("postgres")]     — Declares CRUD/document storage adapter
[VectorAdapter("qdrant")]       — Declares vector search adapter
[DataAdapter("mongodb")]        — Declares general-purpose adapter (fallback)

Each attribute:
  - AttributeTargets.Class, Inherited = true, AllowMultiple = false
  - Contains Provider property (string, required)
  - DataAdapter additionally supports Collection property (optional)
```

### 3.2 Multi-Role Entity Declaration

```
[SourceAdapter("postgres")]    // CRUD → PostgreSQL
[VectorAdapter("qdrant")]      // Vector → Qdrant
public class Product : Entity<Product, Guid>
{
    public string Name { get; set; }
    public string Description { get; set; }
    public float[] Embedding { get; set; }
}
```

Both attributes coexist on the same class. The framework resolves which adapter to use based on the operation type (CRUD vs. vector search).

### 3.3 Resolution Priority Chain

The `AdapterResolver` follows a 5-level priority cascade (first match wins):

```
Level 1: EntityContext.Current.Source → use source's configured adapter
         (Ambient override from AsyncLocal context — highest priority)

Level 2: EntityContext.Current.Adapter → explicit adapter name override
         (Direct adapter specification — overrides all attributes)

Level 3: Role-specific attribute on entity class
         - For CRUD operations: [SourceAdapter] or [DataAdapter]
         - For vector operations: [VectorAdapter] or [DataAdapter]
         (Attribute-declared default for the operation type)

Level 4: "Default" source from DataSourceRegistry
         (Application-wide default when no attribute or context)

Level 5: [ProviderPriority]-ranked IDataAdapterFactory
         (Factory with highest Priority property wins)
```

### 3.4 ProviderPriority Attribute

```
[AttributeUsage(AttributeTargets.Class)]
public sealed class ProviderPriorityAttribute : Attribute
{
    public int Priority { get; }  // Higher value wins; default 0
}
```

Applied to `IDataAdapterFactory` implementations. When multiple factories can handle a request and no higher-priority resolution mechanism matched, the factory with the highest `Priority` is selected.

### 3.5 Role-Aware Resolution Logic

When the framework needs to resolve an adapter for an entity:

```
ResolveForEntity<TEntity>(serviceProvider, sourceRegistry, operationRole):
  1. Check ambient context (Levels 1-2)
  2. If operationRole == VectorSearch:
       attr = typeof(TEntity).GetCustomAttribute<VectorAdapterAttribute>()
       if attr != null: return attr.Provider
  3. If operationRole == Document or CRUD:
       attr = typeof(TEntity).GetCustomAttribute<SourceAdapterAttribute>()
       if attr != null: return attr.Provider
  4. Fallback to DataAdapterAttribute (role-agnostic)
       attr = typeof(TEntity).GetCustomAttribute<DataAdapterAttribute>()
       if attr != null: return attr.Provider
  5. Check DataSourceRegistry for "Default" source
  6. Scan IDataAdapterFactory implementations, sort by ProviderPriority, return highest
```

### 3.6 AI-Specific Adapter Resolution

A separate `AdapterResolver` in the AI module handles AI adapter resolution:
- Finds adapters by capability name (e.g., "chat", "embed", "ocr")
- `ResolveAll()` aggregates across multiple adapters for multi-provider scenarios
- Throws on zero matches or ambiguous resolution

### 3.7 Independent Application

Roles are independently applicable. An entity can have:
- Only `[SourceAdapter]` — CRUD routed explicitly, vector uses default
- Only `[VectorAdapter]` — Vector routed explicitly, CRUD uses default
- Both — Each role routed independently
- Neither — Both use default resolution (Levels 4-5)

---

## 4. Claims-Style Disclosure

1. A system of class-level attributes (`[SourceAdapter]`, `[VectorAdapter]`, `[DataAdapter]`) that declare per-entity adapter preferences for distinct operational roles (document/CRUD storage, vector search, general data access), distinct from ORM entity mapping in that multiple adapter roles coexist on the same entity class.

2. A role-aware adapter resolution mechanism that selects the appropriate attribute based on the operation type being performed (CRUD vs. vector search), falling through a deterministic 5-level priority chain when the role-specific attribute is absent, distinct from per-entity provider binding in that the same entity routes to different providers depending on the operation.

3. A `[ProviderPriority]` attribute on adapter factory implementations that establishes a ranked fallback when no entity-level attribute or ambient context specifies an adapter, providing deterministic resolution without explicit configuration.

4. A combined system wherein ambient context overrides (Levels 1-2), role-specific entity attributes (Level 3), application defaults (Level 4), and factory priority ranking (Level 5) compose into a single deterministic resolution chain, enabling polyglot persistence without manual per-entity, per-operation routing code.

5. A method wherein an entity decorated with both `[SourceAdapter("postgres")]` and `[VectorAdapter("qdrant")]` transparently routes CRUD operations to PostgreSQL and vector search operations to Qdrant through the same entity API (`Product.Get()` vs. `Product.VectorSearch()`), without requiring separate repository abstractions or manual coordination.

6. An AI-specific adapter resolution variant that resolves adapters by capability name rather than provider name, supporting multi-adapter aggregation via `ResolveAll()` for scenarios requiring responses from multiple AI providers.

---

## 5. Implementation Evidence

- **SourceAdapterAttribute:** `src/Koan.Data.Abstractions/SourceAdapterAttribute.cs`
- **VectorAdapterAttribute:** `src/Koan.Data.Abstractions/VectorAdapterAttribute.cs` and `src/Koan.Data.Vector.Abstractions/VectorAdapterAttribute.cs`
- **DataAdapterAttribute:** `src/Koan.Data.Abstractions/DataAdapterAttribute.cs`
- **ProviderPriorityAttribute:** `src/Koan.Data.Abstractions/ProviderPriorityAttribute.cs`
- **AdapterResolver (Data):** `src/Koan.Data.Core/AdapterResolver.cs`
- **AdapterResolver (AI):** `src/Koan.AI/Resolution/AdapterResolver.cs`
- **ADR:** `docs/decisions/DATA-0058-adapter-role-attributes.md`
- **Framework Version:** Koan Framework v0.6.3

---

## 6. Publication Notice

This document is published as a defensive disclosure to establish prior art. The inventor(s) dedicate this disclosure to the public domain and assert no patent rights over the described inventions. All rights to use, implement, and build upon these inventions are hereby granted to the public.

---

## Antagonist Review Log

### Pass 1
**Antagonist:** Per-entity adapter routing via attributes is just configuration metadata — this is equivalent to putting a "database" key in a YAML config file per entity. The attribute is syntactic sugar for configuration.

**Author revision:** The distinction is that attributes participate in a compiled, type-safe, role-aware resolution chain with inheritance (`Inherited = true`). Configuration files are stringly-typed and don't support role-based selection (the same config key can't mean "use postgres for CRUD but qdrant for vector"). The attribute is read at resolution time within a priority cascade that includes ambient context, making it a fallback in a layered system rather than a static configuration binding.

### Pass 2
**Antagonist:** The 5-level priority chain is just a series of if-else checks. Any developer could write this.

**Author revision:** The individual levels are straightforward. The inventive contribution is the design of the complete cascade — particularly the interaction between ambient context (dynamic, per-request), entity attributes (static, per-class), application defaults (static, per-app), and factory priority (static, per-assembly). The decision that ambient context overrides attributes (not the other way around) enables testing, migration, and multi-tenant scenarios. This ordering is a design decision, not an implementation challenge.

### Pass 3
**Antagonist:** No further objections. The role-based attribute system with the specific 5-level cascade, combined with the ability for multiple roles to coexist on a single entity, is sufficiently described.

### Final Status
✅ CLEARED — Antagonist found no further weaknesses. Safe to publish.
