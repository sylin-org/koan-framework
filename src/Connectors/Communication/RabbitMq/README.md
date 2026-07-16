# Koan Communication RabbitMQ

`Sylin.Koan.Communication.Connector.RabbitMq` carries Entity Transport and framework-owned internal
signals across a RabbitMQ mesh while leaving the application language unchanged:

```powershell
dotnet add package Sylin.Koan.Communication.Connector.RabbitMq
```

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
elects RabbitMQ for `Transport/default` and `FrameworkSignals/default`; Entity Events remain on Koan's
built-in process-local provider. A transitive reference does not change network reach. No bus registration, queue names,
handler registration, or provider-selection code is required.

## Connection

With Koan orchestration, the direct reference can provision and discover RabbitMQ with no
configuration. To use an existing broker, set one endpoint:

```json
{
  "Koan": {
    "Communication": {
      "RabbitMq": {
        "ConnectionString": "amqp://app:secret@rabbitmq:5672"
      }
    }
  }
}
```

`RABBITMQ_URL`, `Koan_RABBITMQ_URL`, Aspire service discovery, and
`ConnectionStrings:rabbitmq` are also recognized. When a discovered URL has no credentials,
`Username` and `Password` default to `koan` for the Koan-provisioned container.

## What acceptance means

Awaiting `Send` means RabbitMQ confirmed a persistent publication to Koan's durable exchange and at
least one receiver-group route existed. It does not mean a remote handler completed. The returned
`TransportAcceptance` reports adapter `rabbitmq`, assurance `durably-acknowledged`, and
`SettlementObservable=false`; `WaitForSettlement()` fails correctively because remote settlement is
not observable in this generation.

Each stable receiver group has its own durable queue. Replicas of that group compete on the same
queue, while different groups each receive a serialized copy. Consumers use bounded prefetch and
manual acknowledgement. Koan carries the host's opaque context envelope and authenticates it before
restoring tenant or other registered axes around handler execution.

If the directly intended connector cannot start or publish, startup/send fails with provider-specific
correction. Koan never silently falls back to process-local Transport, because that would change
application reach.

## Options

All options live under `Koan:Communication:RabbitMq`:

- `ConnectionString` — AMQP URI or `auto` (default).
- `Username` / `Password` — credentials added to discovered endpoints without user information.
- `MeshTrustKey` — optional explicit HMAC material; otherwise the authenticated broker credential is
  used. Applications on the same mesh must use the same material.
- `Prefetch` — maximum unacknowledged consumer deliveries; default `32`.
- `PublishTimeout` — confirmed-publication timeout; default `00:00:15`.

Startup facts report RabbitMQ as the elected Transport/framework-signal provider, why it won, its assurance, and the
fact that remote settlement is unobservable. Health `communication.rabbitmq` becomes critical only
when RabbitMQ is elected.

## Deliberate limits

This connector does not currently provide Entity Events, retries, inbox/outbox, deduplication,
dead-letter policy, replay, ordering guarantees beyond RabbitMQ's queue behavior, remote handler
settlement, transactional coupling to Data, schema aliases/migrations, or exactly-once side effects.
The application mesh and CLR contract identities must match across participants.

Framework signals are reserved for Koan modules. Applications do not receive a generic publish or
subscribe API. Jobs wake uses this lane automatically; the signal remains a lossy latency hint and
the Jobs ledger remains the source of truth.

This is the Entity Communication connector. The legacy `Sylin.Koan.Messaging.Connector.RabbitMq`
package has a different arbitrary-message contract and is not used underneath `Entity.Transport`.

See [technical details](TECHNICAL.md) and the
[Communication reference](../../../../docs/reference/communication/index.md).
