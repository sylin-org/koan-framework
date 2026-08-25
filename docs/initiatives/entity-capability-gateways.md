---
type: PLAN
domain: framework
title: "Entity capability gateways"
audience: [maintainers, framework-authors, module-authors]
status: proposed
last_updated: 2026-08-25
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-25
  status: reviewed
  scope: platform pattern ratified by Leo 2026-08-25; Canon ships the reference implementation
    (Person.Canon). Jobs and AI are the next pilots; remaining pillars follow the rollout order below.
---

# Entity capability gateways

One place per entity type where each capability's surface lives:

```csharp
Person.Canon.*      // reconciliation rules, stages, rebuild        (Sylin.Koan.Canon)
Person.Jobs.*       // schedule, queue, inspect this kind's jobs    (Sylin.Koan.Jobs)
Person.AI.*         // embed, semantic search helpers               (capability packages)
Person.Events.*     // raise/observe this kind's events             (Sylin.Koan.Communication)
```

## The law

> **Instance = this object's lifecycle. Type = this kind's capability surface.**

`person.Save()` and `person.Events.Raise(...)` act on one object (instance members, already shipped).
`Person.Jobs.Schedule(...)`, `Person.Canon.OnIntake(...)` act on the capability *for the kind* —
they resolve services through the active host and close over `TModel` for end-to-end typing.

## Mechanism

C# 14 static extension members over a capability marker, defined by the owning package:

```csharp
// Sylin.Koan.Jobs
extension(EntityJobMarker) { public static JobsGateway<T> Jobs => ...; }   // shape illustrative
```

Consequences that make Reference = Intent visible in IntelliSense:

- no package reference → the member does not exist on the entity at all;
- reference → every suitable entity gains the gateway with zero per-entity boilerplate;
- gateways are **thin routers** — they resolve the pillar's host-owned service (runtime, scheduler,
  coordinator) and never contain business logic;
- **the gateway name is owned by its pillar** (Canon owns `.Canon`, Jobs owns `.Jobs`), so
  cross-package member collisions are conventionally impossible.

Canon's implementation is the reference: `CanonEntityGateway<TModel>` (rule registration chaining,
outcome observers, reset-for-tests) plus `DefaultIntakeContributor` bridging registered lambdas into
the pipeline ahead of user Validation contributors.

## Rollout order

1. **Canon** — shipped (`Person.Canon`).
2. **Jobs** — relocate `IKoanJob<T>.Execute` discovery behind `Person.Jobs.*`; add
   `Schedule/Promote` operations over `CanonStage`-style receipts.
3. **AI** — `Person.AI.Embed/Search` bound to the model's declared embedding configuration.
4. **Events/Communication** — mirror the instance-side `order.Events.Raise` as type-scoped
   subscription/handler registration.
5. Remaining pillars case-by-case; capabilities without per-type semantics keep their pillar facades
   (`Model.*`, `Eval.*`).

## Guardrails

- Gateways hold no state beyond rule/observer registrations; anything durable belongs to the pillar.
- Static rule stores are process-global per closed type: expose reset semantics for tests and document
  that registrations are host-wide composition, not per-tenant configuration.
- Instance lifecycle verbs stay instance-side; do not duplicate them onto gateways.
