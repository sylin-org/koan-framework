# Sylin.Koan.Communication.Connector.RabbitMq

`Sylin.Koan.Communication.Connector.RabbitMq` carries Entity Transport and framework-owned internal
signals across a RabbitMQ mesh while leaving the application language unchanged:

```powershell
dotnet add package Sylin.Koan.Communication.Connector.RabbitMq
```

## Meaningful result

```csharp
using Koan.Communication;

public sealed class ImportOrder : IReceiveEntity<Order>
{
    public Task Receive(Order order, CancellationToken ct)
    {
        // business code over an isolated Order snapshot
        return Task.CompletedTask;
    }
}

await order.Transport.Send(ct);
```

A direct application reference is the routing decision. `AddKoan()` discovers the connector and
elects RabbitMQ for `Transport/default`, `FrameworkSignals/default`, and `FrameworkBroadcasts/default`;
Entity Events remain on Koan's
built-in process-local provider. A transitive reference does not change network reach. No bus registration, queue names,
handler registration, or provider-selection code is required.

The default may remain process-local while a named business channel uses RabbitMQ by pinning the
channel at startup:

```json
{
  "Koan": {
    "Communication": {
      "TransportProvider": "in-process",
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
```

The channel name is business policy; RabbitMQ owns exchange, route, and queue realization. Participants
that publish or receive the same flow must declare the same normalized channel name; this generation
does not negotiate channel declarations between heterogeneous applications.

## Connection

Run RabbitMQ with Aspire, Compose, Docker, a managed service, or another standard topology owner. The connector
discovers applicable service endpoints; set one explicit endpoint when discovery is not appropriate:

```json
{
  "ConnectionStrings": {
    "RabbitMq": "amqp://app:secret@rabbitmq:5672"
  }
}
```

`RABBITMQ_URL` and Aspire service discovery are also recognized. When a discovered URL has no credentials,
`Username` and `Password` retain their documented `koan` defaults.

## What acceptance means

Awaiting `Send` means RabbitMQ confirmed a persistent publication to Koan's durable exchange and at
least one receiver-group route existed. It does not mean a remote handler completed. The returned
`TransportAcceptance` reports adapter `rabbitmq`, assurance `durably-acknowledged`, and
`SettlementObservable=false`; `WaitForSettlement()` fails correctively because remote settlement is
not observable in this generation.

Each stable receiver group has its own durable quorum queue, consumed on a channel of its own, so a
slow or hung group never delays another. Replicas of that group compete on the same queue, while
different groups each receive a serialized copy. Consumers use bounded prefetch and manual
acknowledgement. A delivery whose handler fails or exceeds Koan's handler timeout is redelivered up to
five times; after that RabbitMQ dead-letters it to the group's own dead-letter queue
(`{group queue}.dead-letter`), where an operator can inspect, replay, or purge it. An envelope that
fails authentication is dead-lettered at once. Node broadcasts instead use one non-durable, auto-delete
queue per active host, so every live node receives a copy without leaving durable queues behind after
it exits; a failed broadcast is dropped, because its owning module keeps a correctness fallback. Koan
carries the host's opaque context envelope and authenticates it before restoring tenant or other
registered axes around handler execution.

Publications share one confirm-tracking channel and wait only for their own broker confirm, so
concurrent senders do not queue behind one another's round trips.

If the directly intended connector cannot start or publish, startup/send fails with provider-specific
correction. Koan never silently falls back to process-local Transport, because that would change
application reach.

## Options

All options live under `Koan:Communication:RabbitMq`:

- `Username` / `Password` — credentials added to discovered endpoints without user information.
- `MeshTrustKey` — optional explicit HMAC material; otherwise the authenticated broker credential is
  used. Applications on the same mesh must use the same material.
- `Prefetch` — maximum unacknowledged deliveries held by each receiver group; default `32`.
- `PublishTimeout` — confirmed-publication timeout; default `00:00:15`.
- `MaxMessageBytes` — the broker's `max_message_size`; default `16777216`, RabbitMQ 4's default. A
  larger publication fails with `PayloadTooLarge` before it reaches the broker. Set it when the broker
  is configured differently.

Startup facts report each RabbitMQ-elected lane/channel, why it won, its assurance, its bound receiver
groups, and the fact that remote settlement is unobservable. Health `communication.rabbitmq` becomes
critical only when RabbitMQ is elected.

## Deliberate limits

This connector does not currently provide Entity Events, inbox/outbox, deduplication, replay,
ordering guarantees beyond RabbitMQ's queue behavior, remote handler settlement, transactional
coupling to Data, schema aliases/migrations, or exactly-once side effects. Redelivery means a handler
can run more than once for one snapshot, and a redelivered snapshot can arrive after later ones. The
application mesh and CLR contract identities must match across participants.

The topology generation is `v4`. Exchanges and durable queues of earlier generations are not consumed
or removed automatically; operators retaining a broker retire them after all participants move
together.

Framework signals are reserved for Koan modules. Applications do not receive a generic publish or
subscribe API. Jobs wake uses competing groups; Cache peer invalidation uses every-node delivery.
Both owning modules retain their correctness fallback.

This connector implements Entity Transport. It does not add arbitrary-message publication, Events,
or a second communication grammar underneath `Entity.Transport`.

See [technical details](TECHNICAL.md) and the
[Communication reference](../../../../docs/reference/communication/index.md).
