# Koan Communication

Entity Transport is available automatically when an application references `Sylin.Koan` and calls
`AddKoan()`. A lower-level application can reference `Sylin.Koan.Communication` directly. No receiver
registration or transport configuration is required.

```csharp
using Koan.Communication;

public sealed class ImportOrder : IReceiveEntity<Order>
{
    public bool Where(Order order) => order.Ready;

    public Task Receive(Order order, CancellationToken ct)
    {
        // business code
        return Task.CompletedTask;
    }
}

var accepted = await order.Transport.Send(ct);
var settled = await accepted.WaitForSettlement(ct);
```

The same terminal works pointwise for a finite set or a lazy async stream:

```csharp
await orders.Transport.Send(ct);
await Order.QueryStream(order => order.Ready).Transport.Send(ct);
```

`Send` serializes each Entity when the operation accepts it. Every discovered receiver group gets a
fresh deserialized copy; handlers never share the sender's mutable object or another group's object.
The operation captures all composed Koan context carriers once at terminal invocation and restores
them around each receiver. Tenant and subject semantics therefore remain owned by their modules,
while Communication transports their opaque values.

## Acceptance and settlement

Awaiting `Send` means the bounded local channel accepted the enumerated snapshots. It does not wait
for receiver code:

- `TransportAcceptance` reports enumerated, accepted, and rejected counts plus the operation id,
  receiver-group count, selected adapter, and assurance.
- `WaitForSettlement` waits only for that operation and returns delivered, filtered, and failed
  target counts.
- A missing receiver fails before source enumeration.
- Publication cancellation and other publication failures carry the accepted prefix.
- Receiver filters and handler failures are terminal settlement outcomes, not publication failures.

Every deliberate `Send` has a new operation identity. The built-in local runtime performs no retries.

## Local limits

The built-in floor is a bounded, single-process, memory-only channel. Configure its safety bounds only
when the defaults are inappropriate:

```csharp
builder.Services.Configure<CommunicationOptions>(options =>
{
    options.InProcessCapacity = 512;
    options.MaxPayloadBytes = 8 * 1024 * 1024;
});
```

Inspect `communication:transport:default`, discovered receiver groups, assurance, queue capacity,
payload limit, and context carriage through Koan's startup report and `/.well-known/Koan/facts` or
`koan://facts` when those host surfaces are present.

## Boundaries

- The local channel is not durable and does not cross processes or survive restart.
- Receiver handlers must honor their cancellation token so host shutdown can drain responsibly.
- Source order and multiplicity are preserved by the local runtime; no collection atomicity is implied.
- Transport does not save, reload, or otherwise involve the Data pipeline.
- Events, connector election, broker retries, dead letters, and RabbitMQ parity are later Communication
  slices. Legacy `Koan.Messaging` is not the implementation behind this API.

See the [Communication reference](../../docs/reference/communication/index.md) for the supported path
and [ARCH-0113](../../docs/decisions/ARCH-0113-entity-capability-communication.md) for the semantic laws.
