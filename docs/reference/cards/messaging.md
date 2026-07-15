---
type: REF
domain: messaging
title: "Messaging — legacy implementation map"
audience: [developers, ai-agents]
status: deprecated
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: reviewed
  scope: current implementation truth and replacement direction
---

# Messaging — legacy implementation map

> Messaging is a demonstrated experimental surface, not a stable Koan contract. See the
> [current limitations](../messaging/index.md) and the accepted
> [Communication rebuild](../../decisions/ARCH-0113-entity-capability-communication.md).

## Current v0.17 shape

```csharp
services.On<UserRegistered>(Handle);
await new UserRegistered("u-1").Send(ct);
```

| Surface | Current behavior |
|---|---|
| `Send<T>(this T) where T : class` | Intercepts the object, resolves the current host proxy, then buffers or forwards it. |
| `services.On<T>(...)` | Retains at most one handler per CLR type. |
| `IMessageProxy` | Buffers during startup and forwards through one elected provider. It is not durable storage. |
| InMemory provider | Fans out the same mutable object reference inside one process. |
| RabbitMQ provider | Serializes JSON into one CLR-type-derived queue whose consumers compete. |

Those providers do not share copy, cardinality, context, idempotency, durability, or topology
guarantees. Older attribute, batch, routing, inbox/outbox, and dead-letter examples are not shipped
APIs.

## Target replacement

R07 deletes this broad arbitrary-object grammar and introduces:

```csharp
await order.Events.Raise<OrderApproved>(ct);
await order.Transport.Send(ct);
```

The foundation plus `AddKoan()` supplies a complete local ring. A build manifest later turns direct
connector references into deterministic, per-channel, boot-reported election without application
routing code. Awaits return bounded publication acceptance; later receiver settlement remains
correlated and inspectable. This target remains specified—not implemented—until its conformance slices
pass.
