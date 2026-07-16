---
type: REF
domain: messaging
title: "Messaging — Current Legacy Surface"
audience: [developers, architects, ai-agents]
status: deprecated
last_updated: 2026-07-15
framework_version: v0.18.0
validation:
  date_last_tested: 2026-07-15
  status: reviewed
  scope: truthful v0.17 Messaging implementation and R07 replacement boundary
---

# Messaging — current legacy surface

Koan v0.17 contains an experimental Messaging implementation. It demonstrates automatic provider
discovery and real InMemory/RabbitMQ message movement, but it does **not** define a stable delivery or
application contract. Do not base new application architecture on this surface.

[ARCH-0113](../../decisions/ARCH-0113-entity-capability-communication.md) replaces it with the
Communication ring. Its first supported slice now ships as
[process-local Entity Transport](../communication/index.md):

```csharp
await order.Transport.Send(ct);
```

The shipped local path provides `AddKoan()` composition, scalar/set/stream Entity semantics, typed
receiver filters, immutable copies, context isolation, bounded acceptance, local correlated settlement,
and boot-time explanation. Events, connector manifests/election, brokers, retries, and RabbitMQ parity
remain unimplemented and must not be inferred from local Transport.

## What exists in v0.17

The current implementation exposes two terse operations:

```csharp
services.On<UserRegistered>(message =>
{
    Console.WriteLine(message.UserId);
    return Task.CompletedTask;
});

await new UserRegistered("u-1").Send(ct);
```

- `Send<T>(this T) where T : class` applies to every reference type.
- `services.On<T>(Func<T, Task>)` records one handler for a CLR type; a second handler for that type is
  skipped.
- `IMessageProxy` buffers raw objects until one provider becomes live, then forwards them.
- Provider election is a single priority/`CanConnect` choice.
- The InMemory connector uses process-local channels.
- The RabbitMQ connector serializes JSON and uses one queue derived from the CLR type.
- [`S3.Mq.Sample`](../../../samples/S3.Mq.Sample/README.md) demonstrates the RabbitMQ path.

This surface requires an active Koan host. Its package/namespace spelling, startup lifecycle, provider
selection, queue naming, and handler registry are implementation details scheduled for replacement.

## Important limitations

The current providers do not implement equivalent semantics:

| Concern | InMemory | RabbitMQ |
|---|---|---|
| Object copy | sends the same mutable reference | serializes/deserializes a copy |
| Multiple consumers | process-local fan-out | consumers on one type queue compete |
| Handler cardinality | provider can fan out, but Core retains one handler per CLR type | one retained handler/type per host |
| Durable acceptance | no | not certified as a Koan guarantee |
| Context/tenant carriage | no shared Messaging contract | no shared Messaging contract |
| Retry idempotence/inbox | no supported contract | no supported contract |
| Ordering/dead letter/outbox | no supported contract | no supported Koan contract |
| Unified startup facts | incomplete | incomplete |

Consequently, current Messaging does not support a claim of:

- local/broker semantic parity;
- every-subscriber Event fan-out;
- stable Transport receiver groups;
- tenant-safe routing;
- exactly-once effects;
- durable outbox/inbox;
- provider-neutral retries, ordering, replay, dead-letter policy, or batch messaging; or
- attributes, aliases, `SendTo`, routing builders, or topology APIs described by older documentation.

## Current safe use

Treat v0.17 Messaging as a demonstrated experimental adapter path for repository samples and internal
framework signals. If you use it temporarily:

- keep message contracts simple and version them yourself;
- assume provider-specific cardinality;
- make handlers idempotent;
- do not depend on startup buffering as durability;
- do not infer tenant or authorization boundaries; and
- inspect the selected provider and test the exact topology you deploy.

For durable work, prefer [Koan Jobs](../cards/jobs.md), whose ledger is the work truth. For new Entity
communication work, use [Communication](../communication/index.md) rather than expanding this legacy
Messaging generation.

## Replacement path

R07 proceeds in this order:

1. Core-owned ambient context, truthful Lifecycle/streaming, and minimal Entity cardinality are
   complete;
2. process-local Entity Transport under `AddKoan()` is complete;
3. Event occurrence semantics follow on the same proven boundary;
4. zero-application-routing-code connector mesh/channel behavior follows local semantics; and
5. RabbitMQ is rebuilt only against that conformance kit.

No public maturity or package support claim follows from this target until those executable gates pass.
