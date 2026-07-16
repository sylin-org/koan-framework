---
type: REF
domain: communication
title: "Communication — Local Entity Events and Transport"
audience: [developers, architects, operators, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.18.0
validation:
  date_last_tested: 2026-07-15
  status: verified
  scope: foundation AddKoan process-local Events and Transport, scalar/set/stream grammar, typed handlers, context, acceptance, and settlement
---

# Communication — local Entity Events and Transport

Communication gives Entity code two distinct intents without exposing a bus:

- `Events.Raise<TEvent>()` states that a typed business occurrence happened to an Entity.
- `Transport.Send()` distributes the Entity snapshot the application currently holds.

The foundation includes a faithful process-local runtime. Reference `Sylin.Koan`, call `AddKoan()`,
and write the business types; there is no handler registration or routing configuration.

## Shortest supported path

Define an Entity and business-named handlers. Koan discovers both classes:

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
        Console.WriteLine($"Approved {order.Id} as {occurrence.OccurrenceId}");
        return Task.CompletedTask;
    }
}

public sealed class ImportOrder : IReceiveEntity<Order>
{
    public bool Where(Order order) => order.Ready;

    public Task Receive(Order order, CancellationToken ct)
    {
        Console.WriteLine($"Importing {order.Id}");
        return Task.CompletedTask;
    }
}
```

Raise or send one Entity, a finite selection, or a lazy provider-qualified stream:

```csharp
await order.Events.Raise<OrderApproved>(ct);
await orders.Where(order => order.Ready).Events.Raise<OrderApproved>(ct);
await Order.QueryStream(order => order.Ready).Events.Raise<OrderApproved>(ct);

await order.Transport.Send(ct);
await orders.Where(order => order.Ready).Transport.Send(ct);
await Order.QueryStream(order => order.Ready).Transport.Send(ct);
```

## Event details

The Entity already supplies identity and state, so a payloadless event kind is the common path.
Supply a value when the occurrence has additional business details:

```csharp
[EventDetailsRequired]
public sealed record ApprovalRejected(string Reason);

await order.Events.Raise(new ApprovalRejected("Credit limit"), ct);
```

Omitting a required details value fails before Koan enumerates the source. In a handler,
`occurrence.HasDetails` and `DetailsOrDefault` are safe for optional details; `Details` fails clearly
when the occurrence was payloadless.

## Observe acceptance and settlement

```csharp
var accepted = await orders.Events.Raise<OrderApproved>(ct);
Console.WriteLine($"accepted={accepted.Accepted} subscriptions={accepted.SubscriptionGroups}");

var settled = await accepted.WaitForSettlement(ct);
Console.WriteLine(
    $"delivered={settled.Delivered} filtered={settled.Filtered} failed={settled.Failed}");
```

Transport returns the parallel `TransportAcceptance` and `TransportSettlement` types. Awaiting
`Raise` or `Send` means the bounded local lane accepted the enumerated items; it does not wait for
handler completion. Settlement observes only that operation's local targets and is not a distributed
transaction.

## Guarantees

- Every deliberate `Raise` or `Send` creates a new operation.
- Every raised Entity gets a new occurrence identity; all Event subscription groups see that same
  identity for that occurrence.
- Zero Event subscriptions is a valid zero-target occurrence. Zero Transport receiver groups is a
  corrective publication failure before source enumeration.
- Source order and multiplicity are retained by the built-in local runtime.
- Each handler group gets freshly deserialized Entity state. Event groups also get separate details
  copies; handlers never share the publisher's mutable objects or another group's objects.
- Handler `Where` runs at typed ingress; false records a filtered settlement.
- Tenant, subject, and future axes travel through opaque Core context carriers. Communication names
  none of them, and suppresses an absent axis while a handler runs.
- Publication failures and cancellation expose a bounded accepted prefix. Handler exceptions become
  failed settlement and payload-free operator logs.
- Events and Transport use separate bounded local lanes, so a slow Event handler does not block
  Transport delivery.
- Graceful host shutdown drains accepted local work when handlers honor cancellation.

## Configure local safety bounds

Defaults are sufficient for normal first use. The capacity applies independently to each local lane;
the payload limit covers an Entity snapshot plus Event details when present:

```csharp
builder.Services.Configure<CommunicationOptions>(options =>
{
    options.InProcessCapacity = 512;
    options.MaxPayloadBytes = 8 * 1024 * 1024;
});
```

Invalid values fail during host startup. Oversized or unserializable values fail with a typed lane
exception carrying the operation's partial acceptance.

## Inspect composition

Startup reporting and Koan facts identify both local lanes, their `process-memory` assurance, typed
Event subscription and Transport receiver groups, bounds, and the number of context carriers moved.
Read the same structured decisions through `/.well-known/Koan/facts` or `koan://facts` when the Web or
MCP host surfaces are referenced.

## Choose the right intent

Use `Lifecycle` for persistence behavior around load/upsert/remove. Use `Events` for a business fact
associated with an Entity. Use `Transport` when another receiver should get the current Entity copy.
Use Jobs when durable work, retry, or scheduling is the requirement.

The built-in Communication runtime is memory-only and process-local. It does not survive restart,
cross nodes, retry, deduplicate, dead-letter, replay, or couple to a Data transaction. Logical
channels, connector manifests/election, RabbitMQ conformance, and internal Jobs/Cache bridge migration
remain later R07 work.

Do not use the deprecated generic [Messaging](../messaging/index.md) API as an alias. It has different
copy, cardinality, context, and failure semantics.
