---
type: REF
domain: messaging
title: "Messaging Pillar Reference"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-28
framework_version: v0.6.2
validation:
    date_last_tested: 2025-09-28
    status: verified
    scope: docs/reference/messaging/index.md
---

# Messaging Pillar Reference

**Document Type**: REF
**Target Audience**: Developers, Architects
**Last Updated**: 2025-09-28
**Framework Version**: v0.6.2

---

## Installation

```bash
dotnet add package Koan.Messaging
dotnet add package Koan.Messaging.RabbitMq
```

```csharp
// Program.cs
builder.Services.AddKoan();
```

## Message Types

### Command (Directed Work)

```csharp
[Message(Alias = "process-order")]
public class ProcessOrder
{
    public string OrderId { get; set; } = "";
    public decimal Total { get; set; }
    public string CustomerId { get; set; } = "";
}

// Send to specific service
await bus.SendAsync(new ProcessOrder
{
    OrderId = "ORD-001",
    Total = 99.99m,
    CustomerId = "CUST-123"
});
```

### Announcement (Broadcast)

```csharp
[Message(Alias = "order-placed")]
public class OrderPlaced
{
    public string OrderId { get; set; } = "";
    public decimal Total { get; set; }
    public DateTimeOffset PlacedAt { get; set; }
}

// Broadcast to all interested services
await bus.SendAsync(new OrderPlaced
{
    OrderId = "ORD-001",
    Total = 99.99m,
    PlacedAt = DateTimeOffset.UtcNow
});
```

### Flow Event (Data Pipeline)

```csharp
[Message(Alias = "device-data")]
public class DeviceDataReceived
{
    public string DeviceId { get; set; } = "";
    public Dictionary<string, object> Data { get; set; } = new();
    public DateTimeOffset ReceivedAt { get; set; }
}

// Send to flow pipeline
await bus.SendAsync(new DeviceDataReceived
{
    DeviceId = "DEV-001",
    Data = new Dictionary<string, object>
    {
        ["temperature"] = 22.5,
        ["humidity"] = 65.0
    },
    ReceivedAt = DateTimeOffset.UtcNow
});
```

## Message Handlers

### Background Service Handler

```csharp
public class OrderProcessor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Handle commands
        await this.On<ProcessOrder>(async (message, sp, ct) =>
        {
            var orderService = sp.GetRequiredService<IOrderService>();
            await orderService.ProcessAsync(message.OrderId, ct);
        });

        // Handle announcements
        await this.On<OrderPlaced>(async (message, sp, ct) =>
        {
            var logger = sp.GetRequiredService<ILogger<OrderProcessor>>();
            logger.LogInformation("Order {OrderId} placed for {Total}",
                message.OrderId, message.Total);
        });
    }
}
```

### Controller Handler

```csharp
[Route("api/[controller]")]
public class OrderController : ControllerBase
{
    private readonly IBus _bus;

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var order = new Order
        {
            CustomerId = request.CustomerId,
            Total = request.Total
        };

        await order.Save();

        // Send command for processing
        await _bus.SendAsync(new ProcessOrder
        {
            OrderId = order.Id,
            Total = order.Total,
            CustomerId = order.CustomerId
        });

        return Ok(order);
    }
}
```

## Batch Messaging

### Creating Batches

```csharp
// Create batch for multiple messages
await using var batch = bus.CreateBatch();

foreach (var item in orderItems)
{
    batch.Add(new ItemAdded
    {
        OrderId = orderId,
        Sku = item.Sku,
        Quantity = item.Quantity
    });
}

await batch.SendAsync();
```

### Batch Handler

```csharp
public class InventoryService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await this.OnBatch<ItemAdded>(async (messages, sp, ct) =>
        {
            var items = messages.GroupBy(m => m.Sku)
                .Select(g => new { Sku = g.Key, TotalQuantity = g.Sum(x => x.Quantity) });

            foreach (var item in items)
            {
                await UpdateInventory(item.Sku, -item.TotalQuantity);
            }
        });
    }
}
```

## Configuration

### Basic RabbitMQ Configuration

```json
{
  "Koan": {
    "Messaging": {
      "DefaultBus": "rabbit",
      "DefaultGroup": "workers",
      "Buses": {
        "rabbit": {
          "ConnectionString": "amqp://guest:guest@localhost:5672/",
          "RabbitMq": {
            "ProvisionOnStart": true
          }
        }
      }
    }
  }
}
```

### Multiple Buses

```json
{
  "Koan": {
    "Messaging": {
      "Buses": {
        "commands": {
          "ConnectionString": "amqp://localhost:5672/commands",
          "RabbitMq": {
            "ProvisionOnStart": true,
            "Subscriptions": [
              {
                "Name": "order-workers",
                "RoutingKeys": "cmd.order.*",
                "Concurrency": 5,
                "Dlq": true
              }
            ]
          }
        },
        "events": {
          "ConnectionString": "amqp://localhost:5672/events",
          "RabbitMq": {
            "ProvisionOnStart": true,
            "Subscriptions": [
              {
                "Name": "event-handlers",
                "RoutingKeys": "ann.*",
                "Concurrency": 10,
                "Dlq": true
              }
            ]
          }
        }
      }
    }
  }
}
```

### Environment Variables

```bash
# Connection strings
Koan__Messaging__Buses__rabbit__ConnectionString=amqp://guest:guest@localhost:5672/

# Provisioning
Koan__Messaging__Buses__rabbit__RabbitMq__ProvisionOnStart=true

# Groups and routing
Koan__Messaging__DefaultGroup=workers
Koan__Messaging__DefaultBus=rabbit
```

## Message Routing

### Routing Patterns

```csharp
// Commands: cmd.{service}.{alias}
// ProcessOrder → cmd.order.process-order

// Announcements: ann.{domain}.{alias}
// OrderPlaced → ann.order.order-placed

// Flow Events: flow.{adapter}.{alias}
// DeviceData → flow.iot.device-data
```

### Custom Routing

```csharp
[Message(Alias = "high-priority-order", Version = "v2")]
public class HighPriorityOrder
{
    public string OrderId { get; set; } = "";
    public int Priority { get; set; }
}

// Results in routing key: cmd.order.high-priority-order.v2
```

## Error Handling and Retries

### Retry Configuration

```json
{
  "Koan": {
    "Messaging": {
      "Buses": {
        "rabbit": {
          "RabbitMq": {
            "Subscriptions": [
              {
                "Name": "order-workers",
                "RoutingKeys": "cmd.order.*",
                "RetryPolicy": {
                  "MaxRetries": 3,
                  "RetryDelay": "00:00:05",
                  "BackoffMultiplier": 2.0
                },
                "Dlq": true
              }
            ]
          }
        }
      }
    }
  }
}
```

### Dead Letter Handling

```csharp
public class DeadLetterHandler : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await this.On<DeadLetterMessage>(async (message, sp, ct) =>
        {
            var logger = sp.GetRequiredService<ILogger<DeadLetterHandler>>();

            logger.LogError("Message failed after retries: {MessageType} - {Error}",
                message.OriginalMessageType, message.LastError);

            // Send to monitoring system
            await _monitoring.RecordDeadLetter(message);
        });
    }
}
```

## Inbox Pattern

### Idempotent Message Processing

```csharp
public class PaymentProcessor : BackgroundService
{
    private readonly IInboxService _inbox;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await this.On<ProcessPayment>(async (message, sp, ct) =>
        {
            // Check if already processed
            if (await _inbox.IsProcessedAsync(message.PaymentId))
            {
                return; // Skip duplicate
            }

            try
            {
                await ProcessPayment(message);
                await _inbox.MarkProcessedAsync(message.PaymentId);
            }
            catch (Exception ex)
            {
                await _inbox.MarkFailedAsync(message.PaymentId, ex.Message);
                throw;
            }
        });
    }
}
```

### Outbox Pattern

```csharp
public class OrderService
{
    private readonly IBus _bus;
    private readonly IOutboxService _outbox;

    public async Task CreateOrderAsync(CreateOrderRequest request)
    {
        using var transaction = _dbContext.Database.BeginTransaction();

        try
        {
            // Save order
            var order = new Order { /* ... */ };
            await order.Save();

            // Queue outgoing messages
            await _outbox.AddAsync(new OrderCreated
            {
                OrderId = order.Id,
                CustomerId = order.CustomerId,
                Total = order.Total
            });

            await _outbox.AddAsync(new ProcessOrder
            {
                OrderId = order.Id,
                Total = order.Total,
                CustomerId = order.CustomerId
            });

            // Commit transaction
            await transaction.CommitAsync();

            // Send queued messages
            await _outbox.PublishPendingAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
```

## Topology Management

### Automatic Provisioning

```csharp
// Framework automatically creates:
// - Exchanges for commands, announcements, flow events
// - Queues for service groups
// - Bindings based on message handlers
// - Dead letter queues when enabled
```

### Manual Topology

```csharp
public class CustomTopologyProvider : ITopologyProvider
{
    public Task<TopologyPlan> GeneratePlanAsync(MessageHandlerRegistry registry)
    {
        var plan = new TopologyPlan();

        // Custom exchange
        plan.Exchanges.Add(new Exchange
        {
            Name = "priority-orders",
            Type = ExchangeType.Topic,
            Durable = true
        });

        // Custom queue
        plan.Queues.Add(new Queue
        {
            Name = "high-priority-workers",
            Durable = true,
            Arguments = new Dictionary<string, object>
            {
                ["x-max-priority"] = 10
            }
        });

        return Task.FromResult(plan);
    }
}
```

## Monitoring and Health

### Health Checks

```csharp
public class MessagingHealthCheck : IHealthContributor
{
    public string Name => "Messaging";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            // Check broker connectivity
            var isConnected = await _busProvider.IsConnectedAsync();

            // Check queue depths
            var queueDepths = await _busProvider.GetQueueDepthsAsync();
            var hasBacklog = queueDepths.Values.Any(depth => depth > 1000);

            var isHealthy = isConnected && !hasBacklog;
            var message = isHealthy ? null : "Broker disconnected or queue backlog detected";

            return new HealthReport(Name, isHealthy, message);
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, false, ex.Message);
        }
    }
}
```

### Metrics Collection

```csharp
public class MessagingMetrics : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var metrics = await _busProvider.GetMetricsAsync();

            _logger.LogInformation("Messages sent: {Sent}, received: {Received}, failed: {Failed}",
                metrics.MessagesSent, metrics.MessagesReceived, metrics.MessagesFailed);

            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}
```

## Testing

### In-Memory Testing

```csharp
[Test]
public async Task Should_Process_Order_Command()
{
    // Arrange
    services.UseInMemoryMessaging();
    var bus = services.GetRequiredService<IBus>();
    var processor = services.GetRequiredService<OrderProcessor>();

    // Act
    await bus.SendAsync(new ProcessOrder
    {
        OrderId = "TEST-001",
        Total = 50.00m,
        CustomerId = "CUST-001"
    });

    // Process messages
    await processor.ProcessPendingAsync();

    // Assert
    var order = await Order.ById("TEST-001");
    Assert.AreEqual(OrderStatus.Processing, order.Status);
}
```

### Message Verification

```csharp
[Test]
public async Task Should_Send_Order_Placed_Event()
{
    // Arrange
    var messageSink = services.GetRequiredService<IInMemoryMessageSink>();

    // Act
    await _orderService.CreateOrderAsync(new CreateOrderRequest
    {
        CustomerId = "CUST-001",
        Items = new[] { new OrderItem { Sku = "ITEM-001", Quantity = 2 } }
    });

    // Assert
    var sentMessages = messageSink.GetSentMessages<OrderPlaced>();
    Assert.AreEqual(1, sentMessages.Count);
    Assert.AreEqual("CUST-001", sentMessages[0].CustomerId);
}
```

## API Reference

### Core Interfaces

```csharp
public interface IBus
{
    Task SendAsync<T>(T message, CancellationToken ct = default);
    Task SendBatchAsync<T>(IEnumerable<T> messages, CancellationToken ct = default);
    IMessageBatch CreateBatch();
}

public interface IMessageBatch : IAsyncDisposable
{
    void Add<T>(T message);
    Task SendAsync(CancellationToken ct = default);
}

public interface IMessageHandler<T>
{
    Task HandleAsync(T message, IServiceProvider services, CancellationToken ct = default);
}
```

### Message Registration

```csharp
// Attribute-based registration
[Message(Alias = "process-order", Version = "v1")]
public class ProcessOrder { }

// Service registration
services.OnMessage<ProcessOrder>(async (message, sp, ct) =>
{
    // Handle message
});

// Batch registration
services.OnBatch<OrderItem>(async (messages, sp, ct) =>
{
    // Handle message batch
});
```

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+