# Defensive Publication: Unified Multi-Format Patch Normalization

## 1. Header

| Field | Value |
|---|---|
| **Title** | Unified Multi-Format Patch Normalization with Content-Type Detection, Configurable Null Semantics, and Single-Endpoint Convergence |
| **Inventor** | Leo Botinelly (Leonardo Milson Botinelly Soares) |
| **Publication Date** | 2026-03-24 |
| **Framework** | Koan Framework v0.6.3 (.NET, target net10.0) |
| **Repository** | github.com/koan-framework (private; source excerpts included below) |
| **Classification** | Software Architecture -- Web API Design -- HTTP PATCH Normalization |
| **Status** | PUBLISHED -- This document is a defensive publication intended to constitute prior art and prevent patenting of the described techniques. |

---

## 2. Problem Statement

The HTTP PATCH method (RFC 5789) has no single mandated body format. Three competing conventions have emerged in practice:

1. **RFC 6902 (JSON Patch):** An array of discrete operations (`add`, `remove`, `replace`, `move`, `copy`, `test`) targeting JSON Pointer paths. Uses Content-Type `application/json-patch+json`. Precise and expressive, but verbose for simple field updates.

2. **RFC 7396 (JSON Merge Patch):** A partial JSON document where present keys are set and `null` values mean "remove the field." Uses Content-Type `application/merge-patch+json`. Simple for common cases, but the overloaded null semantics create an ambiguity: there is no way to explicitly set a field to `null` while keeping it present.

3. **Partial JSON (plain `application/json`):** A partial JSON document where present keys are set and `null` values mean "set the field to null" (not remove it). No dedicated RFC governs this convention, but it is the intuitive interpretation that many API consumers expect when sending `application/json` to a PATCH endpoint.

These three formats create concrete problems for framework authors and API consumers:

**Problem 1: Null semantics divergence.** The same JSON payload `{"description": null}` has opposite meaning under RFC 7396 (remove the `description` field) versus Partial JSON (set `description` to null). Most frameworks force a single interpretation, creating silent data corruption when clients switch between formats or misunderstand the semantics.

**Problem 2: Format lock-in.** Existing web frameworks support at most one patch format natively. ASP.NET Core provides `JsonPatchDocument<T>` for RFC 6902 only. Spring Framework has no built-in merge-patch support. Express/Node.js requires manual format detection and handling. API designers must choose one format at design time and reject all others, fragmenting the ecosystem.

**Problem 3: No unified intermediate representation.** Without a common internal format, downstream processing (validation, authorization checks, audit logging, provider-specific persistence) must handle each patch format independently. This multiplies the code surface proportionally with the number of supported formats.

**Problem 4: Per-request null policy is absent.** Even within a single format, different API operations may require different null handling (e.g., an admin endpoint that allows field removal vs. a user endpoint that rejects it). No existing framework provides per-request override of null semantics via query parameters or headers.

**Problem 5: Identity field mutation.** Patch operations that target the entity's identity field (`/id`) are a common source of data corruption. Most frameworks do not block this at the normalization layer, leaving it to ad-hoc validation downstream.

---

## 3. Prior Art Survey

### 3.1 Framework-Level PATCH Support

| Framework / Library | Supported Formats | Null Semantics | Unified IR | Per-Request Null Override | Identity Protection |
|---|---|---|---|---|---|
| **ASP.NET Core** (`Microsoft.AspNetCore.JsonPatch`) | RFC 6902 only | N/A (operations are explicit) | No | No | No |
| **Spring Framework** (`spring-web`) | No built-in PATCH normalization | N/A | No | No | No |
| **Express.js / Node.js** | Manual per format | Developer-defined | No | No | No |
| **Django REST Framework** | Partial JSON only (via serializer partial=True) | Null = set null | No | No | No |
| **Laravel** | Partial JSON only | Null = set null | No | No | No |
| **FastAPI (Python)** | Partial JSON via Pydantic `exclude_unset` | Null = set null | No | No | No |
| **json-merge-patch (npm)** | RFC 7396 only | Null = remove | No | No | No |
| **fast-json-patch (npm)** | RFC 6902 only | N/A (explicit ops) | No | No | No |

### 3.2 RFC and Standard Bodies

| Specification | Scope | Limitation |
|---|---|---|
| **RFC 5789** (PATCH method) | Defines HTTP PATCH semantics | Does not mandate body format; defers to Content-Type |
| **RFC 6902** (JSON Patch) | Defines operation array format | Single format; no merge-patch interop |
| **RFC 7396** (JSON Merge Patch) | Defines merge semantics | Cannot distinguish "set to null" from "remove"; no operation granularity |
| **RFC 6901** (JSON Pointer) | Defines path syntax for 6902 | Path syntax only; no normalization layer |

### 3.3 Key Gaps in Existing Art

No surveyed system provides all of the following in combination:

- **Content-Type-based automatic format detection** routing the same PATCH endpoint to format-specific normalizers
- **A single intermediate representation** (`PatchPayload<TKey>` with `PatchOp` list) that all three formats converge to
- **Configurable null semantics** with per-format policies (`MergePatchNullPolicy`, `PartialJsonNullPolicy`) and per-request query string overrides
- **A static, stateless normalizer** with pure functions and no DI dependencies, enabling thread-safe reuse
- **Identity field mutation blocking** at the normalization/execution layer
- **Recursive nested object walking** that preserves JSON Pointer path construction for merge-patch and partial JSON formats

---

## 4. Detailed Description

### 4.1 Architecture Overview

The system comprises four cooperating layers:

```
   HTTP Request (PATCH /entities/{id})
   Content-Type: application/json-patch+json | application/merge-patch+json | application/json
          |
          v
  +------------------+     ASP.NET Content Negotiation
  | EntityController  |     [Consumes] attribute routing
  | (three overloads) |     dispatches to format-specific action
  +--------+---------+
           |
           v
  +------------------+
  | PatchNormalizer   |     Static, stateless, pure functions
  | (.NormalizeXxx)   |     Three entry points, one shared Walk()
  +--------+---------+
           |
           v
  +------------------+
  | PatchPayload<TKey>|    Unified envelope: Id, Ops, Options, KindHint
  | + PatchOp list    |    Canonical operations: add/replace/remove/move/copy/test
  +--------+---------+
           |
           v
  +------------------+
  | PatchOpsExecutor  |    Applies normalized ops to entity via JSON Pointer
  | (or provider-     |    Identity mutation blocked
  |  native path)     |    Case-insensitive property resolution
  +------------------+
```

### 4.2 Content-Type Routing via [Consumes] Attribute

The `EntityController<TEntity, TKey>` declares three `[HttpPatch("{id}")]` action methods on the same route, differentiated solely by the `[Consumes]` attribute:

| Action Method | Content-Type | Normalizer Called |
|---|---|---|
| `PatchJsonPatch` | `application/json-patch+json` | `PatchNormalizer.NormalizeJsonPatch<TEntity, TKey>` |
| `PatchMerge` | `application/merge-patch+json` | `PatchNormalizer.NormalizeMergePatch<TKey>` |
| `PatchPartial` | `application/json` | `PatchNormalizer.NormalizePartialJson<TKey>` |

ASP.NET Core's content negotiation infrastructure examines the incoming `Content-Type` header and routes to the matching action. The merge-patch and partial-JSON overloads are marked `[ApiExplorerSettings(IgnoreApi = true)]` to keep OpenAPI documentation focused on the primary RFC 6902 surface while still accepting all three formats at runtime.

All three actions converge on a single private method `PatchNormalized(TKey id, PatchPayload<TKey> payload, CancellationToken ct)` that handles authorization, set/partition resolution, null policy overrides, entity persistence, and response shaping identically regardless of the originating format.

### 4.3 PatchNormalizer (Static, Stateless)

`PatchNormalizer` is a `public static class` with three public entry points and one private recursive helper. It has no mutable state, no constructor, no dependencies, and no configuration -- all behavior is determined by explicit parameters. This design enables thread-safe concurrent use without synchronization.

**Entry point 1: `NormalizeJsonPatch<TEntity, TKey>(TKey id, JsonPatchDocument<TEntity> doc, PatchOptions options)`**

Iterates the `doc.Operations` collection, mapping each ASP.NET `Operation` to a `PatchOp` record. The `op` string (`add`, `remove`, `replace`, `move`, `copy`, `test`) is preserved verbatim. The `value` field, if non-null, is converted to `JToken` via `JToken.FromObject()` to ensure a uniform representation. The result is wrapped in a `PatchPayload<TKey>` with `KindHint = "json-patch"`.

**Entry point 2: `NormalizeMergePatch<TKey>(TKey id, JToken body, PatchOptions options)`**

Delegates to the shared `NormalizeObjectToOps` with `mergeSemantics: true`. Under merge semantics, `null` values in the input are translated to `PatchOp("remove", path, null, null)`.

**Entry point 3: `NormalizePartialJson<TKey>(TKey id, JToken body, PatchOptions options)`**

Delegates to the shared `NormalizeObjectToOps` with `mergeSemantics: false`. Under partial semantics, `null` values are translated to `PatchOp("replace", path, null, JValue.CreateNull())` -- preserving the null as an explicit value assignment rather than a removal.

**Shared helper: `NormalizeObjectToOps<TKey>(TKey id, JToken body, string kindHint, bool mergeSemantics, PatchOptions options)`**

Implements a recursive `Walk(JToken token, string basePath)` local function:

1. If the token is a `JObject`, iterate its properties. For each property:
   - If the value is itself a `JObject`, recurse with the extended path (`basePath + "/" + propertyName`).
   - If the value is `JTokenType.Null` and `mergeSemantics` is true, emit `PatchOp("remove", path, null, null)`.
   - If the value is `JTokenType.Null` and `mergeSemantics` is false, emit `PatchOp("replace", path, null, JValue.CreateNull())`.
   - Otherwise, emit `PatchOp("replace", path, null, value.DeepClone())`.
2. If the token is a primitive or array at the current path level, emit `PatchOp("replace", basePath, null, token.DeepClone())`.
3. After walking, normalize all paths to start with `/` (JSON Pointer root prefix).

The `DeepClone()` calls ensure the normalized operations are fully detached from the input `JToken` tree, preventing mutation of the original request body.

### 4.4 Canonical Data Model

**PatchPayload<TKey>:**

```
sealed record PatchPayload<TKey>(
    TKey Id,
    string? Set,           // Partition/tenant selector
    string? ETag,          // Optimistic concurrency token
    string? KindHint,      // "json-patch" | "merge-patch" | "partial-json"
    IReadOnlyList<PatchOp> Ops,
    PatchOptions? Options
)
```

**PatchOp:**

```
sealed record PatchOp(
    string Op,             // "add" | "replace" | "remove" | "move" | "copy" | "test"
    string Path,           // JSON Pointer (RFC 6901), e.g., "/name", "/address/city"
    string? From,          // Source path for "move"/"copy" operations
    JToken? Value          // Value for "add"/"replace"/"test"; null for "remove"
)
```

**PatchOptions:**

```
sealed record PatchOptions(
    MergePatchNullPolicy MergeNulls,      // SetDefault | Reject
    PartialJsonNullPolicy PartialNulls,   // SetNull | Ignore | Reject
    ArrayBehavior Arrays                  // Replace (extensible)
)
```

The `KindHint` field is informational -- it records which format produced the payload for audit logging and debugging, but does not influence downstream processing. All downstream code operates on the uniform `Ops` list.

### 4.5 Configurable Null Semantics

**Baseline configuration** is set via `KoanWebOptions` (DI-registered options class):
- `MergePatchNullsForNonNullable`: Controls merge-patch null behavior (`SetDefault` resets to type default; `Reject` throws).
- `PartialJsonNulls`: Controls partial-JSON null behavior (`SetNull` assigns null; `Ignore` skips; `Reject` throws).

**Per-request override** via query string parameters on the PATCH endpoint:

| Query Parameter | Values | Effect |
|---|---|---|
| `?nulls=default` | Global | Sets merge null policy to `SetDefault` |
| `?nulls=reject` | Global | Sets both merge and partial policies to `Reject` |
| `?nulls=null` | Global | Sets partial null policy to `SetNull` |
| `?nulls=ignore` | Global | Sets partial null policy to `Ignore` |
| `?mergeNulls=default\|reject` | Merge-specific | Overrides merge null policy only |
| `?partialNulls=null\|ignore\|reject` | Partial-specific | Overrides partial null policy only |

Specific overrides (`mergeNulls`, `partialNulls`) take precedence over the global `nulls` parameter. When no override is provided, the baseline from `KoanWebOptions` applies. The override mechanism creates a new `PatchOptions` record (immutable) rather than mutating the existing one, using the C# `with` expression on `PatchPayload`.

### 4.6 PatchOpsExecutor (Normalized Application)

`PatchOpsExecutor` is a static class that applies a `PatchPayload<TKey>` against a target entity instance. Key behaviors:

1. **Operation dispatch:** Iterates `payload.Ops` and dispatches on `op.Op.ToLowerInvariant()`:
   - `add` / `replace`: Set value at the JSON Pointer path.
   - `remove`: Remove (null out) the value at the path.
   - `copy` / `move` / `test`: Explicitly unsupported in the fallback executor with `NotSupportedException`.

2. **Identity mutation blocking:** Both `SetValueAtPointer` and `RemoveAtPointer` check if the target path is `/id` (case-insensitive) and throw `InvalidOperationException("Identity mutation is not allowed via patch.")` if so. This prevents clients from changing the entity's primary key through any patch format.

3. **JSON Pointer resolution with case-insensitive matching:** The `ResolveParent` method walks the pointer segments, performing case-insensitive property lookup at each level via `FindExistingPropertyName`. This accommodates the common mismatch between JSON camelCase (`/firstName`) and C# PascalCase (`FirstName`) without requiring explicit mapping configuration.

4. **JSON Pointer escape sequences:** Implements RFC 6901 unescaping (`~1` to `/`, `~0` to `~`).

5. **Intermediate object creation:** If a pointer traverses a path where an intermediate object does not exist, a new `JObject` is created automatically, enabling deep `add` operations without requiring the parent path to pre-exist.

6. **Null propagation:** The `Populate` method uses `JsonMergeSettings` with `MergeNullValueHandling.Merge` to ensure that explicit null values from patch operations flow through to the target entity rather than being silently dropped.

### 4.7 Dual Execution Path

The system supports two execution paths for backward compatibility:

1. **PatchOpsExecutor** (normalized path): Operates on `PatchPayload<TKey>` after normalization. Used by the unified `PatchNormalized` flow in `EntityController`.

2. **PatchApplicators** (legacy path): Factory that creates format-specific `IPatchApplicator<TEntity>` instances (`JsonPatchApplicator`, `MergePatchApplicator`, `PartialJsonApplicator`) from a `PatchRequest` discriminated by `PatchKind` enum. Each applicator applies the patch directly using format-native logic (e.g., `JsonPatchDocument.ApplyTo()` for RFC 6902, recursive `JObject.Merge` for merge patch).

Both paths converge on the same entity persistence layer. The normalized path is preferred for new code; the legacy path remains for scenarios requiring direct format-native application without intermediate conversion.

### 4.8 Normalization Examples

**RFC 6902 input:**

```json
Content-Type: application/json-patch+json

[
  {"op": "replace", "path": "/name", "value": "New Name"},
  {"op": "remove", "path": "/description"},
  {"op": "add", "path": "/tags/0", "value": "urgent"}
]
```

Normalized `PatchPayload.Ops`:

```
PatchOp("replace", "/name",        null, "New Name")
PatchOp("remove",  "/description", null, null)
PatchOp("add",     "/tags/0",      null, "urgent")
```

**RFC 7396 input:**

```json
Content-Type: application/merge-patch+json

{
  "name": "New Name",
  "description": null,
  "address": {
    "city": "Portland"
  }
}
```

Normalized `PatchPayload.Ops`:

```
PatchOp("replace", "/name",         null, "New Name")
PatchOp("remove",  "/description",  null, null)
PatchOp("replace", "/address/city", null, "Portland")
```

Note: Nested objects are recursively walked; `description: null` becomes a `remove` operation under merge semantics.

**Partial JSON input:**

```json
Content-Type: application/json

{
  "name": "New Name",
  "description": null,
  "address": {
    "city": "Portland"
  }
}
```

Normalized `PatchPayload.Ops`:

```
PatchOp("replace", "/name",         null, "New Name")
PatchOp("replace", "/description",  null, null)    // null is VALUE, not removal
PatchOp("replace", "/address/city", null, "Portland")
```

Note: Same input body as merge patch, but `description: null` becomes a `replace` with explicit null value under partial semantics.

---

## 5. Claims

The following claims describe the novel aspects of this invention. They are published defensively to establish prior art and prevent others from obtaining patent protection on these techniques.

**Claim 1.** A method for accepting HTTP PATCH requests in three distinct formats -- RFC 6902 JSON Patch (`application/json-patch+json`), RFC 7396 JSON Merge Patch (`application/merge-patch+json`), and Partial JSON (`application/json`) -- on a single URL endpoint, where the framework's content negotiation infrastructure routes to format-specific action methods based solely on the `Content-Type` header, and all three action methods converge on a shared normalized representation before downstream processing.

**Claim 2.** A static, stateless normalizer class that converts three HTTP PATCH body formats into a unified `PatchPayload<TKey>` intermediate representation comprising a list of `PatchOp` records, where each `PatchOp` contains an operation type (`add`, `replace`, `remove`, `move`, `copy`, `test`), a JSON Pointer path, an optional source path, and an optional value, and where the normalizer's pure functions accept all dependencies as explicit parameters, enabling thread-safe concurrent use without synchronization.

**Claim 3.** A method for differentiating null semantics during patch normalization based on a `mergeSemantics` boolean flag, wherein: (a) when `mergeSemantics` is true, a null value in the input JSON object is normalized to a `remove` operation targeting the corresponding JSON Pointer path; and (b) when `mergeSemantics` is false, a null value is normalized to a `replace` operation with an explicit null value, preserving the distinction between "field removal" and "field set to null" that is ambiguous in the source formats.

**Claim 4.** A recursive object-to-operations walker that traverses a JSON object tree, constructing JSON Pointer paths by concatenating property names at each nesting level, emitting leaf operations for primitives, arrays, and null values, and recursing into nested objects, where the walker serves as the shared implementation for both merge-patch and partial-JSON normalization with behavior differentiated only by the null-handling flag.

**Claim 5.** A configurable null semantics system for HTTP PATCH operations comprising: (a) per-format null policy enumerations (`MergePatchNullPolicy` with `SetDefault` and `Reject` values; `PartialJsonNullPolicy` with `SetNull`, `Ignore`, and `Reject` values); (b) baseline configuration via dependency-injected options; and (c) per-request override via query string parameters (`?nulls=`, `?mergeNulls=`, `?partialNulls=`) where specific overrides take precedence over global overrides, which take precedence over baseline configuration, and where the override produces a new immutable options record rather than mutating the existing one.

**Claim 6.** A patch execution method that blocks identity field mutation by detecting operations targeting the `/id` JSON Pointer path (case-insensitive comparison) and throwing a typed exception, applied uniformly across all three patch formats after normalization, preventing entity primary key corruption regardless of the originating format.

**Claim 7.** A JSON Pointer resolution method for patch execution that performs case-insensitive property matching at each path segment by iterating existing properties of the target JSON object, accommodating the naming convention mismatch between JSON camelCase and C# PascalCase without requiring explicit mapping configuration or naming policy registration, combined with automatic creation of intermediate objects when a pointer traverses a path where parent objects do not yet exist.

**Claim 8.** A patch normalization system wherein: (a) all three format-specific normalizers produce `PatchOp` records with values detached from the input JSON tree via `DeepClone()`, preventing mutation of the original request body; (b) the resulting `PatchPayload` is an immutable sealed record; and (c) downstream processing (authorization, validation, persistence, audit) operates on the normalized representation without awareness of the originating format, with a `KindHint` string preserved solely for diagnostic and audit purposes.

**Claim 9.** A dual-execution architecture for HTTP PATCH processing comprising: (a) a normalized path where all formats are first converted to a `PatchPayload` of `PatchOp` records and then applied by a generic executor using JSON Pointer resolution; and (b) a legacy path where format-specific applicators (`JsonPatchApplicator`, `MergePatchApplicator`, `PartialJsonApplicator`) apply format-native logic directly to the target entity, where both paths converge on the same persistence layer and the normalized path is the preferred default.

**Claim 10.** A method for declaring multiple HTTP PATCH action methods on the same URL route within a web API controller, differentiated by `[Consumes]` media type attributes, where the merge-patch and partial-JSON overloads are annotated with `[ApiExplorerSettings(IgnoreApi = true)]` to suppress their appearance in auto-generated OpenAPI documentation while remaining fully functional at runtime, presenting a simplified API surface to documentation consumers while supporting all three formats for clients that send the appropriate Content-Type header.

---

## 6. Implementation Evidence

The described invention is fully implemented and operational in Koan Framework v0.6.3. The following source files constitute the reference implementation:

### 6.1 Normalizer and Canonical Models (Koan.Web and Koan.Data.Abstractions assemblies)

| File | Purpose |
|---|---|
| `src/Koan.Web/PatchOps/PatchNormalizer.cs` | Static normalizer with three entry points and recursive walker |
| `src/Koan.Data.Abstractions/Instructions/PatchModels.cs` | `PatchPayload<TKey>`, `PatchOp`, `PatchOptions`, `MergePatchNullPolicy`, `PartialJsonNullPolicy`, `PatchKind`, `IPatchApplicator<T>` |
| `src/Koan.Web/Infrastructure/KoanWebConstants.cs` | Content-Type constants (`ApplicationJsonPatch`, `ApplicationMergePatch`, `ApplicationJson`) and query parameter constants (`Nulls`, `MergeNulls`, `PartialNulls`) |

### 6.2 Controller Integration (Koan.Web assembly)

| File | Purpose |
|---|---|
| `src/Koan.Web/Controllers/EntityController.cs` | Generic CRUD controller with three `[HttpPatch]` overloads (lines 472-487), `PatchNormalized` convergence method, `BuildPatchOptions`, `TryBuildPatchOptionsOverride` |

### 6.3 Execution Layer (Koan.Data.Core assembly)

| File | Purpose |
|---|---|
| `src/Koan.Data.Core/Patch/PatchOpsExecutor.cs` | Normalized operation executor with JSON Pointer resolution, identity mutation blocking, case-insensitive property matching |
| `src/Koan.Data.Core/Patch/PatchApplicators.cs` | Legacy format-specific applicators (`MergePatchApplicator<T>`, `PartialJsonApplicator<T>`) and factory |

### 6.4 Framework Version and Build Target

- Framework version: v0.6.3
- Build target: net10.0
- All source files are compiled and tested as part of the standard CI pipeline.

---

## 7. Publication Notice

This document is a **defensive publication**. It is published to establish prior art and to prevent any party -- including the inventor, the inventor's employer, or any third party -- from obtaining patent protection on the techniques described herein.

**Intent:** The sole purpose of this publication is to ensure that the described techniques remain freely available for use by the public. This publication does not grant any patent rights, nor does it restrict anyone from implementing the described techniques.

**Scope:** This publication covers the specific combination of: (a) Content-Type-based routing of three HTTP PATCH formats to a single endpoint; (b) a static, stateless normalizer converting all three formats to a unified `PatchPayload<TKey>` intermediate representation; (c) differentiated null semantics between merge-patch and partial-JSON via a recursive walker with a boolean flag; (d) configurable null policies with per-format enumerations and per-request query string overrides; (e) identity field mutation blocking at the execution layer; (f) case-insensitive JSON Pointer resolution; and (g) a dual-execution architecture supporting both normalized and format-native application paths. Individual components may have prior art in other domains; the novelty lies in their combination for unified HTTP PATCH processing.

**Date of first implementation:** 2025 (initial code merge into Koan Framework).

**Date of this publication:** 2026-03-24.

**Inventor acknowledgment:** I, Leo Botinelly (Leonardo Milson Botinelly Soares), confirm that the techniques described in this document are my original work, implemented within the Koan Framework, and are published here defensively to prevent patenting.

---

## Appendix A: Antagonist Cycle Review

The following adversarial review was conducted to stress-test the claims and identify weaknesses.

### A.1 Challenge: "ASP.NET Core already supports JSON Patch via JsonPatchDocument -- what is new here?"

**Response:** ASP.NET Core's `JsonPatchDocument<T>` supports RFC 6902 exclusively. It provides no merge-patch support, no partial-JSON support, no format detection, and no normalization to a common intermediate representation. The `[Consumes]` attribute exists in ASP.NET Core, but using it to route three competing patch formats to format-specific normalizers that converge on a unified `PatchPayload` is the contribution described here. The framework provides the routing mechanism; the three-way normalization with differentiated null semantics is the invention.

### A.2 Challenge: "RFC 7396 already defines merge-patch semantics -- this just implements the spec"

**Response:** RFC 7396 defines the semantics of a single format. It does not define how to normalize merge-patch operations into the same representation as RFC 6902 operations, nor how to coexist with a third format (partial JSON) that uses the same body structure but different null semantics. The normalization step -- converting `{"description": null}` to `PatchOp("remove", "/description", ...)` under merge semantics but `PatchOp("replace", "/description", null)` under partial semantics -- is the distinguishing transformation. The RFC describes one format; this invention normalizes three.

### A.3 Challenge: "The recursive walker is just a JSON tree traversal -- obvious to any developer"

**Response:** JSON tree traversal is a well-known technique. The specific contribution is using a single walker with a `mergeSemantics` boolean flag to produce semantically different `PatchOp` outputs from identical input structures. The walker constructs JSON Pointer paths by concatenating property names during recursion, emits `remove` vs. `replace` for null values based on the flag, and applies `DeepClone()` to detach values from the input tree. While each individual step is straightforward, the composition into a shared normalization primitive for two formats with divergent null semantics has not been demonstrated in surveyed prior art.

### A.4 Challenge: "Per-request null policy overrides via query strings are just configuration -- not novel"

**Response:** Query string parameters for API behavior modification are common. The specific contribution is the layered precedence model applied to null semantics across two independent policy dimensions (merge-patch nulls and partial-JSON nulls): baseline options -> global `?nulls=` override -> specific `?mergeNulls=` / `?partialNulls=` override. The system produces a new immutable `PatchOptions` record at each override layer rather than mutating, and the override applies after normalization but before execution via the `with` expression on `PatchPayload`. This combination of per-format null policies with per-request override at the patch-specific level is not present in surveyed frameworks.

### A.5 Challenge: "Identity field blocking is basic input validation"

**Response:** Blocking `/id` mutation is indeed a validation concern. The contribution is performing this check uniformly at the execution layer after normalization, rather than at each format-specific entry point. Because all three formats converge on `PatchOp` records with JSON Pointer paths, the identity check is written once in `PatchOpsExecutor` and applies to all formats. In frameworks without normalization, this check must be duplicated for each supported format or deferred to the persistence layer where it may be inconsistently enforced.

### A.6 Challenge: "Case-insensitive JSON Pointer resolution is not standard -- it violates RFC 6901"

**Response:** RFC 6901 defines JSON Pointers as case-sensitive. The case-insensitive resolution in `PatchOpsExecutor` is a pragmatic adaptation for the .NET ecosystem where JSON serialization conventions (camelCase wire format) frequently differ from C# property naming (PascalCase). The `FindExistingPropertyName` method searches existing properties case-insensitively, falling back to the original segment if no match is found. This is applied at the execution layer after normalization, not at the normalization layer itself -- the normalized `PatchOp.Path` preserves the original pointer verbatim. The adaptation is documented as a design choice for .NET interoperability, not as a general-purpose modification of RFC 6901 semantics.

### A.7 Challenge: "The dual execution path (normalized vs. legacy) suggests the normalized path is incomplete"

**Response:** The dual path exists for backward compatibility during migration, not because either path is incomplete. The legacy `PatchApplicators` path supports direct format-native application (e.g., `JsonPatchDocument.ApplyTo()` for RFC 6902, which handles `move`, `copy`, and `test` operations natively). The normalized `PatchOpsExecutor` path currently throws `NotSupportedException` for `move`, `copy`, and `test` because these operations are rarely used in entity PATCH workflows and their execution semantics are format-dependent. The architecture is designed for the normalized path to subsume the legacy path as operation support is extended. Both paths share the same persistence layer, ensuring data consistency regardless of which path is used.

### A.8 Challenge: "Could someone argue this is just the Adapter pattern applied to patch formats?"

**Response:** The Adapter pattern is a structural design pattern for interface compatibility. The invention uses structural adaptation as one component, but the contribution extends beyond it: the recursive null-semantics-aware walker, the configurable per-request policy overrides, the identity mutation blocking after normalization, and the `[Consumes]`-based multi-format routing on a single endpoint are not inherent to the Adapter pattern. The pattern provides the vocabulary; the specific application to the three-way HTTP PATCH normalization problem with configurable null semantics is the inventive step. This publication ensures the combination remains in the public domain.

### A.9 Challenge: "The OpenAPI suppression via [ApiExplorerSettings(IgnoreApi = true)] is a known ASP.NET feature"

**Response:** The `[ApiExplorerSettings]` attribute is indeed a standard ASP.NET Core feature. Its use here is not claimed as novel in isolation. The contribution is the pattern of combining it with `[Consumes]`-differentiated overloads to create a single PATCH endpoint that accepts three formats at runtime while presenting only the primary format (RFC 6902) in auto-generated documentation. This is a UX decision that balances API discoverability with format flexibility, and is documented as part of the overall system design rather than as an independent claim.
