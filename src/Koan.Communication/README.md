# Koan Communication

Entity Events and Transport are available automatically when an application references `Sylin.Koan`
and calls `AddKoan()`. A lower-level application can reference `Sylin.Koan.Communication` directly.
No handler registration, bus, or transport configuration is required.

```csharp
using Koan.Communication;
using Koan.Data.Core.Model;

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

var eventAccepted = await order.Events.Raise<OrderApproved>(ct);
var eventSettled = await eventAccepted.WaitForSettlement(ct);

var sendAccepted = await order.Transport.Send(ct);
var sendSettled = await sendAccepted.WaitForSettlement(ct);
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

Zero Event subscriptions is a valid occurrence. Transport with no receiver group fails before source
enumeration. Each deliberate terminal call has a new operation identity; the built-in runtime does no
retries.

## Provider election, local limits, and inspection

With no connector, the default runtime has separate bounded, single-process, memory-only lanes for
Events and Transport. Configure `CommunicationOptions.InProcessCapacity` and `MaxPayloadBytes` only
when the defaults are inappropriate.

A directly referenced connector may claim one or both lanes. Election is per lane, so a RabbitMQ
Transport connector can coexist with process-local Events. A transitive connector is inert. Direct
or explicit external intent never falls back to process-local reach when unavailable. Advanced hosts
may pin `CommunicationOptions.TransportProvider` or `EventsProvider`; normal applications should let
their direct references express intent.

Startup reporting and shared facts show both adapters, assurances, bounds, typed handler groups, and
context carriage. The same facts reach `/.well-known/Koan/facts` and `koan://facts` when those host
surfaces are present.

The local runtime is not durable and does not cross processes. The RabbitMQ connector currently
provides confirmed, durable Transport publication with group fan-out and authenticated context, but
not remote settlement, retries, deduplication, dead letters, replay, Events, or outbox coupling.
Logical channel authoring and legacy Jobs/Cache bridge migration remain later slices. Legacy
`Koan.Messaging` is not the implementation behind this API.

See the [Communication reference](../../docs/reference/communication/index.md) and
[ARCH-0113](../../docs/decisions/ARCH-0113-entity-capability-communication.md).
