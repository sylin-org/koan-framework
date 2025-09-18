---
id: DATA-0045
slug: DATA-0045-default-projection-policy-and-json-pushdown
domain: DATA
status: Accepted
date: 2025-08-19
---

# 0045: Default projection policy and JSON pushdown


## Context
Adapters should avoid in-memory filtering when server-side pushdown is reasonably possible. For document-like storage on relational engines (e.g., SQLite), we keep full-fidelity JSON while enabling efficient querying and paging.

## Decision
- Projection by default:
  - Project all root-level scalar properties (string, bool, numeric, decimal, DateTime/Offset, Guid) and enums (stored as strings by default).
  - Use standard .NET annotations to refine behavior:
    - [NotMapped] to opt out a property from projection
    - [Column("Name")] to override projected column name
    - [Index] to request an index (existing Koan attribute)
- Provider handling:
  - SQLite: implement projected columns (generated) sourced from Json and create indexes for [Index]. For non-projected properties, rewrite predicates to JSON1 (json_extract) instead of falling back to in-memory. Full SELECT remains uncapped; LIMIT/OFFSET applied elsewhere.
  - Providers without benefit (e.g., Mongo) ignore projection metadata.
- Query translation:
  - Prefer projected column pushdown; otherwise rewrite to provider-native JSON path expression. Only fall back to in-memory for truly unsupported expressions.
- Schema evolution:
  - Idempotent DDL (CREATE TABLE IF NOT EXISTS, ALTER TABLE ADD COLUMN IF NOT EXISTS, CREATE INDEX IF NOT EXISTS).

## Consequences
- Better default performance and fewer surprises: paging and filtering happen server-side.
- Minimal model noise: standard attributes only; no provider-specific entity-level flags required.
- Enums are projected as strings by default; policy hooks can switch to ints later if needed.

## Notes
- A central ProjectionResolver in Koan.Data.Core will compute the projection plan per entity; adapters consume it for DDL and query rewriting.
- Diagnostics may be added later to warn on excessive projections; no hard cap enforced initially.
