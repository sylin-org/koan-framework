---
name: koan-messaging
description: Entity communication through Events.Raise and Transport.Send, typed discovered handlers, local acceptance/settlement, context carriage, and the boundary from legacy Koan.Messaging or RabbitMQ
pillar: communication
card: docs/reference/cards/messaging.md
status: current
last_validated: 2026-07-15
---

# Koan Entity Communication

## Trigger this skill when you see

- `entity.Events.Raise<TEvent>()`, Event details, or `IHandleEntityEvent<TEntity,TEvent>`
- `entity.Transport.Send()`, Entity distribution, or `IReceiveEntity<TEntity>`
- the same operation over `IEnumerable<TEntity>` or `IAsyncEnumerable<TEntity>`
- acceptance, settlement, local fan-out, immutable copies, or context carriage
- questions about message buses, RabbitMQ, `Koan.Messaging`, `.Send()` on arbitrary objects, or
  `services.On<T>()`

## Core principle

**Application code states intent on an Entity; Communication chooses how it moves.** Use Events when a
typed business occurrence happened to an Entity. Use Transport when receivers need a copy of the
Entity state currently held. `AddKoan()` supplies both process-local paths with no registration DSL,
bus, or routing configuration.

<!-- validate -->
```csharp
using System.Threading;
using System.Threading.Tasks;
using Koan.Communication;
using Koan.Data.Core.Model;

public sealed class Order : Entity<Order>
{
    public bool Ready { get; set; }
}

public sealed record OrderApproved;

public sealed class RecordApproval : IHandleEntityEvent<Order, OrderApproved>
{
    public Task Handle(
        Order order,
        EventOccurrence<OrderApproved> occurrence,
        CancellationToken ct) => Task.CompletedTask;
}

public sealed class ImportOrder : IReceiveEntity<Order>
{
    public bool Where(Order order) => order.Ready;
    public Task Receive(Order order, CancellationToken ct) => Task.CompletedTask;
}

public static class OrderCommunication
{
    public static async Task Publish(Order order, CancellationToken ct = default)
    {
        await order.Events.Raise<OrderApproved>(ct);
        await order.Transport.Send(ct);
    }
}
```

## Choose the semantic lane

| Intent | Canonical surface | Meaning |
|---|---|---|
| Persistence rule | `Order.Lifecycle.BeforeUpsert(...)` | Data behavior around load/upsert/remove |
| Business occurrence | `order.Events.Raise<OrderApproved>()` | a typed fact happened to this Entity |
| Entity distribution | `order.Transport.Send()` | deliver isolated copies of current Entity state |
| Durable/retried work | `job.Job.Submit(...)` | ledger-backed execution, not an Event guarantee |

The terminals lift pointwise over one Entity, a finite collection, and a lazy async stream. Standard
LINQ or a provider-qualified Data query selects senders; there is no routing DSL.

## Event details and outcomes

Prefer payloadless event-kind tokens because the Entity supplies identity and state. Pass an explicit
details value when the occurrence needs more information. `[EventDetailsRequired]` makes omission a
pre-enumeration error.

Awaiting Raise/Send means bounded publication acceptance, not handler completion. Call
`WaitForSettlement(ct)` on the returned receipt only when the caller needs local correlated
observation. Event handler filtering/failure and Transport receiver filtering/failure settle
independently.

## Guarantees and current boundary

- Every handler group receives newly deserialized Entity state; Event details are copied per group.
- Context carriers are captured once and restored without Communication naming tenant or subject.
- Zero Event subscriptions is valid; zero Transport receivers fails before source enumeration.
- Events and Transport have separate bounded local lanes and drain on cooperative host shutdown.
- The built-in adapter is process-local, memory-only, and has no retry, durability, outbox, replay,
  dead-letter, or transaction-coupling guarantee.

External connector/channel election and RabbitMQ parity are not yet shipped. The old arbitrary-object
`Koan.Messaging` surface (`message.Send()`, `services.On<T>()`, startup proxy/buffer, interceptors) is
deprecated and remains only for unmigrated internal bridges and repository demonstrations. Do not
teach it as the application path or adapt it beneath Entity Communication.

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `.Send()` on an arbitrary DTO as new application code | Model the business subject as an Entity and choose `.Events.Raise<E>()` or `.Transport.Send()`. |
| `services.On<T>(...)` or a handler lambda | A business-named `IHandleEntityEvent<TEntity,TEvent>` or `IReceiveEntity<TEntity>` class; discovery is automatic. |
| Persistence hooks called `Events` | `Entity.Lifecycle`; Events are business occurrences. |
| Injecting a bus only to publish | Use the Entity terminal; transport is an internal adapter concern. |
| Assuming Raise waits for side effects | Inspect `EventAcceptance`, then explicitly wait for local settlement when needed. |
| Treating a collection Raise as one group fact | Model the group as its own Entity; collection terminals mean one occurrence per yielded Entity. |
| Claiming tenant isolation from Communication-specific code | Context axes are owned by their modules; Communication carries opaque sealed context. |
| Claiming RabbitMQ/durability because a legacy connector is referenced | The current supported ring is local-only until connector conformance ships. |

## See also

- [Communication reference](../../../docs/reference/communication/index.md) — current supported API
- [Messaging card](../../../docs/reference/cards/messaging.md) — legacy boundary and replacement
- [ARCH-0113](../../../docs/decisions/ARCH-0113-entity-capability-communication.md) — semantic laws
- [Entity semantics contract](../../../docs/architecture/entity-semantics-contract.md) — capability ring
- [koan-jobs](../koan-jobs/SKILL.md) — choose durable work when delivery is not enough
