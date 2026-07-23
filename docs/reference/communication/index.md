---
type: REF
domain: communication
title: "Raise occurrences and send Entity snapshots"
audience: [developers, architects, operators, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: verified
  scope: local Events/Transport, startup-declared business channels, internal routes, and directly elected RabbitMQ carriage
---

# Raise occurrences and send Entity snapshots

Communication gives Entity code two distinct intents without exposing a bus:

- `Events.Raise<TEvent>()` states that a typed business occurrence happened to an Entity.
- `Transport.Send()` distributes the Entity snapshot the application currently holds.

The foundation includes a faithful process-local runtime. Reference `Sylin.Koan`, call `AddKoan()`,
and write the business types; there is no handler registration or routing configuration. A direct
connector reference can change physical reach without changing this application grammar.

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
`Raise` or `Send` means the elected provider accepted the enumerated items at its reported assurance
boundary; it does not wait for handler completion. Receipts identify provider, channel, assurance,
and whether settlement is observable. The local provider supports operation-scoped settlement;
external providers may not.

## Guarantees

- Every deliberate `Raise` or `Send` creates a new operation.
- Every raised Entity gets a new occurrence identity; all Event subscription groups see that same
  identity for that occurrence.
- Zero Event subscriptions is a valid zero-target occurrence. A known-local zero Transport receiver
  set fails before source enumeration; an external mandatory route may report it at publication.
- Source order and multiplicity are retained by the built-in local runtime.
- Each handler group gets freshly deserialized Entity state. Event groups also get separate details
  copies; handlers never share the publisher's mutable objects or another group's objects.
- Handler `Where` runs at typed ingress; false records a filtered settlement.
- Tenant, subject, and future axes travel through opaque Core context carriers. Communication names
  none of them, and suppresses an absent axis while a handler runs.
- Publication failures and cancellation expose a bounded accepted prefix. Handler exceptions become
  failed settlement and payload-free operator logs.
- Events, Transport, and framework signals use separate bounded local lanes, so a slow handler in one
  lane does not block another.
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

## Choose a business channel

The inferred `default` channel is the shortest path and needs no configuration. Declare a named
channel only when a business flow needs different reach or assurance:

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

Use the name only at the existing terminal:

```csharp
await urgentOrders.Transport.Send(ct, channel: "priority");
await order.Events.Raise<OrderEscalated>(ct, channel: "priority");
```

Each channel may independently set `TransportProvider` and `EventsProvider`. An omitted setting uses
the same direct-reference or built-in-floor election as `default`; in this example Events continue to
use the local provider because RabbitMQ does not claim Events. Every discovered public receiver or
subscription group binds to every declared public channel. Unknown channels fail before source
enumeration, and startup rejects invalid declarations.

Channel names are normalized to lowercase, must start with a letter or digit, may contain letters,
digits, `.`, `_`, and `-`, and may be at most 64 characters. `default` is reserved and configured with
the existing top-level provider settings. A channel selects route policy; it is not authorization,
confidentiality, receiver filtering, dynamic topology, mirroring, or failover.

## Add RabbitMQ Transport

Reference the connector directly from the application:

```powershell
dotnet add package Sylin.Koan.Communication.Connector.RabbitMq
```

That direct reference elects RabbitMQ for `Transport/default` and both internal default routes;
Events remain process-local. A
transitive connector remains inert. Standard Aspire, Compose, Docker/Podman, Kubernetes, or another
application-owned topology supplies the broker; Koan consumes
`ConnectionStrings:RabbitMq=amqp://user:password@host:5672`.

The Entity and receiver code does not change. RabbitMQ creates a durable queue per stable receiver
group, confirms mandatory persistent publications, restores authenticated context at ingress, and
manually acknowledges handler outcomes. The sender knows broker acceptance, not remote handler
settlement, so `TransportAcceptance.SettlementObservable` is false and `WaitForSettlement()` fails
with `SettlementUnavailable`.

If the directly intended provider is unavailable, startup or publication fails. Koan does not fall
back to local Transport because doing so would silently change reach. Current RabbitMQ limits include
no Events, retry, dedupe, inbox/outbox, dead letters, replay, schema negotiation, or exactly-once
side-effect claim.

Framework signals are not an application bus. Koan modules use them for lossy internal hints; Jobs
wake is the first consumer. Adding Jobs requires no messaging package or registration, and adding the
RabbitMQ connector transparently extends the wake mesh while the durable ledger remains authoritative.

## Inspect composition

Startup reporting and Koan facts identify each lane/channel's elected provider, selection reason,
assurance, settlement observability, typed group binding, applicable bounds, and the number of context
carriers moved.
Health is non-critical for an unelected candidate and critical for an elected external provider. Read
the same structured decisions through `/.well-known/Koan/facts` or `koan://facts` when the Web or MCP
host surfaces are referenced.

## Choose the right intent

Use `Lifecycle` for persistence behavior around load/upsert/remove. Use `Events` for a business fact
associated with an Entity. Use `Transport` when another receiver should get the current Entity copy.
Use Jobs when durable work, retry, or scheduling is the requirement.

The built-in Communication runtime is memory-only and process-local. It does not survive restart,
cross nodes, retry, deduplicate, dead-letter, replay, or couple to a Data transaction. Direct provider
election, RabbitMQ Transport, Jobs wake, Cache coherence, and startup-declared business channels are
supported. Dynamic channels, RabbitMQ Events, additional providers, automatic branching, mirroring,
and failover are not.
