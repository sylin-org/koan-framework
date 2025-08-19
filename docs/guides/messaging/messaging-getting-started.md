# Messaging in Sora — Getting Started

This guide shows how to use Sora.Mq from the simplest setup to more advanced scenarios, with capability-aware behavior.

## 1) Minimal dev setup (auto-discovery)

- Add the RabbitMQ package to your project (NuGet: `Sylin.Sora.Messaging.RabbitMq`).
- In dev, `services.AddSora()` discovers RabbitMQ automatically if referenced.

```csharp
services.AddSora(); // discovery picks RabbitMQ (if referenced)
```

Send and handle messages:

```csharp
await new ProductCreated { Id = "p-1", Name = "Widget" }.Send(ct);

// Register a handler in DI
services.AddSingleton<IMessageHandler<ProductCreated>, ProductCreatedHandler>();

public sealed class ProductCreatedHandler : IMessageHandler<ProductCreated>
{
  public Task HandleAsync(MessageEnvelope env, ProductCreated msg, CancellationToken ct)
  {
    Console.WriteLine($"Product: {msg.Id} - {msg.Name}");
    return Task.CompletedTask;
  }
}
```

Or use the delegate sugar:

```csharp
services.OnMessage<ProductCreated>((env, msg) =>
{
  Console.WriteLine($"Product: {msg.Id} - {msg.Name}");
});

// Chain multiple handlers
services
  .OnMessage<ProductCreated>((env, msg) => Console.WriteLine($"Product: {msg.Id} - {msg.Name}"))
  .OnMessage<UserRegistered>((env, msg) => Console.WriteLine($"Welcome {msg.UserId}"));

// Or use a small builder for semantic grouping
services.OnMessages(h =>
{
  h.On<ProductCreated>((env, msg) => Console.WriteLine($"Product: {msg.Id} - {msg.Name}"));
  h.On<UserRegistered>((env, msg) => Console.WriteLine($"Welcome {msg.UserId}"));
});
```

## 2) Config-driven bus (explicit)

appsettings.json
```json
{
  "Sora": {
    "Messaging": {
      "DefaultBus": "rabbit",
      "Buses": {
        "rabbit": {
          "Provider": "RabbitMq",
          "ConnectionStringName": "rabbitmq",
          "RabbitMq": { "Exchange": "sora", "ExchangeType": "topic", "PublisherConfirms": true },
          "Prefetch": 50,
          "Retry": { "MaxAttempts": 5, "Backoff": "exponential", "FirstDelaySeconds": 3 }
        }
      }
    }
  },
  "ConnectionStrings": { "rabbitmq": "amqp://guest:guest@localhost:5672" }
}
```

Program.cs
```csharp
services.AddSora(); // discovery or options binding (explicit wins)
```

RabbitMQ options (under Sora:Messaging:Buses:<code>):

- Provider: RabbitMq
- ConnectionString or ConnectionStringName
- RabbitMq: { Exchange, ExchangeType, PublisherConfirms }
- Prefetch: per-consumer prefetch (int)
- Retry: { MaxAttempts, Backoff, FirstDelaySeconds, MaxDelaySeconds }
- Dlq: { Enabled }
 - ProvisionOnStart: when true, declares queues/bindings on startup
 - Subscriptions: [ { Name, Queue?, RoutingKeys[], Dlq? } ]
 - If Subscriptions are omitted and provisioning is enabled, Sora auto-creates one subscription using DefaultGroup (see below) bound to all ("#").

Provisioning defaults:
- If ProvisionOnStart is not set, it defaults to true except when DOTNET/ASPNETCORE environment is Production.
- In Production, provisioning is disabled by default unless Sora:AllowMagicInProduction=true.


## 3) Attributes for message types

```csharp
[Message(Alias = "product.created", Version = 1, Bus = "rabbit", Group = "workers")]
public sealed class ProductCreated
{
    [PartitionKey] public string Id { get; init; } = default!;
    [Header("x-tenant")] public string Tenant { get; init; } = "default";
    [Sensitive] public string? InternalNote { get; init; }
}

Headers and partitioning

- [Header(name)] on a public property promotes its value to transport headers when publishing. Use for correlation or downstream filters.
- [PartitionKey] marks a property used to influence message ordering/affinity when the provider supports it. In RabbitMQ we suffix the routing key with a stable shard (".pN") derived from a hash of that value (16 shards by default). This helps consumers bind by alias or alias prefix, while maintaining per-partition ordering.
- [Sensitive] continues to mark properties that should be redacted in logs/diagnostics. If a property is both [Header] and [Sensitive], Sora will not promote it to headers.
```

- Alias drives routing; Group sets the default consumer group.
- PartitionKey influences routing/partition selection.
- Header promotes the property to transport headers; Sensitive redacts logs.

## 4) Delayed delivery (capability-aware)

```csharp
public sealed class SendWelcomeEmail { public string UserId { get; init; } = default!; [DelaySeconds] public int Delay { get; init; } = 300; }
await new SendWelcomeEmail { UserId = "u-1", Delay = 300 }.Send();
```

Outcomes:
- If the provider supports native delayed delivery, it is used.
- Else if TTL+DLX is available, it’s applied with coarse granularity.
- Else delay is ignored; a warning is logged once per route.

## 5) Batch send and named buses

```csharp
await events.Send(); // IEnumerable<object>
await events.SendTo("audit");
```

Notes:

- Works with `IEnumerable<object>` and `List<object>` alike (both have `Send()`/`SendTo()` overloads).
- Each item is published as an individual message; there is no special batch handler implied by sending a list.

Grouped/batch processing:

```csharp
await users.SendAsBatch(); // sends one grouped Batch<User>
services.OnBatch<User>((env, batch, ct) => Save(batch.Items));
// Optional alias: services.OnMessages<User>((env, items, ct) => Save(items));
```

Aliases for grouped messages:

- Batch<T> aliases as `batch:{alias(T)}` where `alias(T)` comes from `[Message(Alias)]` or the full type name.
- Example: `batch:MyApp.Events.UserRegistered` (or with version if applied to T).


Versioned aliases (optional)

- By default, aliases omit version (e.g., `MyApp.Events.UserRegistered`).
- You can opt into suffixing the version from `[Message(Version = n)]` by setting `Sora:Messaging:IncludeVersionInAlias = true`. When enabled, the alias becomes `MyApp.Events.UserRegistered@v1`. The batch form follows the same rule: `batch:MyApp.Events.UserRegistered@v1`.
- Resolution is tolerant: both versioned and unversioned forms resolve to the same type when the registry knows the version.

## 6) Reliability: Outbox and Inbox

- Producer: Add the Outbox publisher to publish repository outbox entries to the bus with idempotency.
- Consumer: Enable InboxStore to de-dup deliveries.

Enable Inbox (dev/test):

- When the `Sora.Messaging.Inbox.InMemory` package is referenced, AddSora() auto-registers the in-memory inbox via discovery.

Notes:
- Publisher promotes `[IdempotencyKey]` to header `x-idempotency-key` automatically.
- Consumer uses `x-idempotency-key` when present, otherwise falls back to the broker `MessageId`.
- In-memory inbox is single-process only; use a durable store for production (planned adapters).

Configuration keys
- Sora:Messaging:Inbox:Endpoint — explicit external inbox client endpoint; when set, discovery is skipped.
- Sora:Messaging:Discovery:Enabled — true/false to force-enable or disable discovery regardless of environment defaults.
- Sora:AllowMagicInProduction — true to allow discovery and other dev conveniences in Production (use with caution).

Discovery tuning (optional)
- Sora:Messaging:Discovery:TimeoutSeconds — total wait time for discovery (default 3s).
- Sora:Messaging:Discovery:SelectionWaitMs — additional wait after first response to collect candidates (default 150ms).
- Sora:Messaging:Discovery:CacheMinutes — duration to cache a discovered endpoint (default 5 minutes).

HTTP inbox client
- Add package reference to `Sylin.Sora.Messaging.Inbox.Http`.
- If `Sora:Messaging:Inbox:Endpoint` is configured, `AddSora()` auto-registers the HTTP inbox client and overrides any dev/test in-memory inbox.

## 7) Health and diagnostics

- Health contributor reports broker connectivity and topology status.
- Effective plan: inspect Sora diagnostics to see how delay/DLQ were applied.

```csharp
var mqDiag = sp.GetRequiredService<IMessagingDiagnostics>();
var plan = mqDiag.GetEffectivePlan("rabbit");
```

Consumer concurrency:

- Set `Subscriptions[i].Concurrency` to configure the number of parallel consumers per queue (default 1).

## 8) Advanced

- Multiple buses with different providers (e.g., RabbitMq + Azure Service Bus).
- Per-subscription overrides for retry/backoff and concurrency.
- Custom serializer and type alias registry.

## 9) Sample: Docker compose + RabbitMQ

- See `samples/S3.Mq.Sample` for a tiny end-to-end example (publisher, dispatcher/handler, and compose stack).
- Start broker only:
  - docker compose -f samples/S3.Mq.Sample/compose/docker-compose.yml up -d rabbitmq
- Run sample locally:
  - dotnet run --project samples/S3.Mq.Sample/S3.Mq.Sample.csproj
- Or run both:
  - docker compose -f samples/S3.Mq.Sample/compose/docker-compose.yml up -d

VS Code tasks (optional):

- S3: Up RabbitMQ + Sample — brings up the compose stack in detached mode.
- S3: Down RabbitMQ + Sample — tears down the compose stack.


Alias and group defaults:

- Alias: by default, the full type name (Namespace.TypeName) is used, whether or not [Message] is present (unless you set Alias explicitly).
- Group: defaults to "workers" via Sora:Messaging:DefaultGroup or bus-level override; used for auto-subscription when Subscriptions are omitted.

