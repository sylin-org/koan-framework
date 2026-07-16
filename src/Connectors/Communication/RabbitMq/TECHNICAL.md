# Koan Communication RabbitMQ technical notes

## Provider boundary

The connector implements `ICommunicationAdapter` and declares only `CommunicationLane.Transport`.
Its descriptor advertises the invariants required by the default Entity Transport route: stable
contract identity, serialized snapshot copies, opaque context carriage, typed receiver groups,
group fan-out, message identity, and bounded acceptance. It reports
`CommunicationDeliveryAssurance.DurablyAcknowledged`.

`Koan.Communication` owns the wire envelope, typed handler catalog, context encoding, validation, and
dispatch. RabbitMQ sees host-produced bytes and routing identities; it does not know Entity types,
tenant semantics, or business handlers. The same host-owned wire is exercised by the built-in local
provider.

Provider election is lane-specific. An explicit `CommunicationOptions.TransportProvider` binding
wins first. Otherwise a direct package/project reference makes this connector eligible. Without
direct intent, only the minimum-priority built-in provider participates. Capability requirements,
assurance, Core-owned `[ProviderPriority]`, and stable provider ID resolve eligible candidates.
Direct or explicit external intent never falls back locally when unavailable.

## Topology

The application identity code is the mesh identity. The connector declares one durable direct
exchange per mesh and Transport channel:

```text
koan.communication.{mesh}.transport.default.v1
```

Every discovered `IReceiveEntity<TEntity>` group gets one durable, non-exclusive queue. Queue and
routing-key suffixes are deterministic SHA-256-derived identifiers over the stable group and
contract identities. All replicas with the same mesh, contract, and group consume from the same
queue; distinct groups bind separate queues to the same contract route and therefore fan out.

The publisher uses a long-lived confirm-enabled channel, persistent messages, and mandatory
publication. A returned message becomes `TransportException.NoReceivers`; a negative/late confirm or
closed provider becomes `TransportException.Unavailable`. Publication is serialized through a
bounded semaphore and limited by `PublishTimeout`.

The consumer uses one long-lived channel, configurable prefetch, asynchronous delivery, and manual
acknowledgement. Delivered and filtered outcomes are acknowledged. Invalid authentication, invalid
wire state, and handler failure are rejected without requeue because retry/dead-letter policy is not
part of this version's contract.

Publisher confirmation is the only remote acceptance observable by the sender. Consumer settlement
is intentionally absent from the receipt rather than guessed from local topology.

## Context trust

Core context carriers produce an opaque context object inside the Communication wire envelope. The
connector signs the exact host-owned body with HMAC-SHA256. It derives per-mesh signing material from
`MeshTrustKey`, or from the authenticated broker credential when no explicit key is set. Consumers
compare signatures in constant time before supplying `ContextIngressTrust.Authenticated` to the host.

This authenticates integrity and shared-mesh provenance; it does not encrypt the payload. Broker TLS,
network policy, credentials, and vhost isolation remain operator responsibilities. Participants in
one mesh must share both application code and signing material.

## Lifecycle, discovery, and health

The elected adapter opens one auto-recovering RabbitMQ connection plus publisher and consumer
channels during host start, declares topology, and binds all local receiver groups before reporting
ready. Graceful host stop closes consumer, publisher, and connection in that order.

Discovery considers explicit Koan configuration, legacy RabbitMQ connection keys for transition,
standard connection strings, environment URLs, Aspire endpoints, and Koan orchestration. Candidate
health is proven with a real AMQP connection. Direct reference intent enables orchestration; explicit
endpoint configuration also enables it.

`communication.rabbitmq` is healthy and non-critical while the connector is merely available. Once
elected, it is critical and healthy only while the connection and both channels are open. Diagnostic
errors are de-identified and never include Entity payloads or credentials.

## Wire compatibility and non-claims

The current wire schema is version `1`. Contract identity is derived from CLR full names; there is no
alias registry or rolling schema negotiation yet. Mesh, lane, channel, contract, operation identity,
source ordinal, and Entity payload are validated at host ingress before business dispatch.

No claim is made for retries, deduplication, inbox/outbox, dead letters, replay, cross-version
contracts, cross-mesh routing, Events, transactional publication, handler settlement, exactly-once
effects, or transparent bounded-context integration.
