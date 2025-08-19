# 0031: JSON Filter $options.ignoreCase

Status: Accepted

Date: 2025-08-17

## Context

We introduced a pragmatic JSON filter DSL (ADR-0029) used by `EntityController` for GET `?filter=` and POST `/query` operations. Early consumers requested case-insensitive search for string fields, especially for contains/startsWith/EndsWith patterns. Mongo LINQ and other providers have limited support for overloads that take `StringComparison`, so we need a provider-friendly approach.

## Decision

Add an `$options` object that can appear at the root or nested within any filter object. The first supported option is:

- `ignoreCase: true|false` (default false)

Semantics when `ignoreCase` is true:
- For string equals, preserve null semantics: if the filter value is `null`, match only `field == null`. Otherwise require `field != null` and compare `field.ToLower() == valueLower`.
- For contains/startsWith/endsWith, lower both operands and use single-argument string methods so LINQ providers (e.g., Mongo) can translate.
- For `$in` with strings, lower the candidate array and the field.

Scope resolution:
- `$options` merges from outer to inner scopes. Inner `$options` override outer ones.
- POST `/query` also accepts a top-level `$options` sibling to `filter` that is applied to the whole filter.

Provider compatibility:
- We avoid `StringComparison` overloads, using only 1-arg string methods for contains/startsWith/endsWith. This keeps Mongo LINQ translations working.

## Consequences

- API surface stays minimal; the filter language remains simple and URI-friendly.
- Clients can toggle case-insensitivity without server configuration.
- Implementation applies a best-effort push-down to LINQ-capable adapters; others safely evaluate in memory.

## References

- ADR-0029: JSON Filter Language and Query Endpoint
- ADR-0030: Entity Sets Routing and Storage Suffixing
