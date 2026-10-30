# Sylin.Koan.Communication

Entity Events and Transport are available automatically when an application references `Sylin.Koan`
and calls `AddKoan()`. A lower-level application can reference `Sylin.Koan.Communication` directly.
No handler registration, bus, or transport configuration is required.

## Install

```powershell
dotnet add package Sylin.Koan.Communication
```

## Meaningful result

```csharp
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
        CancellationToken ct)
    {
        // business reaction to an occurrence
        return Task.CompletedTask;
    }
}

public sealed class ImportOrder : IReceiveEntity<Order>
{
    public bool Where(Order order) => order.Ready;

    public Task Receive(Order order, CancellationToken ct)
    {
        // business code over an isolated snapshot
        return Task.CompletedTask;
    }
}

public static class OrderFlow
{
    public static async Task Publish(Order order, CancellationToken ct)
    {
        var eventAccepted = await order.Events.Raise<OrderApproved>(ct);
        var eventSettled = await eventAccepted.WaitForSettlement(ct);

        var sendAccepted = await order.Transport.Send(ct);
        var sendSettled = await sendAccepted.WaitForSettlement(ct);
    }
}
```

Both terminals work pointwise for an Entity, a finite set, or a lazy async stream. Event kinds are
payloadless by default; pass a details value when the fact needs more information and mark contracts
that require it with `[EventDetailsRequired]`.

Communication serializes each accepted Entity. Every discovered handler group receives a fresh copy;
Event details are copied per subscription too. The operation captures composed Koan context carriers
once and restores them around every handler, without Communication naming tenant, subject, or any
other axis.

## Acceptance, providers, and settlement

Awaiting `Raise` or `Send` means the elected route accepted the enumerated items at its reported
assurance boundary. It does not wait for handler code. Each receipt reports fixed-size aggregate
counts, provider, channel, assurance, and whether settlement is observable. Publication failures and
cancellation carry the accepted prefix.

The built-in process-local provider supports operation-scoped `WaitForSettlement`; filtering and
handler failure are settlement outcomes. External providers may report that remote settlement is not
observable, in which case waiting fails correctively rather than pretending that broker acceptance
means business completion.

Publishers never wait on handlers. In the process-local provider every receiver and subscription
group has its own backlog and one sequential consumer, so a slow or hung handler holds only its own
group, and each group receives deliveries in publication order. A backlog is bounded by memory, not by
a count: a handler whose backlog stays within `InProcessBacklogBytes` never loses a delivery. A group
whose waiting publications reach the budget fails further deliveries at once and visibly; settlement
counts each in both `Failed` and `Overflowed`, a warning names the group, and the publisher and every
other group are unaffected. Each handler runs under `HandlerTimeout`: its cancellation token is cancelled when the
timeout passes, and a handler that ends because of it is a failed delivery.

Zero Event subscriptions is a valid occurrence. Transport with no receiver group fails before source
enumeration. Each deliberate terminal call has a new operation identity; the built-in runtime does no
retries.

## Provider election, local limits, and inspection

With no connector, the default runtime carries Events, Transport, competing-group framework signals,
and every-node framework broadcasts in a single process and in memory, with one backlog per bound
group. The internal routes are not an application bus: Jobs uses the competing-group route for wake
hints, while Cache uses the every-node route for peer L1 invalidation.
`CommunicationOptions.InProcessBacklogBytes` (default 64 MiB) is the memory one group's waiting
publications may hold, counted as their encoded bytes, with a publication shared by several groups
counted once in each; a group exceeds it by at most one publication, plus per-delivery bookkeeping and
the one delivery in flight; `HandlerTimeout` (default two minutes) bounds one handler invocation. The process-local provider has no payload limit; a provider whose transport has one
declares it, and Koan rejects a larger publication with `PayloadTooLarge` before handing it over.

A directly referenced connector may claim supported lanes. Election is per lane, so RabbitMQ can
carry Transport and both internal routes while Events remain process-local. A transitive connector is inert unless
its owning engine activates a layered capability—for example, an active Redis L2 activates Redis Cache broadcast.
Direct or explicit external intent never falls back to process-local reach when unavailable. Advanced hosts
may pin `CommunicationOptions.TransportProvider`, `EventsProvider`, `FrameworkSignalsProvider`, or
`FrameworkBroadcastsProvider`;
normal applications should let their direct references express intent.

## Business channels

Most application code should stay on the inferred `default` channel. When a workflow needs different
reach or assurance, declare a stable business name at startup and use it at the existing terminal:

```json
{
  "Koan": {
    "Communication": {
      "Channels": {
        "priority": {
          "TransportProvider": "rabbitmq"
        }
      }
    }
  }
}
```

```csharp
await urgentOrders.Transport.Send(ct, channel: "priority");
await order.Events.Raise<OrderEscalated>(ct, channel: "priority");
```

`TransportProvider` and `EventsProvider` are independent and optional. An omitted pin uses the same
direct-reference or built-in-floor election as `default`; therefore the example keeps
`Events/priority` process-local. Every discovered handler group binds to every startup-declared
public channel. Unknown channels reject before Koan enumerates the source.

A channel chooses route policy; it is not authorization, confidentiality, a receiver predicate, or a
provider type in business code. Names are case-normalized, must begin with a letter or digit, may
contain letters, digits, `.`, `_`, and `-`, and are limited to 64 characters. Dynamic channels,
automatic branching, mirroring, and failover are not supported.

Startup reporting and shared facts show every lane/channel election, assurance, applicable bound,
typed handler binding, and context carriage. The same facts reach `/.well-known/Koan/facts` and
`koan://facts` when those host surfaces are present.

The local runtime is not durable, does not cross processes, and does not retry. The RabbitMQ connector
provides confirmed Transport, competing-group signals, and ephemeral per-node broadcast subscriptions with
authenticated context, and redelivers a failed durable-group delivery a bounded number of times before
dead-lettering it, but
not remote settlement, deduplication, replay, Events, or outbox coupling.
Jobs wake and Cache peer invalidation both reuse Communication while retaining their different delivery topology.
No arbitrary-object messaging surface is implemented underneath this API.

See the [Communication reference](https://github.com/sylin-org/koan-framework/blob/main/docs/reference/communication/index.md)
and [ARCH-0113](https://github.com/sylin-org/koan-framework/blob/main/docs/decisions/ARCH-0113-entity-capability-communication.md).
