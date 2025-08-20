---
id: ARCH-0001
slug: ARCH-0001-rename-generic-to-tentity
domain: ARCH
status: Accepted
date: 2025-08-16
---

# ADR 0001: Rename public generic parameter from TAggregate to TEntity

Context
- Public APIs used `TAggregate` historically, but the domain-facing base is `Entity<T>`, and the codebase moved to a domain-centric vocabulary.

Decision
- Rename all public generic parameters `TAggregate` → `TEntity` across Sora abstractions and implementations.
// Updated later by ADR 0009 to unify on `IEntity<TKey>`; aggregate root remains a doc concept.

Consequences
- API clarity: aligns generics with `Entity<T>` and common DDD naming.
- Source-compatible for most consumers using type inference; binary compatibility not impacted since parameter names are non-breaking.
- Docs updated accordingly.

References
- PR: Rename sweep across Sora; samples and tests updated.
