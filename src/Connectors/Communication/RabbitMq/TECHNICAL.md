# Koan Communication RabbitMQ technical notes

## Provider boundary

The connector implements `ICommunicationAdapter` and declares `CommunicationLane.Transport`,
`CommunicationLane.FrameworkSignals`, and `CommunicationLane.FrameworkBroadcasts`. Its descriptor advertises the invariants required by these routes: stable
contract identity, serialized snapshot copies, opaque context carriage, typed receiver groups,
group fan-out, node fan-out, message identity, and bounded acceptance. It reports
`CommunicationDeliveryAssurance.DurablyAcknowledged`.

`Koan.Communication` owns the wire envelope, typed handler catalog, context encoding, validation, and
dispatch. RabbitMQ sees host-produced bytes and routing identities; it does not know Entity types,
tenant semantics, or business handlers. The same host-owned wire is exercised by the built-in local
provider.

Provider election is route-specific. An explicit default `CommunicationOptions.TransportProvider`,
named-channel `TransportProvider`, `FrameworkSignalsProvider`, or `FrameworkBroadcastsProvider` binding
wins first. Otherwise a direct package/project reference makes this connector eligible. Without
direct intent, only the minimum-priority built-in provider participates. Capability requirements,
assurance, Core-owned `[ProviderPriority]`, and stable provider ID resolve eligible candidates.
Direct or explicit external intent never falls back locally when unavailable.

## Topology

The application identity code is the mesh identity. The connector declares one durable direct
exchange per mesh for the channel-aware topology generation:

```text
koan.communication.{mesh}.v4
koan.communication.{mesh}.v4.dead-letter
```

Every discovered Entity receiver or competing internal signal group gets one durable quorum queue.
Queue and routing-key suffixes are deterministic SHA-256-derived identifiers over the logical channel,
stable group, and contract identities. All replicas with the same mesh, channel, contract, and group
consume from the same queue; distinct groups bind separate queues to the same contract route and
therefore fan out. Every-node bindings use unique non-durable, exclusive, auto-delete classic queues, so
each active host gets a copy and departed nodes leave no durable subscription behind.

Each group queue declares `x-delivery-limit` 5, the mesh's dead-letter exchange, its own name as the
dead-letter routing key, `at-least-once` dead-lettering, and `reject-publish` overflow (which
at-least-once dead-lettering requires). Its dead-letter queue, `{group queue}.dead-letter`, is a durable
quorum queue bound to the dead-letter exchange by that routing key, so each group's dead letters stay
apart. Koan never consumes or expires them; operators inspect, replay, or purge them.

The publisher uses one long-lived channel with publisher confirmation tracking, persistent messages,
and mandatory publication. Publications run concurrently on it: the client serializes only frame
writes and each publication awaits its own confirm. The adapter's confirm window holds a permit around
the whole awaited publish and releases it on every outcome, so up to 128 publications await their
confirms at once with no delay, a 129th waits, oldest first, until a confirm settles, and a slow broker
cannot make the publisher hold more. The wait for a permit counts against `PublishTimeout`. The
client's own outstanding-confirmation limiter is off: it releases its lease once the frame is written,
before the confirm is awaited, so it bounds only the write. A returned message becomes `TransportException.NoReceivers`; a
negative/late confirm or closed provider becomes `TransportException.Unavailable`; each publication is
limited by `PublishTimeout`. The adapter declares `MaxMessageBytes` as its transport limit, so a larger
encoded publication fails with `PayloadTooLarge` before it reaches the broker, which would otherwise
close the publishing channel.

Every receiver group consumes on its own channel, with that group's prefetch and a dispatch concurrency
of one, so a slow or hung group never delays another group's deliveries. Ingress runs the handler under
Koan's `HandlerTimeout`. Delivered and filtered outcomes are acknowledged. A failed delivery is rejected
with requeue, which the quorum queue counts; past the delivery limit RabbitMQ dead-letters it. A
delivery interrupted by host stop is negatively acknowledged with requeue, which the quorum queue does
not count. An envelope that fails authentication is rejected without requeue and dead-lettered at
once. A failed node broadcast is rejected without requeue and dropped.

Publisher confirmation is the only remote acceptance observable by the sender. Consumer settlement
is intentionally absent from the receipt rather than guessed from local topology.

The v4 exchange does not consume earlier generations' topology. RabbitMQ does not delete old durable
exchanges or queues for Koan; an operator retires them after a coordinated upgrade if the broker is
retained.

## Context trust

Core context carriers produce an opaque context object inside the Communication wire envelope. The
connector signs the exact host-owned body with HMAC-SHA256. It derives per-mesh signing material from
`MeshTrustKey`, or from the authenticated broker credential when no explicit key is set. Consumers
compare signatures in constant time before supplying `ContextIngressTrust.Authenticated` to the host.

This authenticates integrity and shared-mesh provenance; it does not encrypt the payload. Broker TLS,
network policy, credentials, and vhost isolation remain operator responsibilities. Participants in
one mesh must share both application code and signing material.

## Lifecycle, discovery, and health

The elected adapter opens one auto-recovering RabbitMQ connection, one publisher channel, and one
channel per local receiver group during host start, declares topology, and binds all local receiver
groups before reporting ready. Host stop cancels running handlers, then closes the group channels, the
publisher, and the connection in that order.

Discovery considers the standard `ConnectionStrings:RabbitMq` endpoint, `RABBITMQ_URL`, Aspire endpoints, and
environment-appropriate local/container candidates. Candidate health is proven with a real AMQP connection. The
application or its standard deployment tool owns broker lifecycle.

`communication.rabbitmq` is healthy and non-critical while the connector is merely available. Once
elected, it is critical and healthy only while the connection and every channel are open. Diagnostic
errors are de-identified and never include Entity payloads or credentials.

## Wire compatibility and non-claims

The current wire schema is version `2`; its neutral payload field carries either an Entity snapshot or
a typed framework signal or broadcast. Entity contract identity is derived from CLR full names; there is no
alias registry or rolling schema negotiation yet. Mesh, lane, channel, contract, operation identity,
source ordinal, and Entity payload are validated at host ingress before business dispatch.

No claim is made for deduplication, inbox/outbox, replay, cross-version contracts, cross-mesh
routing, Events, transactional publication, handler settlement, exactly-once effects, or transparent
bounded-context integration. Redelivery makes handler execution at-least-once and can reorder a
redelivered snapshot behind later ones.
