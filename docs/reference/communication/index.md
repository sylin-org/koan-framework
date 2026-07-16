---
type: REF
domain: communication
title: "Communication — Local Entity Transport"
audience: [developers, architects, operators, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.18.0
validation:
  date_last_tested: 2026-07-15
  status: verified
  scope: foundation AddKoan process-local Transport, scalar/set/stream grammar, typed receivers, context, acceptance, and settlement
---

# Communication — local Entity Transport

Use Transport when application code intends to send the Entity snapshot it currently holds to every
local business receiver group for that Entity type. The foundation bundle includes the process-local
runtime; `AddKoan()` is the only bootstrap call.

## Shortest supported path

Define an Entity and a business-named receiver. Do not register the receiver:

```csharp
using Koan.Communication;
using Koan.Data.Core.Model;

public sealed class Order : Entity<Order>
{
    public bool Ready { get; set; }
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

Send one Entity, a finite selection, or a lazy provider-qualified stream:

```csharp
await order.Transport.Send(ct);
await orders.Where(order => order.Ready).Transport.Send(ct);
await Order.QueryStream(order => order.Ready).Transport.Send(ct);
```

Koan discovers `ImportOrder`, captures the current logical context, serializes each accepted `Order`,
and gives the receiver a fresh copy inside a scoped host/context boundary.

## Observe the result

```csharp
var accepted = await orders.Transport.Send(ct);

Console.WriteLine($"accepted={accepted.Accepted} rejected={accepted.Rejected}");

var settled = await accepted.WaitForSettlement(ct);
Console.WriteLine(
    $"delivered={settled.Delivered} filtered={settled.Filtered} failed={settled.Failed}");
```

`Send` waits for bounded publication acceptance, not receiver completion. This keeps a slow receiver
from silently changing the publisher's contract. The local-only settlement wait exists for tests,
small process-local workflows, and corrective observation; it is not a distributed transaction.

## Guarantees

- A deliberate `Send` is a new logical operation, even when the Entity contents are unchanged.
- Source order and multiplicity are retained by the built-in local runtime.
- Every receiver class is one receiver group and gets a separately deserialized copy.
- Receiver `Where` runs at typed ingress; false records a filtered settlement.
- Tenant, subject, and future axes travel through opaque Core context carriers. Communication names none
  of them.
- An absent context axis is suppressed while the handler runs.
- No receiver group is a corrective publication failure before source enumeration.
- Publication failures and cancellation expose a bounded accepted prefix.
- Handler exceptions produce failed settlement and operator logs without logging Entity payloads.
- Graceful host shutdown drains accepted local work when handlers honor cancellation.

## Configure local safety bounds

Defaults are sufficient for normal first use. Change them only for a measured application need:

```csharp
builder.Services.Configure<CommunicationOptions>(options =>
{
    options.InProcessCapacity = 512;
    options.MaxPayloadBytes = 8 * 1024 * 1024;
});
```

Invalid values fail during host startup. Oversized or unserializable snapshots fail with a
`TransportException` carrying the operation's partial acceptance.

## Inspect composition

Startup reporting and Koan facts identify:

- `communication:transport:default` → `in-process` via the built-in floor;
- `process-memory` assurance;
- typed receiver groups and their Entity contracts;
- bounded queue and payload limits; and
- the number of context carriers transported.

Read the same structured facts through `/.well-known/Koan/facts` or `koan://facts` when the Web or MCP
host surfaces are referenced.

## Current boundary

The built-in runtime is memory-only and process-local. It is not durable, does not retry, and does not
cross nodes. Events, logical channels, connector manifests/election, RabbitMQ, retries, dedupe, dead
letters, and internal Jobs/Cache bridge migration remain later R07 slices.

Do not use the deprecated generic [Messaging](../messaging/index.md) API as an alias for Transport; it
has different copy, cardinality, context, and failure semantics.
