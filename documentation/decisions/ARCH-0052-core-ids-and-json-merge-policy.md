---
id: ARCH-0052
slug: core-ids-and-json-merge-policy
domain: Architecture
status: accepted
date: 2025-08-28
title: Core IDs (ShortId + ULID) and JSON Merge policy (default Union)
---

## Context

Koan standardizes on Newtonsoft.Json for JSON handling and is introducing a small, cohesive set of DX helpers in Koan.Core and Koan.Storage. Two cross-cutting choices need to be codified:

- ID generation: provide compact, URL-safe identifiers for public URLs and optional time-sortable IDs.
- JSON merge semantics: a consistent, deterministic behavior for merging layered JSON payloads across features (configuration layering, request shaping, data transforms).

## Decision

1. IDs

- Provide ShortId (22-char base64url-encoded Guid) as the default compact ID.
- Provide ULID generation alongside ShortId for time-sortable IDs.
- Keep both deterministic and independent; call sites choose.

2. JSON Merge policy

- MergeJson accepts an ordered set of JSON layers. Earlier layers are stronger (first wins) over later layers.
- Default array policy = Union by index:
  - For arrays at the same path: earlier layers override element positions; missing tail comes from weaker layers.
  - Type conflicts at the same path resolve in favor of the earlier (stronger) layer for that node.
- Expose a control flag to change array strategy per call site:
  - Union (default)
  - Replace (strongest entire array replaces weaker arrays)
  - Concat (append weaker arrays after stronger arrays)
  - Union-by-key (advanced): when configured with a key and both arrays are objects holding that key, merge by key — preserve order from the stronger array, keep stronger values on conflicts, and append unseen keys from weaker arrays.

## Scope

- Applies to Koan.Core JSON utilities and ID helpers.
- Non-breaking for existing modules; new surface area only.
- JSON utilities are Newtonsoft.Json-based and respect Koan JsonDefaults.

## Consequences

- Developers get predictable, documented JSON merge behavior across the stack.
- ShortId is optimal for compact URLs; ULID is available when lexicographic time ordering is desired.
- Call sites can opt into alternate array strategies when needed without forking utility code.

## Implementation notes

- Packages/locations
  - IDs: `src/Koan.Core/Utilities/Ids/ShortId.cs` and `UlidId.cs` (or a single `Ids` static with separate methods).
  - JSON: `src/Koan.Core/Json/JsonDefaults.cs`, `JsonExtensions.cs` (ToJson/FromJson using JsonDefaults), and `JsonMerge.cs` (MergeJson with options).
- JsonDefaults
  - CamelCase, NullValueHandling.Ignore, DefaultValueHandling.Ignore, ISO 8601 dates, InvariantCulture, deterministic ordering where applicable.
- MergeJson
  - Layers: params string[] or IEnumerable<string>.
  - Earlier index = stronger precedence.
  - Arrays: default Union-by-index; control via enum option (Union|Replace|Concat) and optional ArrayObjectKey for Union-by-key when arrays are objects with that key.
  - Mixed-type conflicts: earlier layer wins at that node.
- IDs
  - ShortId: base64url Guid without padding (“-”, “\_”), 22 chars; round-trip Guid conversion.
  - ULID: use a standard ULID implementation (lexicographically sortable); expose `Guid.CreateVersion7().ToString()` returning canonical 26-char string.

## Follow-ups

- Consider adding KSUID/UUIDv7 if time-sortable IDs gain broader usage.
- Consider advanced array merge modes (e.g., object-key matching) if a stable contract emerges.
- Add concise docs in engineering front door linking this ADR.

## References

- Newtonsoft policy (repo-wide): internal decision and docs
- .NET FileExtensionContentTypeProvider for MIME types (separate STOR ADRs)
