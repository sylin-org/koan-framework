---
type: REF
domain: data
title: "Entity Lifecycle"
audience: [developers, architects, ai-agents]
last_updated: 2026-07-17
framework_version: v0.20.0
status: current
validation:
  date_last_tested: 2026-07-17
  status: tested
  scope: host ownership, stable pre-write snapshots, Data/Entity parity, transactions, bulk preflight, facts
---

# Entity Lifecycle

`Entity<T>.Lifecycle` is the host-owned policy boundary around persistence. It is deliberately not a
domain-event bus: `BeforeUpsert` decides whether and how an entity may be stored; a future
`order.Events.Raise<OrderApproved>()` expresses a business occurrence.

## Shortest path

The parameterless `AddKoan()` remains the complete bootstrap when an application has no custom
lifecycle policy. Declare business-specific behavior in the composition callback:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan(() =>
    Order.Lifecycle
        .BeforeUpsert(context =>
        {
            var prior = context.Prior;
            if (prior?.Status == OrderStatus.Shipped)
                return context.Cancel("A shipped order cannot be changed.", "order.shipped");

            context.Protect(nameof(Order.CustomerId));
            return context.Proceed();
        })
        .AfterUpsert(context =>
        {
            // The write has succeeded (and a Koan transaction has committed).
            AuditTrail.Record(context.Current.Id);
        }));
```

Declarations outside `AddKoan(...)` or module composition fail with corrective guidance. Plans belong
to the host, so sequential or simultaneous hosts do not share handlers and tests need no static reset.
Framework modules may declare their own handlers during normal Koan module registration.

## Contract

- Available phases are `BeforeLoad`, `AfterLoad`, `BeforeUpsert`, `AfterUpsert`, `BeforeRemove`, and
  `AfterRemove`.
- Distinct handlers run in declaration order. Registering the same delegate instance twice is
  idempotent.
- `Before*` handlers return `context.Proceed()` or `context.Cancel(reason, code)`. Cancellation throws
  `EntityLifecycleCancelledException` before the corresponding persistence operation.
- `context.Prior` is the stable persisted predecessor captured before the operation, or `null` for a new entity.
- `Protect`, `ProtectAll`, and `AllowMutation` guard fields from subsequent handler mutation.
- `Items` carries operation-local values between handlers; it is not shared between writes.
- Handler exceptions surface to the caller. Koan does not silently retry application handlers.

Lifecycle executes at the outer Data repository boundary. Calls through `Entity<T>`, `Data<T,TKey>`,
generated REST controllers, and generated MCP entity tools therefore have the same persistence
meaning. Caches, provider decorators, transforms, isolation predicates, and adapters remain internal
to that boundary.

## Bulk and transaction semantics

`UpsertMany` evaluates every `BeforeUpsert` handler before the first write. A rejection therefore does
not create a framework-induced partial batch. With handlers configured, Koan persists pointwise so
`AfterUpsert` corresponds to a real completed write; without handlers, adapters retain their native
bulk path.

Lifecycle does not claim cross-provider batch atomicity. `BatchOptions.RequireAtomic` remains an
adapter contract. In particular, an atomic batch containing soft deletes rejects because soft delete
is lowered to multiple canonical updates rather than falsely reported as one atomic native delete.

`AfterUpsert` is deferred until a surrounding Koan transaction commits. A rolled-back write does not
emit the after phase.

## Load meaning

Load phases run when a model materializes at the canonical repository boundary, after stored-field
transforms have restored the application representation. They may enrich the returned instance but do
not persist it. Keep load handlers deterministic and inexpensive; use a projection or explicit service
when enrichment requires substantial I/O.

## Deliberate bypasses

- `RemoveStrategy.Fast` explicitly bypasses per-entity remove lifecycle for truncate/drop-style
  cleanup. `Optimized` preserves lifecycle when remove handlers are configured.
- Provider-native instructions and raw provider queries are escape hatches, not modeled Entity
  persistence. Prefer `Entity`/`Data` operations when lifecycle guarantees matter.
- `UpsertIfChanged` compares the caller's model with the current stored application value before it
  chooses to write. Lifecycle runs only when a real `Upsert` is selected.

## Inspectability

At startup Koan reports each composed entity plan and its handler counts in the shared composition
facts using `koan.data.lifecycle.selected`. The same facts feed startup reporting, operator facts, and
agent-facing self-description. `IDataDiagnostics.GetLifecyclePlansSnapshot()` exposes the host-owned
inventory to framework integrations and tests.
