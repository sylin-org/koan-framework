---
id: STOR-0003
slug: STOR-0003-storage-profiles-and-routing-rules
domain: STOR
title: Storage profiles and routing rules (selection and determinism)
status: Accepted
date: 2025-08-24
---

Context

- Multiple providers may be active simultaneously; we need deterministic rules to choose where a file lives.
- Profiles name logical targets (Provider + container/bucket + settings) and allow policy per class of data (e.g., secure, cold).

Decision

- Introduce StorageProfile and a simple rule engine owned by IStorageRouter.
- Profiles
  - Name (unique), ProviderId (DI key), Container/Bucket/Path, Encryption/Retention (optional), AuditEnabled.
  - Optional staging strategy for pipeline (None | LocalTemp | ProviderShadow).
- Rules
  - Ordered predicate list, first-match-wins; validated at startup.
  - Predicates over: tags (has/any/all), content-type (pattern), size (ranges), classification hints, caller context.
  - Default rule required; fallback is explicit.
- Diagnostics
  - Router exposes Explain(ctx) returning the matched rule id and reasons; useful in logs and tests.

Scope

- In scope: profile and rule shape, evaluation order, validation; not implementing a general-purpose DSL.

Consequences

- Positive: simple and predictable routing; easy to extend with new predicates.
- Negative: limited expressiveness by design; complex scenarios may require custom router implementation via DI.

Implementation notes

- Keep rule evaluation allocation-free and fast; precompile regex/patterns.
- Provide options binding: Koan:Storage:Profiles[] and Koan:Storage:Rules[].
- Expose minimal metrics: rule hit counts per profile.

References

- STOR-0001 Storage module and contracts
