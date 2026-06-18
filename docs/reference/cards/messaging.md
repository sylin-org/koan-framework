---
type: REF
domain: messaging
title: "Messaging — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-06-18
framework_version: v0.17.0
validation:
  date_last_tested: 2026-06-18
  status: verified
  scope: docs/reference/cards/messaging.md
---

# Messaging — pillar map

> One-screen map of the Messaging pillar — fire-and-forget `.Send()` with zero ceremony, buffered until the transport is live. Full detail: [messaging/index.md](../messaging/index.md).

**What it does** — Any object becomes a message: `await myMessage.Send()` routes it through an `IMessageProxy` that **buffers during startup and goes live once a transport connects** (`AdaptiveMessageProxy`), so `Send()` works from the first line of code without a wired bus. Handlers register with `services.On<T>(handler)`; consumers are created when the lifecycle goes live. Transport is **Reference = Intent** — referencing `Koan.Messaging.Connector.RabbitMq` self-registers an `IMessagingProvider` (priority 100) that the lifecycle auto-selects over the in-memory fallback by probing `CanConnect`. Connection discovery is orchestration-aware; no bus wiring in `Program.cs` beyond `AddKoan()`.

## The one canonical pattern

Any class is a message. `.Send()` it; register a handler with `services.On<T>()`. The proxy buffers until a provider is live, then flushes.

```csharp
public sealed class UserRegistered { public string UserId { get; init; } = ""; public string Email { get; init; } = ""; }

// Register a handler (during startup / ConfigureServices)
services.On<UserRegistered>(async msg =>
{
    Console.WriteLine($"Welcome {msg.UserId} ({msg.Email})");
});

// Send from anywhere — buffered pre-live, routed to the transport once connected
await new UserRegistered { UserId = "u-1", Email = "u1@example.com" }.Send();
```

`Send<T>` dispatches on the payload's concrete runtime type (after any registered interceptor), so derived/substituted types route correctly.

## ≤5 surfaces you'll use

| Surface | What it does |
|---|---|
| `myMessage.Send(ct)` (`MessagingExtensions.Send`) | Fire-and-forget send; auto-buffers pre-live, then routes to the live `IMessageBus`. |
| `services.On<T>(Func<T,Task>)` (`MessagingExtensions.On`) | Registers a handler into the `HandlerRegistry`; a consumer is created when messaging goes live. |
| `IMessageProxy` (`IsLive`, `BufferedMessageCount`) | The buffer→live seam; resolved from `AppHost.Current`, so `Send()` needs no injection. |
| `IMessagingProvider` (`Name`, `Priority`, `CanConnect`, `CreateBus`) | Transport contract; self-registers via `IKoanAutoRegistrar`, auto-selected by descending priority. |
| `MessagingInterceptors.RegisterForType<T>` / `RegisterForInterface<T>` | Substitutes the outbound payload at send time (e.g. wrap in a transport envelope) by type or interface. |

## The escape hatch

Inside the semantic pipeline DSL, `Notify(...)` sends a per-entity message as a pipeline stage (faults are recorded on the envelope, not thrown):

```csharp
await someEntities
    .Pipeline()
    .Notify(e => new UserRegistered { UserId = e.Id });   // PipelineMessagingExtensions.Notify
```

Lower level: `AddKoanMessaging()` registers the core proxy/buffer/lifecycle by hand (normally done for you by the auto-registrar). The RabbitMQ transport reads `Koan:Messaging:RabbitMQ:ConnectionString` (fallback `Koan:Messaging:ConnectionString`), but discovery resolves the broker automatically when omitted.

## The sample that shows it

[`samples/S3.Mq.Sample`](../../../samples/S3.Mq.Sample/README.md) — a minimal end-to-end Koan Messaging app over RabbitMQ: `AddKoan()` self-wires the connector, then `new Hello { Name = "Koan" }.Send()` and `new UserRegistered { ... }.Send()` fire over the live bus.
