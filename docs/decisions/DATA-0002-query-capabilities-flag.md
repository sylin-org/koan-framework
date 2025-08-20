---
id: DATA-0002
slug: DATA-0002-query-capabilities-flag
domain: DATA
status: Accepted
date: 2025-08-16
---

# ADR 0002: Introduce QueryCapabilities flags and IQueryCapabilities

Context
- Providers have different query features (e.g., raw string queries vs LINQ predicates). We need a discoverable way to expose capabilities.

Decision
- Add `QueryCapabilities` [Flags] enum with `None`, `String`, `Linq`.
- Add `IQueryCapabilities` with a `Capabilities` property.
- Implement the interface in repositories that support discovery; `RepositoryFacade` forwards capabilities from the inner repository.
- JSON adapter declares `Linq` support; string queries remain for future SQL adapters.

Consequences
- Callers can check `IQueryCapabilities` to decide when to use `Query(...)` vs LINQ overloads.
- Adapters opt-in; no breaking change for those that ignore it.

References
- PR: Capability flags and facade surfacing.
