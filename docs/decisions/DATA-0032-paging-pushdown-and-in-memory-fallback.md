---
id: DATA-0032
slug: DATA-0032-paging-pushdown-and-in-memory-fallback
domain: DATA
status: Accepted
date: 2025-08-17
---

# ADR 0032: Paging pushdown and in-memory fallback

## Context

`EntityController` supports filtering via JSON or string queries and returns pagination headers.
Originally, pagination was applied after materializing the filtered results in memory, which is simple but inefficient for large datasets.

We also added repository-level `CountAsync` to avoid loading all items just to compute totals.

## Decision

- Prefer native paging pushdown to adapters when possible.
  - LINQ-capable adapters should apply `Skip/Take` and sorting before execution.
  - String-query adapters should translate to `LIMIT/OFFSET` (or equivalent) with `ORDER BY`.
- Keep in-memory pagination as a correctness fallback for adapters that lack native paging (e.g., pure JSON store) or when capabilities are insufficient.
- When in-memory pagination is used, the controller must surface a response header: `Koan-InMemory-Paging: true`.
- Totals should be computed via repository `CountAsync(..)` whenever supported; if not, fall back to counting the materialized result.

## Consequences

- Adapters should extend their query paths to support native paging over time.
- Clients and operators can detect non-native paging via the `Koan-InMemory-Paging` header and treat it as a performance warning.
- Behavior remains correct across providers; performance improves as adapters adopt pushdown.

## Alternatives considered

- Require all adapters to implement native paging before release: rejected. Increases barrier to entry for simple providers; fallback keeps the system usable.
- Always in-memory paging: rejected. Poor performance at scale and hides provider capabilities.
