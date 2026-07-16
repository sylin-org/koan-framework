---
type: REF
domain: messaging
title: "Messaging — legacy implementation map"
audience: [developers, ai-agents]
status: deprecated
last_updated: 2026-07-15
framework_version: v0.18.0
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

## Current replacement status

R07 deletes this broad arbitrary-object grammar and introduces:

```csharp
await order.Transport.Send(ct);
```

The foundation plus `AddKoan()` now supplies the tested process-local Transport half of the ring:
scalar/set/stream Entity snapshots, typed receiver groups and filters, isolated copies, opaque context
carriage, bounded publication acceptance, local settlement, and boot facts. Use the current
[Communication reference](../communication/index.md).

Events, build-manifest connector intent, per-channel election, retries, and RabbitMQ conformance remain
specified but unimplemented. They are not implied by the shipped local Transport path.
