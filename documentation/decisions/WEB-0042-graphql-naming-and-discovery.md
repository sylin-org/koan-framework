---
id: WEB-0042
slug: WEB-0042-graphql-naming-and-discovery
domain: WEB
status: Accepted
date: 2025-08-19
title: GraphQL naming & discovery policy - storage-based names, IEntity-only, collision handling
---

# ADR 0042: GraphQL naming & discovery policy - storage-based names, IEntity-only, collision handling

## Context

During the implementation of `Koan.Web.GraphQl` (ADR-0041), we encountered schema initialization issues in Hot Chocolate caused by duplicate/ambiguous CLR type names and inconsistent naming across payload/input types. The runtime errors included:

- "The name becomes immutable once it was assigned" when type names clashed across registrations
- "The type definition is null" from broken cross-references when reusing names

Root causes:

- Discovering all exported classes led to non-`IEntity<>` types being considered inadvertently
- Name derivation from CLR simple names (e.g., `Item`) collided across projects/namespaces
- Reusing the same name for different GraphQL constructs (entity, input, payload)

We needed a deterministic policy for discovery and naming that aligns with Koan’s storage conventions and avoids Hot Chocolate naming conflicts.

## Decision

Adopt a strict discovery + naming policy for GraphQL auto-generation:

1. Discovery scope: IEntity-only

- Only types implementing `IEntity<TKey>` are considered for schema generation.
- This aligns GraphQL with REST `EntityController<T>` behavior and avoids leaking internal types.

2. Storage-based naming

- For each `TEntity`, compute its storage name using `StorageNameRegistry` / `IStorageNameResolver` (see ADR-0017 and ADR-0018).
- Derive GraphQL identifiers from the storage name, not the CLR name.
- Apply consistent shaping:
  - Type: PascalCase (e.g., `Todo`)
  - Field (singular): camelCase (e.g., `todo`)
  - Field (collection): pluralized camelCase (e.g., `todos`)
  - Input: `<TypeName>Input` (e.g., `TodoInput`)
  - Collection payload: `<TypeName>CollectionPayload`
- Sanitize names to valid GraphQL identifiers (prefix with `_` if starting with a digit; strip/replace invalid chars).

3. Collision handling

- If multiple entities resolve to the same GraphQL type name (after storage-based derivation), prefer the first discovered and skip subsequent duplicates, logging a warning. This ensures schema stability and prevents runtime crashes.
- Avoid reusing the same name for different constructs. Each of: entity type, input type, and collection payload receive unique names.

4. Deterministic references

- When wiring fields, reference types using `NamedTypeNode` with the computed names rather than relying on inferred types to prevent ambiguous resolution.

5. Centralized constants and controller hosting

- Keep route constants and defaults centralized; expose GraphQL only via the controller (no inline MapGraphQL endpoints), per Koan guardrails.

## Implementation notes

- `AddKoanGraphQlExtensions` now:
  - Discovers `IEntity<>` types via reflection and filters strictly
  - Computes storage-derived names per entity
  - Registers per-entity object type, input type, and collection payload with unique names
  - Adds query fields (`item`, `items`) and mutation (`upsert`) using `NamedTypeNode` references
  - Integrates hooks (`HookRunner`) and maps filter/paging options to `QueryOptions`
- Duplicate registrations by name are avoided; entity discovery is de-duplicated.

## Consequences

- Positive
  - Naming aligns with storage and REST routes; schema generation is stable and deterministic.
  - Eliminates Hot Chocolate naming conflicts; reduces container-only failures due to stale registrations.
- Negative
  - If two distinct entities share a storage name, one will be skipped (with a warning). Developers must resolve the conflict via storage naming overrides.

## Alternatives considered

- CLR-based names with namespace disambiguation: still collided after trimming and were unfriendly.
- Random suffixes on collisions: avoided for determinism and DX.

## Follow-ups

- Sorting support and typed sort inputs (deferred from ADR-0041) should follow the same naming policy.
- Add a minimal troubleshooting section to docs for cleaning and rebuilding container images when schema changes (compose `down -v`, `build --no-cache`, `up -d`).

## References

- ADR-0017: Storage naming conventions
- ADR-0018: Centralized naming registry and DX
- ADR-0041: GraphQL module and controller
