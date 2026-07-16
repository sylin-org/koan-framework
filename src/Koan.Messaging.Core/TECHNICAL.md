---
uid: reference.modules.Koan.messaging.core
title: Koan.Messaging.Core - Legacy Technical Boundary
description: Truthful boundary for the v0.17 experimental Messaging implementation.
since: 0.2.x
packages: [Sylin.Koan.Messaging.Core]
source: src/Koan.Messaging.Core/
---

## Status

This is a legacy experimental implementation being replaced under
[ARCH-0113](../../docs/decisions/ARCH-0113-entity-capability-communication.md). Process-local Entity
Transport now ships separately in `Koan.Communication`; this package remains only for unmigrated legacy
and internal bridge consumers. It is code evidence, not a stable delivery contract.

## Current mechanism

1. `Send<T>(this T) where T : class` applies a process-static interceptor, resolves the current host's
   `IMessageProxy`, and submits the resulting object.
2. `AdaptiveMessageProxy` buffers raw objects in memory before one provider becomes live, then forwards
   them to that provider's `IMessageBus`.
3. `services.On<T>(...)` writes one handler per CLR type into the current `HandlerRegistry`.
4. Provider election orders `IMessagingProvider` instances by priority and chooses a connectable one.
5. The InMemory provider uses process-local channels and object references. RabbitMQ serializes JSON
   into a queue derived from the CLR type.

## Unsupported claims

The current package does not establish provider-neutral:

- publish/subscribe versus competing receiver-group cardinality;
- immutable-copy behavior;
- durable acceptance or delivery;
- retry idempotence, inbox/outbox, or exactly-once effects;
- ordering, replay, dead-letter, partition, batch, or schema-evolution policy;
- tenant/context carriage or authenticated provenance; or
- complete startup, settlement, health, and metric facts.

Do not infer those guarantees from connector names, historical MESS decisions, or older examples.

## Replacement boundary

R07 moves ambient context beneath Data, makes Data Lifecycle/streaming truthful, and then proves Entity
`Transport` and `Events` first through a faithful built-in in-process adapter. External connectors are
rebuilt only against the same conformance kit. See the
[current Messaging reference](../../docs/reference/messaging/index.md).
