---
id: ARCH-0106
slug: entity-semantics-contract
domain: Architecture
status: Accepted
date: 2026-07-13
title: Entity language admission, facets, and responsibility boundaries
---

# ARCH-0106: Entity language admission, facets, and responsibility boundaries

> **2026-07-15 amendment:** [ARCH-0113](ARCH-0113-entity-capability-communication.md) retains this
> decision's Entity admission and module-facet rules but supersedes its lifecycle/event/messaging and
> compatibility posture. Persistence hooks are `Lifecycle`; `Events` and `Transport` are distinct
> Entity intents over one Communication pillar; greenfield replacement does not presume aliases.

## Context

ARCH-0105 ratified `Entity<T>` as Koan's semantic spine and IntelliSense as its primary discovery
surface. R02 then found a strong verified data core alongside uneven extension maturity. R03's
[consumer inventory](../initiatives/koan-v1/R03-ENTITY-INVENTORY.md) shows that the current Entity
surface combines identity, Active Record persistence, queries, relationships, lifecycle registration,
cache operations, context, provider control, and host-global service access.

Several current shapes undermine the intended experience:

- persistence and messaging extensions attach plausible verbs to every `object` or class and reject
  invalid receivers only at runtime;
- Data.Core exposes cache members even when the cache module is unavailable;
- backup and view-rebuild operations appear instance-local while acting on a whole type/control plane;
- a backup deletion method returns successful placeholder behavior;
- relationship helpers can conceal an unbounded load-and-filter fallback;
- lifecycle/service caches are static and can outlive a host;
- module references activate runtime behavior but do not consistently make module Entity extensions
  discoverable without an additional namespace import.

The project needs a rule that preserves Koan's Rails-like speed without allowing Entity-first to mean
everything-on-Entity.

Focused [ecosystem design mining](../initiatives/koan-v1/R03-ECOSYSTEM.md) found complementary lessons:
Rails validates direct convention-led model grammar; ABP distinguishes entity/aggregate invariants,
application workflows, repositories, unit of work, domain events, integration events, and module
lifecycle; EF Core reinforces short-lived host-owned units of work; C# 14 enables constrained static
and instance extension members.

## Decision

Adopt the canonical
[`Entity Semantics Contract`](../architecture/entity-semantics-contract.md).

### Semantic locations

Every capability has one primary location:

1. Entity instance for state, invariants, and receiver-essential behavior;
2. Entity type/set for reads, queries, and type-wide entity capability facets;
3. Entity operation context for typed scope, transaction, and deliberate execution overrides;
4. a plain application workflow for multi-entity/external-system use cases;
5. an explicit framework/operator control plane for administration, topology, migrations, backup
   catalogs, health, and provider management.

Only the first three are the Entity language. This does not make workflows second-class; it prevents
infrastructure coordination from looking intrinsic to every entity.

### Admission

An Entity member must pass the contract's subject, granularity, compile-time validity, business
readability, provider/cost honesty, context safety, inspectability, name-budget, and evidence/removal
tests. An entity appearing somewhere in an operation is not sufficient.

Broad receivers (`this object`, unconstrained reference types), unused instance receivers, silent
unbounded fallback, and success-shaped placeholders are rejected.

### Module-grown IntelliSense

Koan will use C# 14 constrained extension members for new module-contributed Entity language:

- direct instance extensions only for unmistakable receiver-local verbs;
- static noun facets for type-wide capability, such as the contract shapes `Article.Semantic` and
  `Article.Cache`;
- extension containers in the canonical Entity language namespace so a normal model import is enough;
- no placeholder members in Data.Core for absent modules and no package-injected broad global usings;
- compile-consumer collision/absence/presence tests for every contributing module.

The mechanism was proven with a disposable .NET 10 compile probe for a static and instance extension
on a constrained Entity subtype. The probe was removed; R04 will create repository-owned test cells.

### Context, lifetime, and events

Entity runtime state is host-owned. Static process-wide caches are allowed only for immutable,
host-independent metadata. Context is immutable, nested, and explicit about capture/suppression across
dispatch. Business/request scope and transactions remain common vocabulary; adapter/cache/provider
overrides are expert controls.

Entity invariants, persistence lifecycle hooks, deferred domain events, integration messages, and
framework projections are separate mechanisms with named transaction timing. Integration reliability
requires a declared immediate/outbox contract; a generic `.Send()` does not imply atomic persistence.

### Canonical verbs and escape hatches

`Save` and `Remove` are the canonical business-facing instance persistence verbs. Advanced storage
terms (`Upsert`, raw queries, bulk replacement, provider instructions), `Data<T,TKey>`, repositories,
and services remain explicit escape hatches rather than duplicated common-path vocabulary.

This decision defines direction and migration; it does not remove existing APIs immediately.

## Alternatives considered

### Keep adding direct members to the base class

Rejected. It maximizes immediate visibility but makes absent capabilities appear available, couples
Data.Core to optional modules, and consumes an unbounded IntelliSense budget.

### Put every module API in its own namespace/service

Rejected as the common path. It preserves assembly ownership but loses Entity-first discovery and
forces developers/agents to know disconnected infrastructure vocabularies before IntelliSense can
help.

### Inject module global usings through packages

Rejected as the default mechanism. It changes an application's global namespace scope and increases
collision risk beyond Entity language. A shared, narrow Entity namespace plus constrained extensions
is more local and predictable.

### Require repositories and application services for all data access

Rejected. This would import ABP/DDD layering cost into the smallest use cases and violate meaningful
small steps. Repositories and workflows remain valuable optional boundaries when the domain earns
them.

### Use a dynamic property/capability bag

Rejected for app-owned entities. It weakens compile-time discovery, refactoring, schemas, agent safety,
and business readability. A future third-party module-extension seam may be considered separately.

## Consequences

### Positive

- Module references can grow IntelliSense honestly without growing Data.Core.
- Application code retains direct Rails-like persistence while gaining ABP-like responsibility and
  transaction boundaries.
- Agents and developers get compile-time rejection instead of plausible invalid verbs.
- Type-wide capability, instance behavior, workflow, and operator control become visibly distinct.
- Provider cost/fallback, event timing, and host lifetime become part of the semantic contract.

### Costs

- C# 14 extension facets need consumer compilation tests and careful namespace governance.
- Existing aliases, broad receivers, partial members, and static registries require staged migration.
- Framework authors must spend a scarce name/facet budget and provide operation explanation.
- Documentation and samples will need to distinguish current syntax from target syntax during
  consolidation.

### Compatibility

- Existing APIs are inventoried before removal.
- Safe forwarding and `[Obsolete]` periods are preferred for naming/facet moves.
- False-success behavior, especially placeholder destructive operations, is repaired or disabled
  immediately rather than preserved as compatibility.
- Pre-1.0 status allows cleanup, but migration remains explicit and proportionate.

## Evidence reviewed

- `src/Koan.Data.Core/Model/Entity.cs`, `src/Koan.Cache/Entity/EntityCacheFacet.cs`,
  `AggregateExtensions.cs`, `EntityContext.cs`, and Entity lifecycle types.
- Entity extensions in cache, AI, media, messaging, backup, canon, relationships, and data movement.
- R02 capability/test/package evidence and the R03 consumer-visible inventory.
- Current primary documentation from ABP, Rails, Microsoft .NET/EF Core, and C# 14, linked in the R03
  design-mining record.

## Follow-up

R04 must produce dependency-ordered implementation cards, beginning with false-success and host
lifetime hazards, then broad receivers/capability checks, then C# 14 facets and vocabulary cleanup.
R05 must prove the resulting language through a clean anonymous V0-to-V1 journey and consumer/agent
tests.
