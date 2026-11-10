---
type: REFERENCE
domain: data
title: "Entity Lifecycle"
audience: [developers, architects, ai-agents]
last_updated: 2026-07-17
framework_version: v1.0.0
status: current
validation:
  date_last_tested: 2026-07-17
  status: tested
  scope: host ownership, stable pre-write snapshots, Data/Entity parity, transactions, bulk preflight, facts
---

# Entity Lifecycle

`Entity<T>.Lifecycle` is the host-owned policy boundary around persistence, and it is deliberately not
a domain-event bus. The two answer different questions: `BeforeUpsert` decides whether and how an
entity may be stored, while `order.Events.Raise<OrderApproved>()` states that something happened to
one that already was.

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
            // The write has succeeded. Inside a deferred transaction it has also committed; inside a
            // hold it has not yet, and rolls back with the hold.
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
- `Protect`, `ProtectAll`, and `AllowMutation` guard fields from subsequent handler mutation. The guard is for
  handlers: after the write, protection is rebased to the persisted row, so the identity the write mints and the
  `[Timestamp]` and contributor stamps it applies never trip it (DATA-0132).
- `Items` carries operation-local values between handlers; it is not shared between writes.
- Handler exceptions surface to the caller. Koan does not silently retry application handlers.

Lifecycle executes at the outer Data repository boundary. Calls through `Entity<T>`, `Data<T,TKey>`,
generated REST controllers, and generated MCP entity tools therefore have the same persistence
meaning. Caches, provider decorators, transforms, isolation predicates, and adapters remain internal
to that boundary.

## Declared transitions

A state member that changes only along a table declares it once, and the lifecycle judges every save by it (DATA-0171):

```csharp
builder.Services.AddKoan(() =>
    Order.Lifecycle.Transitions(order => order.Status,
        (OrderStatus.Open, OrderStatus.Shipped),
        (OrderStatus.Open, OrderStatus.Cancelled)));
```

It is one `BeforeUpsert` rule. A save whose row stood at one value and would stand at another that no pair names throws `Refusal`,
a conflict whose word is `transition` and whose sentence names the member, the value it stands at, the one asked for and where it
goes from there; a command answers `Outcome.Refused`, and a declared endpoint 409. Each value is worded as itself, or by the function
`Transitions(member, word, …)` takes, as a client sends it, and a null as `none`. A save that leaves the member as it stands passes,
and so does one with no row as it stood: a first save, and an `Insert`. The rule judges the member as it stands when it runs, so
declare it after any rule that sets the member. Outside a hold a save is judged against the row as it was read before the write. An
exact transfer skips the lifecycle and is not judged; under a deferred transaction a save is judged at `Commit`, against the instance
as it stands then, and the refusal is the inner exception of the `TransactionException` it throws.

## Bulk and transaction semantics

`UpsertMany` evaluates every `BeforeUpsert` handler before the first write. A rejection therefore does
not create a framework-induced partial batch. With handlers configured, Koan persists pointwise so
`AfterUpsert` corresponds to a real completed write; without handlers, adapters retain their native
bulk path.

`Batch().Delete(id)` runs `BeforeRemove` only for ids the caller can read in its current scope, and
only those are deleted; an invisible or missing id reports `Missing` and runs no handler. When the
provider reports complete item outcomes, `AfterRemove` runs for each delete it reports `Applied`
(DATA-0124).

Lifecycle does not claim cross-provider batch atomicity. `BatchOptions.RequireAtomic` remains an
adapter contract. In particular, an atomic batch containing soft deletes rejects because soft delete
is lowered to multiple canonical updates rather than falsely reported as one atomic native delete.

Inside a deferred `EntityContext.Transaction`, `AfterUpsert` waits until the scope commits, and a
rolled-back write does not emit the after phase. Inside a hold (`EntityContext.Hold`, DATA-0166) a write
acts when it is issued, so `AfterUpsert` runs then, before the hold commits: what it writes on the hold's
route commits or rolls back with the hold, and anything it tells beyond the store must wait for
`EntityContext.UncommittedScope` to clear or use the hold's `AfterCommit`. An `EntityController<T>` write
is such a hold where its provider offers one (WEB-0086).

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
