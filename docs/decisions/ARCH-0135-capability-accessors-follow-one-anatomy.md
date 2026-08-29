---
id: ARCH-0135
slug: capability-accessors-follow-one-anatomy
domain: Architecture
status: Accepted
date: 2026-08-28
title: Capability accessors follow one anatomy
related:
  - JOBS-0005
  - ARCH-0113
  - AI-0021
---

# ARCH-0135: Capability accessors follow one anatomy

## Context

Entity capability accessors accreted one capability at a time. Jobs resolved its `.Job`/`.Jobs`
mechanics in JOBS-0005 §12.14/§16 — C# 14 extension members, no source generator — and then each
later capability re-derived the same idea from that source file: Data.AI's `.Ai` gateway, Cache's
`.Cache` facets, Communication's `.Events`/`.Transport`/`.EventGateway`. Six delivery sites across
five packages now agree on the mechanics because the authors read each other's code, not because a
decision says so. The next capability will re-derive it again, and the places derivations drift are
exactly the places users can see: the accessor's name, the singular/plural split, which verbs live
inside the facade, and how names get qualified.

The naming question that surfaced this decision was concrete. Semantic search on an entity shipped
as `Note.AI.Search(...)` (scoped facade), then nearly re-shipped as `Todo.Ai.SemanticSearch(...)`
(mirroring the flat `EntityEmbeddingExtensions.SemanticSearch<T>` static), before settling. The
rule that resolves it was already in the tree: the vector facade is `Vector<T>.Search`, never
`Vector<T>.VectorSearch` — qualification belongs where context is absent.

ARCH-0113 owns two adjacent concerns and this decision defers to both: the Events/Transport/Lifecycle
intent grammar, and pointwise capability lifting as a framework law. What remains unowned is the
accessor anatomy itself — delivery, shape, naming, and the instance-vs-static split.

## Decision

An entity capability accessor follows one anatomy:

1. **Delivery — C# 14 extension members on the Entity form.** No source generator, no `partial`
   requirement, no members authored into the entity class (resolving JOBS-0005 §12.14). The
   accessor exists exactly when the owning capability package is referenced and never otherwise
   (Reference = Intent). Documentation may state the gate as "present whenever `<package>` is
   referenced".
2. **Shape — a `readonly struct` of verbs routing ambiently.** `JobStatics<T>`, `AiStatics<T>`,
   `EntityCacheFacet<TEntity,TKey>`: stateless structs whose members resolve the capability
   ambiently (AppHost / static facades) at call time. No DI fields, no per-instance state, and no
   state cached from `KoanRegistry` at register time. Collection lifts ride beside the accessor
   only under ARCH-0113's pointwise-lifting law.
3. **Naming — one accessor per capability; qualify in the flat namespace, elide in the facade.**
   Inside the facade the verb is short: `Todo.Ai.Search(...)`, `Vector<T>.Search(...)`. The
   qualifier lives on the flat static the facade routes to
   (`EntityEmbeddingExtensions.SemanticSearch<T>`), where "Search" alone would be orphaned. When
   both spellings could read, the facade wins the short name.
4. **Instance vs static — split by subject, not by ceremony.** A static and an instance member
   cannot share one name (CS0102 forced `Job`/`Jobs`); before inventing a plural, ask whether the
   instance form should be an accessor at all. Operations whose subject is the instance read as
   the entity's own verb — `note.Similar(...)`, SoftDelete's bare `HardDelete()`/`Restore()` —
   delivered as ordinary instance extensions, no facade. Operations over the kind, or the
   capability's control plane, take the static facade — `Note.Ai.Search(...)`,
   `MyModel.Jobs.Trigger(...)`.
5. **Builders — small, fluent, entity-level.** Configure lambdas take `Action<TBuilder>` in the
   `VectorQuery` style. The builder carries only entity-level knobs (`SemanticSearchQuery`:
   `Top`/`Threshold`/`Partition`); provider, transport, and hybrid-query knobs stay on the
   long-form surface the accessor routes to. The builder's smallness is the contract.

An accessor is the wrong tool when the operation is already an entity domain verb — `Save`,
`Query`, `Remove` never pass through a capability namespace.

## Inventory (2026-08-28)

| Package | Accessor | Form | Verb struct |
|---|---|---|---|
| Koan.Jobs | `.Job` / `.Jobs` | instance + static + collection lifts | `JobOps<T>` / `JobStatics<T>` |
| Koan.Data.AI | `.Ai` | static; instance similarity is the bare verb `note.Similar()` | `AiStatics<T>` |
| Koan.Cache | `.Cache` | instance + static | `EntityCacheEntryFacet` / `EntityCacheFacet` |
| Koan.Communication | `.Events` / `.Transport` / `.EventGateway` | instance (+ lifts) + static | `EntityEventsFacet` / `EntityTransportFacet` / `EventGateway<T>` |
| Koan.Data.SoftDelete | none | bare instance verbs + static `WithDeleted()` ambient scope | — |

## Consequences

- New accessors copy an anatomy instead of a neighboring capability's source; the naming rule
  (short verb inside the facade, qualified flat static) settles the next `Search`-shaped debate
  in one line.
- The `.Ai` spelling (from `.AI`) and the `Similar` move from static gateway member to instance
  verb are breaking relative to published `Sylin.Koan.Data.AI` packages through 1.0.23. That is
  within the BUILD-0073 boundary while the framework is unannounced; the capability leaf and the
  AI how-to carry the feed-boundary note so published-package readers get the incantation that
  works today.
- Accessor names are shared vocabulary now: a rename sweeps call sites, specs, capability leaves,
  the README, and reflection/`nameof` sites with the same discipline as any contract change.
