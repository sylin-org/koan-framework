# Defensive Publication

## Dual-Mode AI Embedding with Convention-Inferred Defaults and Attribute-Gated Lifecycle

**Publication Type:** Defensive Publication (Prior Art Establishment)
**Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
**Date of Disclosure:** 2026-03-24
**Framework:** Koan Framework v0.6.3, .NET 10
**Repository:** https://github.com/koan-framework (private; source excerpts provided herein)
**Related ADR:** AI-0021 — Category-Driven AI with Convention-Inferred Defaults

---

## 1. Problem Statement

Application frameworks that integrate AI embedding capabilities into entity persistence face a fundamental tension between **ease of adoption** and **production lifecycle control**.

### 1.1 The Zero-Config Barrier

When a framework requires explicit decoration (attributes, annotations, or configuration) before any embedding operation can execute, developers cannot experiment with semantic search or vector similarity during prototyping. Every entity type that a developer wants to embed must first be annotated, even for one-off exploratory calls. This creates an adoption barrier that contradicts the "convention over configuration" principle established by frameworks such as Ruby on Rails and ASP.NET MVC.

### 1.2 The Lifecycle Conflation Problem

Existing approaches treat "this entity can be embedded" and "this entity should be automatically embedded on every persistence event" as the same declaration. A single annotation both enables on-demand embedding operations and activates lifecycle hooks (auto-embed-on-save, change detection, background re-embedding, schema-version-driven bulk re-embedding). This conflation produces two failure modes:

- **Over-triggering:** Developers who add an embedding attribute solely to enable on-demand `Embed(entity)` calls inadvertently activate costly write-path hooks on every `Save()`, generating embedding API traffic proportional to write volume.
- **Under-access:** Developers who omit the attribute to avoid lifecycle overhead lose the ability to perform on-demand embedding entirely, forcing manual property extraction and API calls.

### 1.3 The Metadata Gap

When an entity lacks explicit embedding configuration, frameworks typically return null metadata, throw an exception, or require manual specification of which properties to embed. No prior framework provides a convention-based metadata resolution that (a) never returns null, (b) automatically identifies embeddable content from the entity's type shape, and (c) distinguishes between convention-inferred metadata (on-demand only) and attribute-declared metadata (on-demand plus lifecycle).

### 1.4 The Schema Versioning Problem

When the set of properties included in an embedding changes (e.g., a new field is added to the template, or a property is excluded), all previously stored vectors become stale. No prior framework provides a declarative mechanism to version the embedding schema and trigger bulk re-embedding when the version increments, while keeping this concern entirely separate from the on-demand convention path.

---

## 2. Prior Art Survey

### 2.1 LangChain (Python, TypeScript)

LangChain provides `Embeddings` classes (OpenAIEmbeddings, HuggingFaceEmbeddings) that accept raw text strings. Document loading and text splitting are separate pipeline stages. There is no entity-aware embedding: the developer manually extracts text from domain objects, passes it to the embeddings API, and manages vector storage. No convention scanning, no attribute-gated lifecycle, no schema versioning.

- **Key gap:** Every embedding call requires explicit text extraction. No type-introspection-based convention. No lifecycle integration with persistence.

### 2.2 Semantic Kernel (Microsoft, .NET)

Semantic Kernel provides `ITextEmbeddingGenerationService` that accepts `IList<string>`. Memory stores (`IMemoryStore`) and the `TextMemoryPlugin` handle vector persistence. Developers manually compose the text to embed and manually invoke the embedding service. The `KernelMemory` abstraction provides document ingestion with chunking, but operates on documents (files, URLs), not on domain entity types.

- **Key gap:** No entity-type-aware metadata resolution. No convention scanning of entity properties. No attribute that distinguishes on-demand from lifecycle embedding. No schema versioning.

### 2.3 Entity Framework Core with pgvector

EF Core with the `Npgsql.EntityFrameworkCore.PostgreSQL` pgvector extension allows mapping `Vector` properties on entities. The developer manually populates the vector property before calling `SaveChanges()`. There is no automatic embedding: the framework stores vectors but does not generate them. There is no convention for which text properties should be embedded, no lifecycle hook for auto-embedding, and no schema versioning.

- **Key gap:** Vector storage only, not vector generation. No text composition from entity properties. No convention vs. attribute distinction. No lifecycle hooks.

### 2.4 Spring AI (Java)

Spring AI provides `EmbeddingClient` for generating embeddings from text. The `VectorStore` abstraction handles storage. Document objects carry content and metadata, but the framework does not introspect JPA entity types to determine embeddable properties. Developers manually construct `Document` objects from their domain entities.

- **Key gap:** No entity annotation for embedding lifecycle. No convention-based property scanning. No dual-mode (convention vs. attribute) distinction.

### 2.5 Milvus / Weaviate / Qdrant Client SDKs

Vector database client SDKs provide low-level vector CRUD operations. Some (Weaviate) support schema definitions that describe which fields to vectorize, but these schemas are database-side configurations, not application-side entity metadata. None provide application-framework-level convention scanning or persistence lifecycle integration.

- **Key gap:** Database-side schema, not application-side entity metadata. No convention inference from runtime type reflection. No attribute-gated lifecycle hooks in the application persistence pipeline.

### 2.6 Summary of Gaps in Prior Art

No surveyed system provides the combination of:

1. A metadata resolver that never returns null, falling back to convention-based property scanning when no explicit annotation exists.
2. A clear boundary where the annotation gates only lifecycle integration (auto-embed-on-save, change detection, background processing, schema versioning), while on-demand operations work universally via convention.
3. Template-based text composition declarable via the same annotation.
4. A schema version field on the annotation that triggers bulk re-embedding when incremented.
5. Per-property opt-out (`[EmbeddingIgnore]`) within the convention scanning path.

---

## 3. Detailed Description of the Invention

### 3.1 Architecture Overview

The invention introduces a **dual-mode embedding metadata resolution system** within an entity-first application framework. The system comprises:

- **`EmbeddingMetadata`**: An immutable metadata descriptor resolved per entity type, cached via `ConcurrentDictionary`. Resolution never returns null.
- **`EmbeddingAttribute`**: A class-level CLR attribute that opts into lifecycle integration and provides explicit embedding configuration.
- **`EmbeddingIgnoreAttribute`**: A property-level CLR attribute that excludes individual properties from convention scanning.
- **`EmbeddingPolicy`** enum: Governs text composition strategy (`AllStrings`, `Explicit`, `Template`).
- **Convention scanner**: Reflection-based introspection that identifies embeddable content from the entity's type shape without any annotation.
- **Lifecycle gate**: A boolean flag (`LifecycleEnabled`) on the resolved metadata that controls whether persistence hooks fire.

### 3.2 Resolution Algorithm

The `EmbeddingMetadata.Resolve<T>()` static method is the single entry point for all embedding metadata queries. It implements the following resolution chain:

```
Resolve<T>()
  |
  +-- Check ConcurrentDictionary cache for typeof(T)
  |     |
  |     +-- Cache hit: return cached EmbeddingMetadata
  |     |
  |     +-- Cache miss: compute and cache
  |           |
  |           +-- Check: does T have [Embedding] attribute?
  |           |     |
  |           |     +-- YES: FromAttribute(type, attr)
  |           |     |     - Read Policy, Template, SchemaVersion, Include, Exclude
  |           |     |     - Resolve property list per Policy
  |           |     |     - Set LifecycleEnabled = true
  |           |     |     - Return EmbeddingMetadata
  |           |     |
  |           |     +-- NO: FromConvention(type)
  |           |           - Scan public instance properties where PropertyType == string
  |           |           - Exclude properties marked [EmbeddingIgnore]
  |           |           - Set Policy = AllStrings
  |           |           - Set LifecycleEnabled = false
  |           |           - Set Template = null
  |           |           - Set SchemaVersion = 0
  |           |           - Return EmbeddingMetadata
  |           |
  |           +-- Store in ConcurrentDictionary
  |
  +-- Return EmbeddingMetadata (never null)
```

### 3.3 Convention Path (No Attribute)

When an entity type lacks the `[Embedding]` attribute, the system performs convention-based property scanning:

```csharp
private static EmbeddingMetadata FromConvention(Type type)
{
    var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.PropertyType == typeof(string))
        .Where(p => p.GetCustomAttribute<EmbeddingIgnoreAttribute>() == null)
        .ToList();
    return new EmbeddingMetadata
    {
        Policy = EmbeddingPolicy.AllStrings,
        Properties = props,
        LifecycleEnabled = false,
        Template = null,
        SchemaVersion = 0
    };
}
```

**Key behaviors of the convention path:**

- All public string properties are included unless explicitly excluded via `[EmbeddingIgnore]`.
- `LifecycleEnabled` is `false`: the persistence pipeline's `Save()` method does not trigger auto-embedding.
- `SchemaVersion` is `0`: no schema tracking occurs (there is no declared schema to version).
- `Template` is `null`: text composition concatenates property values with newline separators.
- On-demand operations (`Client.Embed(entity)`, `SemanticSearch<T>()`) work without restriction.

**Fallback for entities with no string properties:**

When the convention scanner finds zero string properties, the system falls back to JSON serialization of all public readable properties. If the entity has no public readable properties at all, the operation fails with a clear diagnostic message identifying the type and the problem.

### 3.4 Attribute Path (Explicit Opt-In)

When an entity type carries the `[Embedding]` attribute, the system reads explicit configuration:

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class EmbeddingAttribute : Attribute
{
    public EmbeddingPolicy Policy { get; init; } = EmbeddingPolicy.AllStrings;
    public string? Template { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public string[]? Include { get; init; }
    public string[]? Exclude { get; init; }
}
```

**Key behaviors of the attribute path:**

- `LifecycleEnabled` is `true`: the persistence pipeline's `Save()` method triggers auto-embedding.
- `SchemaVersion` enables version tracking. When the developer increments the version (e.g., from `1` to `2`), stored embeddings with version `1` are detected as stale, and a bulk re-embedding process is triggered.
- `Template` enables structured text composition (e.g., `"{Title}\n\n{Description}"`) using a lightweight template engine that resolves property references.
- `Include`/`Exclude` arrays provide fine-grained property selection when `Policy` is not `Template`.
- Change detection (SHA256 hash of composed text) is active, preventing redundant embedding API calls when an entity is saved without textual changes.
- Background processing (`Async = true` on the attribute, if configured) delegates embedding to a background worker queue.
- `EmbeddingState<T>` tracking records embedding status (Pending, Complete, Failed, Stale) per entity instance.

### 3.5 The Lifecycle Gate Mechanism

The critical innovation is the **single boolean gate** (`LifecycleEnabled`) that separates on-demand operations from lifecycle hooks:

| Operation | Convention (`LifecycleEnabled = false`) | Attribute (`LifecycleEnabled = true`) |
|-----------|----------------------------------------|---------------------------------------|
| `Client.Embed(entity)` | Works. Uses convention-scanned properties. | Works. Uses attribute-configured properties/template. |
| `Client.Embed("raw text")` | Works (no entity metadata involved). | Works (no entity metadata involved). |
| `SemanticSearch<T>()` | Works. Query text composed via convention. | Works. Query text composed via attribute config. |
| `entity.Save()` auto-embed | **Does not trigger.** | Triggers. Composes text, generates embedding, stores vector. |
| Change detection (SHA256) | **Inactive.** | Active. Skips embedding if text unchanged. |
| `EmbeddingState<T>` tracking | **Inactive.** | Active. Tracks Pending/Complete/Failed/Stale. |
| Schema version check | **Inactive.** (SchemaVersion = 0) | Active. Stale vectors flagged when version increments. |
| Background re-embedding | **Inactive.** | Active when configured. Failed/stale embeddings retried. |

This gate ensures that:

- **Prototyping is frictionless.** A developer can call `Client.Embed(entity)` on any entity type without prior setup. The framework infers what to embed from the type's shape.
- **Production is explicit.** Lifecycle hooks that generate API costs on every write require a deliberate opt-in via the `[Embedding]` attribute.
- **The transition is additive.** Moving from convention to attribute requires adding one attribute. No code changes to on-demand call sites. No behavioral regression.

### 3.6 Text Composition

The `ComposeText(object entity)` method on `EmbeddingMetadata` provides unified text generation for both paths:

```csharp
public string ComposeText(object entity)
{
    if (Template != null)
        return TemplateEngine.Render(Template, entity);
    return string.Join("\n", Properties.Select(p => p.GetValue(entity)?.ToString() ?? ""));
}
```

- **Convention path:** Concatenates all scanned string property values with `"\n"` separator. Null values are rendered as empty strings.
- **Template path:** Renders a user-defined template string with `{PropertyName}` placeholders resolved against the entity instance.
- **Explicit path:** Concatenates only the properties specified in `Include` (or all minus `Exclude`), with `"\n"` separator.

### 3.7 Schema Versioning and Bulk Re-Embedding

The `SchemaVersion` property on `[Embedding]` serves as a declarative staleness signal:

1. Developer changes the embedding template from `"{Title}"` to `"{Title}\n\n{Description}"`.
2. Developer increments `SchemaVersion` from `1` to `2`.
3. On application startup or next background sweep, the framework compares stored vector schema versions against the declared version.
4. All entities with stored vectors at version `1` are flagged as stale in `EmbeddingState<T>`.
5. A background worker re-embeds stale entities using the new template at version `2`.

Convention-path entities (no attribute) have `SchemaVersion = 0`, which is a sentinel value meaning "no versioning." These entities never participate in version-based staleness detection because their embedding is always on-demand and ephemeral.

### 3.8 Integration with Category-Driven AI Routing

The embedding metadata system operates within a broader category-driven AI architecture (ADR AI-0021) that provides:

- **Per-category routing:** Embedding operations route independently from chat operations. `Client.Embed()` can use OpenAI while `Client.Chat()` uses a local Ollama instance.
- **Seven-level model resolution chain:** (1) Explicit call-site model, (2) ambient scope override, (3) recipe binding from `IAiRecipeProvider`, (4) orchestrator recommendation from `IAiModelAdvisor`, (5) category configuration, (6) source/member default, (7) hardcoded fallback.
- **Scoped routing:** `Client.Scope(embed: "openai-prod")` overrides the embedding source for all operations within the scope, without affecting chat or OCR routing.

The convention-inferred metadata integrates transparently with this routing chain: the `Resolve<T>()` call returns metadata that `Client.Embed(entity)` uses to compose text, which is then routed through whichever embedding source the category router selects.

### 3.9 Implementation Evidence

The following source files in the Koan Framework codebase implement or directly reference this invention:

| File | Role |
|------|------|
| `src/Koan.Data.AI/EmbeddingMetadata.cs` | Core metadata resolver with convention fallback |
| `src/Koan.Data.AI/EntityEmbeddingExtensions.cs` | `SemanticSearch<T>()` using `Resolve<T>()` |
| `src/Koan.AI/Pipeline/AiCategoryRouter.cs` | Category-aware routing for Embed category |
| `src/Koan.AI/Pipeline/AiRecipeProvider.cs` | Recipe bindings in model resolution chain |
| `src/Koan.AI/ServiceCollectionExtensions.cs` | DI registration of routing pipeline |
| `src/Koan.Core/AI/IAiModelAdvisor.cs` | Model advisor SPI (level 4 in resolution chain) |
| `src/Koan.Core/AI/IAiRecipeProvider.cs` | Recipe provider SPI (level 3 in resolution chain) |
| `src/Koan.ZenGarden/AI/ZenGardenModelAdvisor.cs` | Orchestrator-backed model advisor implementation |
| `docs/decisions/AI-0021-category-driven-ai-with-convention-defaults.md` | Architectural decision record (Proposed, 2026-02-08) |
| `docs/decisions/AI-0032-intent-capability-resolution-with-recipes.md` | Recipe layer ADR extending resolution chain |

The `AiCategoryRouter` source (lines 72-112) demonstrates the seven-level model resolution chain, including recipe and advisor consultation, that operates on metadata produced by `EmbeddingMetadata.Resolve<T>()`.

The `ZenGardenModelAdvisor` source (lines 84-101) demonstrates the runtime model recommendation SPI that provides level-4 resolution for embedding model selection, consumed by the same routing pipeline.

---

## 4. Claims

The following claims describe the novel aspects of this invention. They are disclosed defensively to establish prior art and prevent others from obtaining exclusionary patent rights.

### Claim 1: Never-Null Convention-Inferred Embedding Metadata

A method for resolving embedding metadata for an arbitrary entity type in an object-oriented programming language, wherein:

(a) A static resolution method (`Resolve<T>()`) accepts a generic type parameter and returns a non-null metadata descriptor.

(b) When the entity type carries a framework-defined class-level attribute (`[Embedding]`), the metadata is constructed from the attribute's properties (policy, template, schema version, inclusion/exclusion lists).

(c) When the entity type does not carry the attribute, the metadata is constructed by convention: scanning the type's public instance properties for string-typed properties, excluding any marked with a property-level opt-out attribute (`[EmbeddingIgnore]`).

(d) The resolution result is cached per type in a thread-safe concurrent dictionary, ensuring the reflection cost is incurred at most once per type per application lifetime.

(e) The convention path sets a lifecycle-enabled flag to `false`, ensuring that persistence hooks do not fire for convention-resolved entities.

### Claim 2: Attribute as Lifecycle Gate, Not Operation Gate

A system for managing AI embedding operations on persistent entities, wherein:

(a) On-demand embedding operations (explicit `Embed(entity)` calls, semantic search query composition) are available for all entity types regardless of attribute presence, using convention-inferred metadata when no attribute exists.

(b) Persistence lifecycle hooks (auto-embed-on-save, change detection, background re-embedding, state tracking) are activated exclusively by the presence of an explicit class-level attribute.

(c) The attribute's single gating responsibility is lifecycle integration; it does not gate on-demand operations.

(d) The transition from convention mode (on-demand only) to attribute mode (on-demand plus lifecycle) requires only adding the attribute to the class declaration, with no changes to existing on-demand call sites.

### Claim 3: Template-Based Text Composition with Convention Fallback

A method for composing embedding input text from entity instances, wherein:

(a) When a template string is declared on the embedding attribute (e.g., `"{Title}\n\n{Description}"`), a template engine resolves property-name placeholders against the entity instance.

(b) When no template is declared but specific properties are selected (via inclusion/exclusion lists), the selected property values are concatenated with a separator.

(c) When neither template nor explicit selection is declared (convention path), all public string property values are concatenated with a newline separator, with null values rendered as empty strings.

(d) A single `ComposeText(object entity)` method on the metadata descriptor dispatches to the appropriate strategy based on the resolved metadata, providing a uniform interface regardless of resolution path.

### Claim 4: Declarative Schema Versioning for Bulk Re-Embedding

A mechanism for detecting and resolving stale embeddings when the text composition schema changes, wherein:

(a) The embedding attribute carries an integer `SchemaVersion` property (default `1` for attributed entities, sentinel `0` for convention-resolved entities).

(b) When an entity's embedding is stored, the active schema version is persisted alongside the vector.

(c) When the developer increments the declared schema version, a comparison against stored versions identifies all entities whose vectors were generated under a prior schema.

(d) Stale entities are flagged in a state tracker (`EmbeddingState<T>`) and processed by a background worker that re-embeds them using the current schema.

(e) Convention-resolved entities (schema version `0`) are excluded from version-based staleness detection because their embeddings are ephemeral (on-demand only, not persisted by lifecycle hooks).

### Claim 5: Per-Property Opt-Out Within Convention Scanning

A property-level attribute (`[EmbeddingIgnore]`) that excludes individual properties from convention-based embedding metadata scanning, wherein:

(a) The attribute is only meaningful in the convention path; attributed entities use their declared policy (Template, Explicit, or AllStrings with Include/Exclude arrays) to determine property selection.

(b) The exclusion applies during the one-time reflection scan and is cached with the resulting metadata.

(c) Common use cases include excluding audit fields (CreatedBy, ModifiedDate), system identifiers, or properties containing markup/code that would degrade embedding quality.

### Claim 6: Dual-Mode Metadata Within Category-Driven AI Routing

Integration of the dual-mode embedding metadata system with a category-driven AI routing architecture, wherein:

(a) The embedding category ("Embed") is one of multiple independently-routed AI categories (Chat, Embed, Ocr), each with its own source, model, and adapter resolution.

(b) The metadata resolved by `EmbeddingMetadata.Resolve<T>()` composes the text input, which is then routed through the embedding category's resolution chain: call-site model, scope override, recipe binding, orchestrator advisor, category configuration, source default, hardcoded fallback.

(c) Convention-inferred metadata and attribute-declared metadata produce identical downstream routing behavior; the routing chain is metadata-source-agnostic.

(d) Scoped routing (`Client.Scope(embed: "source-name")`) overrides the embedding source for all operations within the scope without affecting the metadata resolution path.

### Claim 7: Convention Fallback Chain for Non-String Entities

A multi-step fallback chain for convention-based embedding when the primary string-property scan yields no candidates, wherein:

(a) Primary: scan for public string-typed instance properties, excluding `[EmbeddingIgnore]`-marked properties.

(b) Secondary: if no string properties are found, serialize all public readable properties to JSON.

(c) Terminal: if the entity has no public readable properties, fail with a diagnostic message identifying the type and the absence of embeddable content.

(d) This chain ensures that `Resolve<T>()` produces actionable metadata for the widest possible range of entity types without requiring any annotation.

### Claim 8: Change Detection Gated by Lifecycle Flag

A mechanism for avoiding redundant embedding API calls, wherein:

(a) When `LifecycleEnabled` is `true` (attribute path), the persistence pipeline computes a SHA256 hash of the composed text before generating an embedding.

(b) The hash is compared against the stored hash from the previous embedding operation.

(c) If the hashes match, the embedding API call is skipped, avoiding cost and latency.

(d) When `LifecycleEnabled` is `false` (convention path), change detection is inactive because there is no persistence hook to trigger it and no stored hash to compare against.

(e) This ensures that change detection overhead is incurred only for entities that have explicitly opted into lifecycle integration.

### Claim 9: Additive Transition from Convention to Attribute

A software design pattern wherein the transition from zero-configuration convention-based embedding to fully-configured attribute-based embedding is strictly additive:

(a) Adding `[Embedding]` to a class that previously relied on convention does not change the behavior of existing `Client.Embed(entity)` call sites (they continue to work, now using attribute-configured metadata instead of convention-inferred metadata).

(b) Adding `[Embedding]` activates lifecycle hooks that were previously inactive, but does not deactivate any previously-available on-demand operations.

(c) Removing `[Embedding]` from a class that previously had it deactivates lifecycle hooks but preserves on-demand operation availability via convention fallback.

(d) At no point does the presence or absence of the attribute create a state where on-demand embedding operations fail solely due to missing annotation.

### Claim 10: Seven-Level Model Resolution with Metadata-Agnostic Routing

A model resolution chain for AI embedding operations that operates independently of the metadata resolution path, comprising seven ordered levels:

(a) Level 1: Explicit model specified on the embedding call site (`EmbedOptions.Model`).

(b) Level 2: Ambient model override from a scoped routing context (`AiCategoryScope`).

(c) Level 3: Recipe binding from a named recipe configuration (`IAiRecipeProvider.GetModel("Embed")`), authored by ML engineers or DevOps specialists.

(d) Level 4: Runtime model recommendation from an orchestrator advisor (`IAiModelAdvisor.GetRecommendedModel("Embed")`), based on available compute resources and model benchmarks.

(e) Level 5: Category-level configuration (`Koan:Ai:Embed:Model` in application settings).

(f) Level 6: Source/member default model from the resolved AI source definition.

(g) Level 7: Hardcoded fallback model.

Each level returns null to defer to the next level. The chain operates identically regardless of whether the embedding text was composed via convention-inferred or attribute-declared metadata.

---

## 5. Implementation Evidence

### 5.1 Source Code Provenance

The invention is implemented in the Koan Framework, a .NET 10 application framework. The relevant source files are enumerated in Section 3.9. The architectural decision record AI-0021 (dated 2026-02-08, status: Proposed) describes the convention-inferred defaults system. The `AiCategoryRouter` implementation (committed to the `dev` branch as of 2026-03-24) demonstrates the seven-level model resolution chain including recipe and advisor integration.

### 5.2 Key Implementation Characteristics

- **Thread-safe caching:** `ConcurrentDictionary<Type, EmbeddingMetadata>` with `GetOrAdd` and a static lambda to avoid closure allocation on the hot path.
- **Reflection minimized:** Property scanning occurs once per type per application lifetime. Subsequent calls return the cached descriptor.
- **Immutable metadata:** `EmbeddingMetadata` uses `init`-only properties, ensuring that cached instances cannot be mutated after creation.
- **Category router integration:** The `AiCategoryRouter.Resolve("Embed", ...)` method (lines 72-112 of `AiCategoryRouter.cs`) merges scope overrides, recipe bindings, advisor recommendations, and category configuration into a single `AiRouteResolution` that carries the effective model for the embedding operation.

### 5.3 Framework Version

Koan Framework v0.6.3, targeting .NET 10 (`net10.0`). The embedding metadata system is part of the `Koan.Data.AI` assembly. The category routing system is part of the `Koan.AI` assembly.

---

## 6. Publication Notice

This document is a **defensive publication** intended to establish prior art and prevent the granting of exclusionary patent rights on the described techniques. The inventor disclaims any intent to seek patent protection on the claims described herein.

By publishing this document, the inventor makes the described techniques available as prior art under 35 U.S.C. 102 (United States), Article 54 EPC (European Patent Convention), and equivalent provisions in other jurisdictions.

Any person or organization is free to implement the techniques described in this publication. This publication does not grant or imply any license to any existing patents or patent applications.

**Signed:** Leo Botinelly (Leonardo Milson Botinelly Soares)
**Date:** 2026-03-24

---

## Appendix A: Antagonist Cycle Review

### A.1 Devil's Advocate: "Convention scanning is obvious"

**Challenge:** Scanning public string properties via reflection is a straightforward application of the convention-over-configuration pattern. Any competent developer would arrive at this approach.

**Response:** The novelty is not in reflection-based property scanning alone. It is in the **dual-mode resolution where the same entry point (`Resolve<T>()`) never returns null, produces functionally distinct metadata depending on attribute presence, and uses a single boolean flag to gate an entire lifecycle pipeline.** The convention scanning is one component of a system whose novelty lies in the interaction between convention inference, lifecycle gating, schema versioning, and category-driven routing.

### A.2 Devil's Advocate: "EF Core conventions are prior art for reflection-based property scanning"

**Challenge:** Entity Framework Core uses conventions to discover entity properties, configure column mappings, and establish relationships. The pattern of "scan properties, apply defaults, allow attribute overrides" is well-established.

**Response:** EF Core conventions operate on the persistence schema (column types, relationships, indexes). They do not produce AI embedding metadata, do not gate AI lifecycle hooks, do not compose embedding text via templates, and do not version embedding schemas. The application of the convention-over-configuration pattern to a fundamentally different domain (AI embedding with lifecycle gating) is the contribution, not the pattern itself.

### A.3 Devil's Advocate: "The lifecycle gate is just a boolean flag"

**Challenge:** Using a boolean flag to enable/disable behavior is a trivial programming technique.

**Response:** The novelty is in **where the flag is set and what it controls.** The flag is set by the metadata resolution path (convention sets it false, attribute sets it true) and controls an entire lifecycle pipeline (auto-embed-on-save, change detection, schema versioning, background re-embedding, state tracking). The design decision that "attribute presence = lifecycle opt-in, attribute absence = on-demand only" is an architectural choice that prevents the over-triggering and under-access failure modes described in Section 1.2. The flag is the mechanism; the dual-mode resolution producing the flag value is the invention.

### A.4 Devil's Advocate: "Schema versioning exists in database migrations"

**Challenge:** Schema versioning is a well-known concept in database migration tools (EF Core Migrations, Flyway, Liquibase). Incrementing a version number to trigger a migration is standard practice.

**Response:** The novelty is in applying schema versioning to **embedding text composition schemas** (the set of properties and template used to generate embedding input text), not to database table schemas. When an embedding template changes, all previously generated vectors become semantically stale even though the underlying data has not changed. The mechanism of detecting this staleness via a version number on the embedding attribute, and triggering bulk re-embedding (not database migration), is specific to the AI embedding domain. The exclusion of convention-path entities (version 0 sentinel) from this mechanism is a further novel design choice.

### A.5 Devil's Advocate: "The seven-level resolution chain is over-engineered"

**Challenge:** Seven levels of model resolution is excessive. Most systems need at most two or three levels (explicit, configured, default).

**Response:** This criticism addresses the broader category-driven AI routing architecture (ADR AI-0021), not the dual-mode embedding metadata system specifically. However, the interaction between the metadata system and the routing chain is relevant: the metadata system is **routing-agnostic** (Claims 6 and 10). Convention-inferred and attribute-declared metadata produce identical downstream routing behavior. This separation of concerns is a deliberate design choice that allows the metadata system and the routing system to evolve independently. The seven levels exist to support real production scenarios: ML engineers curating recipe bindings (level 3), infrastructure orchestrators recommending models based on GPU availability (level 4), and operations teams setting category defaults (level 5).

### A.6 Devil's Advocate: "LangChain document loaders do convention-based text extraction"

**Challenge:** LangChain's document loaders extract text from various file formats (PDF, HTML, CSV) using format-specific conventions. This is analogous to convention-based property scanning.

**Response:** LangChain document loaders operate on **file formats**, not on **runtime entity types**. They extract text from files using format-specific parsers (PDF parser, HTML parser), not by reflecting on CLR/JVM type metadata. They do not produce cached per-type metadata descriptors, do not distinguish between convention and attribute paths, and do not gate lifecycle hooks. The analogy is superficial: both "extract text by convention," but the domains (file formats vs. entity type shapes), mechanisms (parsers vs. reflection), and gating behaviors (none vs. lifecycle flag) are entirely different.

### A.7 Strengthened Claims After Antagonist Review

After adversarial review, the following aspects are confirmed as novel in combination:

1. **Never-null resolution with dual-mode output** (convention-inferred vs. attribute-declared) from a single generic method.
2. **Lifecycle gating by metadata source** (convention = on-demand only, attribute = on-demand + lifecycle) via a single boolean flag set during resolution.
3. **Schema versioning for embedding text composition** (not database schemas) with sentinel exclusion for convention-path entities.
4. **Additive transition** from convention to attribute with no behavioral regression on existing call sites.
5. **Metadata-source-agnostic routing** where the seven-level model resolution chain operates identically regardless of whether metadata was convention-inferred or attribute-declared.
