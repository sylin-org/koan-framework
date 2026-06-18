---
name: koan-messaging
description: Any object as a message — myMessage.Send() buffered-until-live (AdaptiveMessageProxy), services.On<T>(handler) registration, IMessagingProvider Reference=Intent transport (RabbitMQ), MessagingInterceptors payload substitution, and the pipeline Notify() stage
pillar: messaging
card: docs/reference/cards/messaging.md
status: current
last_validated: 2026-06-18
---

# Koan Messaging

## Trigger this skill when you see

- `myMessage.Send(ct)` / `.Send()` on a plain object (`MessagingExtensions.Send`)
- `services.On<T>(handler)` handler registration, `HandlerRegistry`
- `IMessageProxy` (`IsLive`, `BufferedMessageCount`), `AdaptiveMessageProxy`, "buffered until live"
- `IMessagingProvider` (`Name`, `Priority`, `CanConnect`, `CreateBus`), `IMessageBus`, `IMessageConsumer`
- `MessagingInterceptors.RegisterForType<T>` / `RegisterForInterface<T>`, `TransportEnvelope`
- References to `Koan.Messaging.Core` / `Koan.Messaging.Connector.RabbitMq`, `AddKoanMessaging()`
- `.Pipeline().Notify(...)` (`PipelineMessagingExtensions`)
- "fire and forget", "publish a message", "message bus", "broker", "RabbitMQ", "send without wiring a bus"

## Core principle

**Any object is a message — `.Send()` it, register a handler with `services.On<T>()`, never wire a bus.** `Send()` routes through an `IMessageProxy` that **buffers during startup and goes live once a transport connects** (`AdaptiveMessageProxy`), so it works from the first line of code with no broker yet. Transport is **Reference = Intent**: referencing `Koan.Messaging.Connector.RabbitMq` self-registers an `IMessagingProvider` (priority 100) that the lifecycle auto-selects over the in-memory fallback by probing `CanConnect` in descending `Priority`. No bus wiring in `Program.cs` beyond `AddKoan()`. `Send<T>` dispatches on the payload's **concrete runtime type** (after any registered interceptor), so derived / envelope-substituted types route correctly.

<!-- validate -->
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Koan.Messaging;

// Any plain class is a message — no base type, no [Message] attribute required.
public sealed class UserRegistered
{
    public string UserId { get; init; } = "";
    public string Email { get; init; } = "";
}

public static class MessagingWiring
{
    // Register a handler during startup (ConfigureServices). A consumer is
    // created for it when messaging goes live.
    public static IServiceCollection AddUserHandlers(this IServiceCollection services)
        => services.On<UserRegistered>(async msg =>
        {
            Console.WriteLine($"Welcome {msg.UserId} ({msg.Email})");
            await Task.CompletedTask;
        });

    // Substitute the outbound payload at send time (e.g. wrap in a transport
    // envelope) by concrete type — applied inside Send before dispatch.
    public static void RegisterEnvelope()
        => MessagingInterceptors.RegisterForType<UserRegistered>(m => m);
}

public sealed class Registration
{
    // Fire-and-forget from anywhere: buffered pre-live, routed to the bus once connected.
    public async Task Complete(string userId, string email, CancellationToken ct = default)
        => await new UserRegistered { UserId = userId, Email = email }.Send(ct);
}
```

## Reference = Intent activation

| Add this reference | Effect |
|---|---|
| `Koan.Messaging.Core` | `.Send()` + `services.On<T>()` live; `AdaptiveMessageProxy` buffers until a transport connects. In-memory fallback provider when nothing else elects. |
| `+ Koan.Messaging.Connector.RabbitMq` | Self-registers an `IMessagingProvider` (priority 100) via `IKoanAutoRegistrar`; lifecycle auto-selects it over in-memory when `CanConnect` succeeds. Connection is orchestration-discovered. |

| Surface | What it does |
|---|---|
| `myMessage.Send(ct)` (`MessagingExtensions.Send`) | Fire-and-forget; auto-buffers pre-live, routes to the live `IMessageBus`. Dispatches on the concrete runtime type. |
| `services.On<T>(Func<T,Task>)` (`MessagingExtensions.On`) | Adds a handler to the `HandlerRegistry`; a consumer is created when messaging goes live. |
| `IMessageProxy` (`IsLive`, `BufferedMessageCount`) | The buffer→live seam, resolved from `AppHost.Current` — `Send()` needs no injection. |
| `IMessagingProvider` (`Name`, `Priority`, `CanConnect`, `CreateBus`) | Transport contract; self-registers via `IKoanAutoRegistrar`, auto-selected by descending `Priority`. |
| `MessagingInterceptors.RegisterForType<T>` / `RegisterForInterface<T>` | Substitute the outbound payload at send time, by concrete type or by implemented interface. |

## The buffer → live lifecycle

`Send()` resolves the singleton `IMessageProxy` (`AdaptiveMessageProxy`) from `AppHost.Current`. Until a provider goes live it calls `_buffer.BufferAsync`; on `GoLive(bus)` the lifecycle flushes the buffer to the bus and flips `IsLive`. `MessagingLifecycleService` (an `IHostedService` wired by `AddKoanMessaging()`) iterates `availableProviders.OrderByDescending(p => p.Priority)`, calls `CanConnect`, and `CreateBus()` on the first that answers — then `HandlerRegistry.CreateConsumers(bus)` binds every `On<T>` handler. So a `Send()` issued in startup, before any broker check, is never lost.

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `IBus` / `IMessageBus` injected into a service just to publish | `myMessage.Send()` — the proxy is resolved ambiently; no injection, and it works pre-live. |
| `bus.SendAsync(msg)` / `_bus.PublishAsync(msg)` as the app-facing verb | `msg.Send(ct)` is the canonical surface (`IMessageBus.SendAsync` is the transport-internal sink the proxy calls). |
| `this.On<T>((msg, sp, ct) => …)` / `services.OnMessage<T>(…)` (3-arg) | `services.On<T>(Func<T,Task>)` — the handler takes the message only; resolve dependencies inside it. |
| A hand-rolled `IHostedService` that connects RabbitMQ in `Program.cs` | Reference `Koan.Messaging.Connector.RabbitMq` — Reference = Intent self-registers the provider; the lifecycle elects it. |
| `if (!busReady) queue.Enqueue(msg)` startup buffering by hand | The `AdaptiveMessageProxy` already buffers pre-live and flushes on `GoLive` — just `Send()`. |
| `services.AddSingleton<IMessageProxy>(...)` / manual provider wiring | Don't hand-register the proxy/lifecycle; `AddKoan()` (or `AddKoanMessaging()`) wires the core. |
| Manually wrapping every payload in an envelope at each call site | `MessagingInterceptors.RegisterForType<T>` / `RegisterForInterface<T>` once — substitution happens inside `Send`. |
| Sending inside the pipeline DSL with a try/catch around `Send` | `.Notify(e => new …)` (`PipelineMessagingExtensions`) — faults are recorded on the envelope, not thrown. |

## Escape hatches

- **Pipeline notification**: inside the semantic pipeline DSL, `await someEntities.Pipeline().Notify(e => new UserRegistered { UserId = e.Id })` sends a per-entity message as a stage; a send fault is recorded on the envelope (`envelope.RecordError`), not thrown (`PipelineMessagingExtensions.Notify`).
- **Payload substitution**: `MessagingInterceptors.RegisterForType<T>(m => wrapped)` (exact concrete type) or `RegisterForInterface<T>(...)` (any implementer) rewrites the outbound payload at send time — used to wrap in a `TransportEnvelope`. Dispatch is then on the substituted object's runtime type.
- **Manual core wiring**: `services.AddKoanMessaging()` registers the proxy / buffer / lifecycle by hand — normally the auto-registrar does this for you under `AddKoan()`.
- **Provider config**: the RabbitMQ transport reads `Koan:Messaging:RabbitMQ:ConnectionString` (fallback `Koan:Messaging:ConnectionString`), but orchestration discovery resolves the broker automatically when omitted — no config needed for the local/container path.
- **Proxy introspection**: resolve `IMessageProxy` to read `IsLive` / `BufferedMessageCount` for health/diagnostics (e.g. assert the buffer drained after startup).

## See also

- [Reference card: messaging.md](../../../docs/reference/cards/messaging.md) — one-screen pillar map
- [Messaging pillar reference](../../../docs/reference/messaging/index.md) — full detail (message types, routing, config)
- [`samples/S3.Mq.Sample`](../../../samples/S3.Mq.Sample/README.md) — minimal end-to-end over RabbitMQ: `AddKoan()` self-wires the connector, then `new UserRegistered { … }.Send()` fires over the live bus
- [koan-jobs](../koan-jobs/SKILL.md) — `Koan.Jobs.Transport.Messaging` rides this bus for cross-node push-dispatch
- [koan-caching](../koan-caching/SKILL.md) — `Koan.Cache.Coherence.Messaging` rides this bus for cross-node invalidation
