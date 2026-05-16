# ARCH-0076: Repository Decorator Order Canon

**Status**: Accepted
**Date**: 2026-05-15
**Deciders**: Enterprise Architect
**Scope**: `IDataRepositoryDecorator` implementations across all pillars
**Related**: ARCH-0075 (Koan.Cache pillar — first `[ProviderPriority]`-locked decorator)

---

## Context

`Koan.Data.Core` exposes `IDataRepositoryDecorator` as an extension point. Pillars can register decorators that wrap `IDataRepository<T, K>` to layer cross-cutting concerns onto every entity operation. Two decorators ship today:

1. **`CacheRepositoryDecorator`** (`Koan.Cache`, M6) — applies `[Cacheable]` / `[CachePolicy]` policies. Read short-circuits, write-through to cache after DB commit, broadcast eviction via coherence.
2. **`CqrsRepositoryDecorator`** (`Koan.Data.Cqrs`) — appends outbox events on writes, optionally fan-outs reads to read replicas.

More decorators are anticipated as the framework grows: audit-log decorators, soft-delete decorators, multi-tenancy filters, validation decorators.

**The problem:** without an explicit ordering canon, decorator registration order silently determines runtime behavior. Two decorators can interact in surprising ways depending on which wraps which. For example:

- Cache **outer**, CQRS **inner**: cache hits short-circuit before CQRS observes the read. The outbox sees only DB-fetched reads.
- CQRS **outer**, Cache **inner**: CQRS observes every read (including cache hits). The outbox grows unnecessarily.

Both might be desired in different contexts, but the framework must pick one as canon and make the choice declarative, not order-dependent.

### Forces

1. **Declarative > positional.** Registration order is fragile and invisible at the call site. `[ProviderPriority]` (already used by `IDataAdapterFactory` selection in `Koan.Data.Core`) gives decorators a stable, attribute-driven ordering surface.
2. **Read short-circuit is the dominant concern.** A cache hit that returns before any downstream work is the highest-value cross-cutting behavior — sub-millisecond reads are the whole point of the cache pillar. Cross-cutting decorators that observe reads (audit, CQRS) only matter when reads actually happen.
3. **Write transformations are different.** Soft-delete and multi-tenancy filters need to MUTATE the query/write before it reaches the inner repository. These belong inside everything else.
4. **Framework-reserved bands** keep room for future pillar work without colliding with user decorators.

---

## Decision

`IDataRepositoryDecorator` implementations declare `[ProviderPriority(N)]` from `Koan.Data.Abstractions`. The data service composes decorators by descending priority — higher priority wraps lower. Within a priority band, registration order breaks ties.

### Priority bands

| Band | Concern | Examples | Rationale |
|---|---|---|---|
| **100+** | **Read short-circuit** | `CacheRepositoryDecorator` (100) | Hits return before downstream observes the read. Highest value placement. |
| **50–99** | **Read observation** | `CqrsRepositoryDecorator` (50), audit-log, telemetry | Sees actual DB reads; not noise-polluted by cache hits. |
| **0–49** | **Write transformation** | Soft-delete filter, multi-tenancy filter, validation | Mutates the query/write before it reaches the inner repository. |
| **< 0** | **Framework reserved** | (future internal use) | Don't use without coordinating with the framework team. |

### Concrete assignments

| Decorator | Priority | Band | Rationale |
|---|---|---|---|
| `Koan.Cache.Decorators.CacheRepositoryDecorator` | **100** | Read short-circuit | M6 (ARCH-0075) |
| `Koan.Data.Cqrs.CqrsRepositoryDecorator` | **50** | Read observation | Records DB operations to outbox; doesn't need to see cache hits |

### Convention for new decorators

1. Pick the band that matches the concern.
2. Within the band, pick a priority that leaves headroom (use multiples of 10) so future decorators in the same band have insertion points.
3. Document the choice in the decorator's XML doc with a sentence explaining why it sits in that band.
4. Test the placement with a `[ProviderPriority]` reflection assertion (mirrors `DecoratorPrioritySpec` from M6).

---

## Consequences

### Positive

- **Decorator behavior is now declarative and discoverable.** A grep for `[ProviderPriority]` on `IDataRepositoryDecorator` implementations enumerates the order canon.
- **Future decorators have a clear placement question.** No more "it depends on registration order" surprises.
- **Test coverage is straightforward** — reflection-asserts the priority value (already done for `CacheRepositoryDecorator`).
- **Aligns with existing framework canon** — `[ProviderPriority]` already powers `IDataAdapterFactory` resolution in `Koan.Data.Core`.

### Negative

- **One more attribute to remember.** Mitigated by the convention that omitting `[ProviderPriority]` puts the decorator at priority 0 (write-transformation band), which is the right default for most non-cache concerns.
- **Priority is a magic number.** Constants on a static helper class (e.g., `DecoratorPriorities.CacheShortCircuit = 100`) could improve readability — defer until a third decorator needs the same band.

### Risks

- **Two decorators in the same band with the same priority** — registration order decides. Document this in the band tables so contributors know to pick distinct values within a band.
- **A future "ultra-priority" need (>1000)** — currently no use case. The framework-reserved band (<0) covers the symmetric case. Re-evaluate if a new pattern emerges.

### Open

- A `[DecoratorPriority]` alias attribute (`[ProviderPriority]` reads naturally for adapters/factories, less so for decorators). Defer until a contributor raises it; the current naming is consistent with existing canon.

---

## References

- `src/Koan.Data.Abstractions/ProviderPriorityAttribute.cs` — the attribute used here
- `src/Koan.Cache/Decorators/CacheRepositoryDecorator.cs` — first decorator to adopt this canon (M6, priority 100)
- `src/Koan.Data.Cqrs/CqrsRepositoryDecorator.cs` — second adopter (priority 50 — needs the attribute applied per this ADR)
- `tests/Suites/Cache/Topology/Koan.Tests.Cache.Topology/Specs/DecoratorPrioritySpec.cs` — reflection assertion pattern
- ARCH-0075 (Koan.Cache pillar) — the milestone that drove this canonization
