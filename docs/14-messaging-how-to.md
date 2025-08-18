# Messaging How‑To: from zero to production

Audience: developers new to Sora messaging. This guide grows from the tiniest “hello message” to pragmatic production patterns, with short, runnable examples.

Prereqs
- You reference the RabbitMQ provider (NuGet: Sylin.Sora.Messaging.RabbitMq). In dev, `services.AddSora()` auto-discovers it.
- A RabbitMQ broker (Docker is fine). See the S3 sample for a compose file.

What you’ll learn
- Send and handle messages (delegates or DI handlers)
- Configure a bus and subscriptions
- Partitioning, headers, and sensitive data
- Batches and grouped processing
- Retries, DLQ, and publisher confirms
- Health and diagnostics
- Testing with Testcontainers

## 1) The tiniest possible message flow

Program setup
```csharp
services.AddSora(); // discovery finds RabbitMQ when referenced

// Handle ProductCreated using a delegate
services.OnMessage<ProductCreated>((env, msg) =>
{
    Console.WriteLine($"Product: {msg.Id} - {msg.Name}");
});

await new ProductCreated { Id = "p-1", Name = "Widget" }.Send();

public sealed class ProductCreated { public string Id { get; init; } = string.Empty; public string Name { get; init; } = string.Empty; }
```

Concepts
- Alias: by default, the full type name (Namespace.Type) is used as the topic/alias unless you set `[Message(Alias = ...)]`.
- Group: consumers default to the `DefaultGroup` (workers) when the provider auto-subscribes.

## 2) Using a DI handler instead of a delegate

```csharp
services.AddSingleton<IMessageHandler<UserRegistered>, UserRegisteredHandler>();

public sealed class UserRegistered { public string UserId { get; init; } = string.Empty; public string Email { get; init; } = string.Empty; }

public sealed class UserRegisteredHandler : IMessageHandler<UserRegistered>
{
    public Task HandleAsync(MessageEnvelope env, UserRegistered msg, CancellationToken ct)
    {
        Console.WriteLine($"Welcome {msg.UserId} ({msg.Email})");
        return Task.CompletedTask;
    }
}
```

Tip: You can chain delegate registrations:
```csharp
services
  .OnMessage<ProductCreated>((e,m) => Console.WriteLine(m.Name))
  .OnMessage<UserRegistered>((e,m) => Console.WriteLine(m.UserId));
```

## 3) Configure the bus explicitly (appsettings)

appsettings.json (excerpt)
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
          "ProvisionOnStart": true,
          "Dlq": { "Enabled": true },
          "Subscriptions": [ { "Name": "workers", "RoutingKeys": ["#"] } ]
        }
      }
    }
  },
  "ConnectionStrings": { "rabbitmq": "amqp://guest:guest@localhost:5672" }
}
```

Notes
- If Subscriptions are omitted, Sora can auto-create one using DefaultGroup (workers) and bind to all (“#”) when provisioning is enabled.
- In Production, provisioning defaults to off unless `Sora:AllowMagicInProduction=true`.

## 4) Builder style for grouped registration

```csharp
services.OnMessages(h =>
{
  h.On<ProductCreated>((e,m) => Console.WriteLine(m.Name));
  h.On<UserRegistered>((e,m) => Console.WriteLine(m.UserId));
});
```

## 5) Sending many vs. grouped batches

Stream many items (individual messages)
```csharp
IEnumerable<object> events = new object[]
{
  new ProductCreated { Id = "p-2", Name = "Phone" },
  new UserRegistered { UserId = "u-1", Email = "u1@example.com" }
};
await events.Send(); // each item is its own message
```

Grouped processing (Batch<T>)
```csharp
var users = new[] { new UserRegistered{ UserId="u-2", Email="u2@example" }, new UserRegistered{ UserId="u-3", Email="u3@example" } };
await users.SendAsBatch(); // sends one Batch<UserRegistered>

services.OnBatch<UserRegistered>((env, batch, ct) =>
{
  Console.WriteLine($"Saving {batch.Items.Count} users...");
  return Task.CompletedTask;
});
```

Aliases for batches
- `Batch<T>` uses `batch:{alias(T)}` (e.g., `batch:MyApp.Events.UserRegistered`).
- Optional version suffix: set `Sora:Messaging:IncludeVersionInAlias=true` to emit `@vN` (e.g., `batch:UserRegistered@v1`).

## 6) Attributes: alias, partitioning, and headers

```csharp
[Message(Alias = "product.created", Version = 1, Bus = "rabbit", Group = "workers")]
public sealed class ProductCreated
{
  [PartitionKey] public string Id { get; init; } = string.Empty; // influences ordering/affinity
  [Header("x-tenant")] public string Tenant { get; init; } = "default"; // promoted to transport headers
  [Sensitive] public string? InternalNote { get; init; } // redacted from logs and not promoted as a header
}
```

How they work
- [PartitionKey]: routing key gets a stable suffix like `.pN` (0–15) for shard affinity while keeping broad bindings (e.g., `product.created.#`).
- [Header(name)]: values are promoted to transport headers for downstream consumers; avoid large data.
- [Sensitive]: redacted in logs/diagnostics; if combined with [Header], it won’t be promoted.

## 7) Reliability: retries, DLQ, and publisher confirms

Behavior on handler failure (RabbitMQ)
- First failure: message is requeued for redelivery.
- Redelivery: if DLQ is enabled for the subscription, the message is NACKed without requeue (broker routes to DLX).
- Envelope.Attempt will be 1 on first delivery, 2 on redelivery.

Publisher confirms
- Enable with `RabbitMq:PublisherConfirms=true`. The publisher enters confirm mode and waits briefly for broker acks.

## 8) Health and diagnostics

- Health contributor reports connection status; marked critical so readiness reflects broker health.
- Inspect the effective messaging plan:
```csharp
var diag = sp.GetRequiredService<IMessagingDiagnostics>();
var plan = diag.GetEffectivePlan("rabbit");
```

## 9) Local dev with Docker

- The S3 sample includes a compose stack (RabbitMQ + sample). A helper `start.bat` supports up/rebuild/logs/down modes.
- Bind `ConnectionStrings:rabbitmq` to your broker.

## 10) Testing your flow (Testcontainers)

Minimal integration test
```csharp
public class MqTests : IAsyncLifetime
{
  private TestcontainersContainer? _rabbit;
  public async Task InitializeAsync()
  {
    _rabbit = new TestcontainersBuilder<TestcontainersContainer>()
      .WithImage("rabbitmq:3.13-management")
      .WithPortBinding(5674, 5672)
      .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5672))
      .Build();
    await _rabbit.StartAsync();
  }
  public Task DisposeAsync() => _rabbit?.DisposeAsync().AsTask() ?? Task.CompletedTask;

  [Fact]
  public async Task Send_and_handle()
  {
    var services = new ServiceCollection();
    var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
      ["Sora:Messaging:DefaultBus"] = "rabbit",
      ["Sora:Messaging:Buses:rabbit:Provider"] = "RabbitMq",
      ["Sora:Messaging:Buses:rabbit:ConnectionString"] = "amqp://guest:guest@localhost:5674",
      ["Sora:Messaging:Buses:rabbit:ProvisionOnStart"] = "true",
      ["Sora:Messaging:Buses:rabbit:Subscriptions:0:Name"] = "workers",
      ["Sora:Messaging:Buses:rabbit:Subscriptions:0:RoutingKeys:0"] = "product.created"
    }).Build();
    services.AddSingleton<IConfiguration>(cfg);
    services.AddSora();
    services.OnMessage<ProductCreated>((e,m) => Task.CompletedTask);
    var sp = services.BuildServiceProvider(); sp.UseSora();
    await new ProductCreated { Id = "p-9", Name = "Test" }.Send();
    await Task.Delay(250);
  }
}
```

## 11) Troubleshooting

- No messages consumed? Check routing key bindings (include `.#` if you use partition suffixing) and the alias used by your type.
- Provisioning errors in Production? Either set `ProvisionOnStart=true` or enable `Sora:AllowMagicInProduction=true` with caution.
- Large payloads? RabbitMQ default message size limit is small; see `MaxMessageSizeKB` in provider options.

You’re set. Start simple, then adopt batches, headers, and partitioning as your needs grow.
