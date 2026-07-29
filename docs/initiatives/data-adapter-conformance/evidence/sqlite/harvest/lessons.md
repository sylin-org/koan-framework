---
type: REFERENCE
domain: data
title: "SQLite provider lessons"
status: accepted
last_updated: 2026-07-28
---

# SQLite provider lessons

- Managed `Id + Json` and explicit relational shapes are mapping choices, not reasons for separate repositories.
- Private `:memory:` connections need a host-owned shared-memory identity and keeper; source identity must be part of
  that name or routed sources leak into one another.
- Opening a normal file connection can create storage. External validation, health, inspection, and named reads must
  use non-creating/read-only opens.
- `PRAGMA query_only=ON` requires a deferred read transaction; the default transaction tries to acquire write state.
- Nested legacy values can be updated with `json_set` while preserving unmapped siblings. Whole-object bindings are
  intentionally replacing writes.
- Provider-generated keys require `RETURNING`; bounded multi-statement commands preserve input ordering for bulk.
- Adapter-local query compilers, JSON codecs, neutral readers, and discovery flows duplicate shared Relational law
  and create drift. SQLite needs only its dialect and physical facts.
- Configuration and health are observations. Neither should synchronously discover, probe, or materialize a file.
