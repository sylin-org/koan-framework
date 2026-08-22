---
type: RECIPE
recipe: make-repeated-reads-fast
title: "Make repeated reads fast"
domain: data
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/make-repeated-reads-fast.md
gets_you: "Expensive reads served from memory or a shared tier, without changing what is true."
works_if: "Something is read far more often than it changes, and you can say which thing."
costs: "Nothing to operate — a process-memory floor ships in the box. A shared tier adds a service to run."
ingredients:
  - "one | cache and derived state | Sylin.Koan.Cache"
  - "optional | durable local tier | Sylin.Koan.Cache.Adapter.Sqlite"
  - "optional | shared tier across nodes | Sylin.Koan.Cache.Adapter.Redis"
---

# Make repeated reads fast

Cache is an optimization. The source of truth does not move into it, and authoritative behavior does
not depend on it.

## When this is the answer

"The dashboard is slow." "We hit the database for the same thing constantly."

**Measure before reaching for this.** Caching is frequently the wrong answer to a slow read — the
actual cause is often a missing index, an unbounded query, or fetching a whole collection to show
twenty rows. Fixing those is cheaper and does not add a staleness problem. Ask what is slow and why
before adding a tier.

When it *is* the answer, the questions are:

- **What exactly is being cached, and when is it wrong?** If nobody can say when an entry becomes
  stale, it is not ready to be cached.
- **Is stale acceptable, and for how long?** Fresh-or-miss is the default; a bounded stale window is an
  explicit opt-in and a product decision, not a tuning knob.
- **One node or several?** A process-memory floor is per-node. Several nodes serving inconsistent
  values is the classic surprise, and it only shows up under load.

`Sylin.Koan.Cache` ships a **built-in process-memory floor**, so it works with no adapter at all. The
adapters are upgrades — a durable local tier, or a shared tier across nodes — not requirements.

## Assembly

```powershell
dotnet add package Sylin.Koan.Cache
```

Add an adapter only for the reason that earns it: durability across restarts, or coherence across
nodes. Configure the tier, TTL, and invalidation posture explicitly rather than accepting whatever a
default gives you.

Depth: [cache reference](https://github.com/sylin-org/koan-framework/blob/main/docs/reference/data/cache.md).

## Prove it

1. **Behavior** — a miss populates, a hit serves, and an invalidation is observed.
2. **Composition** — assert which tier actually answered. A passing test proves *something* returned a
   value, and the floor will happily stand in for the shared tier you thought you configured.
3. **Correction** — assert behavior when the shared tier is unavailable, and that any fallback to the
   source is deliberate rather than accidental.

## Boundaries

- Cache never becomes the source of truth, and authoritative rules do not move into it.
- It does not fix an unbounded query or a missing index; it hides them until the data grows.
- A cached value crossing a tenant boundary is a data leak that every permission check will pass.

## Interacts with

**Tenancy.** Cache keys must include the tenant. This is the single most common way an otherwise
correct multi-tenant application serves one customer another's data.

**Authorization.** Do not cache a per-user view under a shared key. If the value depends on who is
asking, the key does too.
