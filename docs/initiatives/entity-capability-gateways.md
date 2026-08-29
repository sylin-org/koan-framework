---
type: PLAN
domain: framework
title: "Entity capability gateways"
audience: [maintainers, framework-authors, module-authors]
status: proposed
last_updated: 2026-08-28
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-28
  status: reviewed
  scope: platform pattern ratified by Leo 2026-08-25; Canon ships the reference implementation
    (Person.Canon). Jobs pilot conformant (2026-08-25); AI pilot shipped (2026-08-28) as `.Ai`
    with the accessor anatomy codified in ARCH-0135; remaining pillars follow the rollout order below.
---

# Entity capability gateways

One place per entity type where each capability's surface lives:

```csharp
Person.Canon.*      // reconciliation rules, stages, rebuild        (Sylin.Koan.Canon)
Person.Jobs.*       // schedule, queue, inspect this kind's jobs    (Sylin.Koan.Jobs)
Person.Ai.*         // embed, semantic search helpers               (Sylin.Koan.Data.AI)
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
2. **Jobs** — gateway surface ALREADY CONFORMANT (verified 2026-08-25): `JobAccessorExtensions`
   delivers `.Job` / `.Jobs` / source `Submit` via C# 14 extension members constrained on
   `IKoanJob<T>` — pillar-owned name, thin router over `IJobCoordinator`, absent without the
   package reference. Pilot remainder, in order:
   - **Schedule** — SHIPPED (2026-08-25): `MyJob.Jobs.Schedule(action, interval|expression)` plus
     `ResetSchedules()` over the per-closed-type `JobScheduleRegistry` (process-global per type,
     corrective on conflicting re-registration, idempotent on identical); `JobScheduler` unions
     gateway registrations with attribute schedules in both the boot and due-tick paths.
   - **Promote** — stage-promotion operation over staged receipts; depends on the Staging surface
     (queue item 3), so it lands with or after that slice, not before.
   - Discovery relocation of `IKoanJob<T>.Execute` is thereby CLOSED without code: the interface
     remains the authoring contract (it owns the handler); the capability surface is the gateway.
3. **AI** — SHIPPED (2026-08-28): `EntityAiGatewayExtensions` delivers `.Ai` (`AiStatics<T>`:
   `Search` / `SearchScored` with a `SemanticSearchQuery` declaration builder, plus `Embed`) via
   C# 14 static extension members constrained on `Entity<T>` — thin router over `EntityAi` and
   the vector facade, bound to the kind's declared `[Embedding]` configuration, absent without the
   package reference. Instance similarity ships as the entity's own verb (`note.Similar()`) per
   ARCH-0135's subject rule, which now codifies the whole accessor anatomy. (Published packages
   1.0.13–1.0.23 spell the gateway `.AI` with positional parameters; 1.0.24 carries the rename.)
4. **Events/Communication** — mirror the instance-side `order.Events.Raise` as type-scoped
   subscription/handler registration.
5. Remaining pillars case-by-case; capabilities without per-type semantics keep their pillar facades
   (`Model.*`, `Eval.*`).

## Guardrails

- Gateways hold no state beyond rule/observer registrations; anything durable belongs to the pillar.
- Static rule stores are process-global per closed type: expose reset semantics for tests and document
  that registrations are host-wide composition, not per-tenant configuration.
- Instance lifecycle verbs stay instance-side; do not duplicate them onto gateways.
