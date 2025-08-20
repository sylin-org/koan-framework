---
id: DATA-0004
slug: DATA-0004-index-anchoring-and-json-mapping
domain: DATA
status: Accepted
date: 2025-08-16
title: Index anchoring and automatic JSON mapping policy
---
 
# 0004: Index anchoring and automatic JSON mapping policy
 

## Context

- We want provider-agnostic annotations for storage naming and indexing.
- Using class-level Index with raw field names can confuse authors about whether to reference property names or storage names.
- For relational adapters, requiring [JsonEncoded] everywhere is noisy; most non-simple types should map to JSON by default.

## Decision

1) Index anchoring

- Introduce IndexAttribute that supports both class-level and property-level usage.
- Recommended usage: annotate the participating properties directly.
- Composite indexes: give each participating property Index with the same Name (or Group) and an Order value to control column order; Unique can be set on any (adapters should unify).
- When class-level Index is used, Fields are property names; adapters must resolve to storage names using StorageName/naming strategy.

2) Automatic JSON mapping

- Add a shared TypeClassification helper.
- Relational adapters default to storing complex types (non-simple and collections) as JSON TEXT/BLOB columns.
- A future [JsonEncoded] attribute remains optional to force/override if needed; but default policy is automatic.

## Consequences

- Authors don’t need to know storage column names for Index; they can anchor to properties.
- Less attribute noise for common scenarios; relational adapters transparently JSON-map complex properties.
- Providers may still offer overlays for advanced DDL/index features.

## Notes

- Core annotations added: Storage, StorageName, IgnoreStorage, RenamedFrom, Index.
- Adapters should honor DataAnnotations like [Required], [DefaultValue], [MaxLength] when feasible.

## Enforcement of Identifier (PK/unique) and dedupe

- The Identifier-marked property is the entity key and MUST have a fast, unique lookup.
- Core defines the intent (via Identifier and shared metadata); physical enforcement is adapter-specific:
	- Relational: use PRIMARY KEY (preferred) or UNIQUE when PK can’t be used; do not create an extra secondary index on the same column.
	- Document: map to native id with its built-in unique index (_id, etc.).
	- Vector: use the native id/pk semantics.
	- JSON/file: maintain an in-memory id map; no user-visible index.
- Deduplication: if users declare [Index] on the Identifier, adapters should no-op it (PK/unique already covers it).
