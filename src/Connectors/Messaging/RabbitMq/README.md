# Sylin.Koan.Messaging.Connector.RabbitMq

RabbitMQ transport for Koan Messaging: connection factory, publisher, and consumer helpers.

- Target framework: net9.0
- License: Apache-2.0

## Capabilities
- Alias-based routing to exchanges/queues, optional auto-provisioning
- Publisher confirms, prefetch/QoS, retry buckets (scheduled) and DLQ topology
- Health checks and diagnostics via Koan.Messaging.Core

## Install

```powershell
dotnet add package Sylin.Koan.Messaging.Connector.RabbitMq
```

## Minimal setup

Use Koan bootstrap. When this package is referenced, the RabbitMQ provider is auto-registered.

```csharp
var builder = WebApplication.CreateBuilder(args);

// Core wiring and auto-registrars
builder.Services.AddKoan();

// Optional: explicit registration (not required when using AddKoan)
// builder.Services.AddRabbitMq();

var app = builder.Build();
// Greenfield boot handled by host templates; ensure AppHost.Current is set and IAppRuntime started.
app.Run();
```

Quick config (appsettings.json):

```json
{
	"Koan": {
		"Messaging": {
			"DefaultBus": "rabbit",
			"DefaultGroup": "workers",
			"Buses": {
				"rabbit": {
					"ConnectionString": "amqp://guest:guest@localhost:5672",
					  // Or use ConnectionStringName to resolve from ConnectionStrings:{name}
					  // "ConnectionStringName": "RabbitMq",
					"RabbitMq": {
						"Exchange": "Koan",
						"Prefetch": 100,
						"ProvisionOnStart": true,
						"Dlq": { "Enabled": true },
						"Retry": { "MaxAttempts": 5, "FirstDelaySeconds": 2, "Backoff": "exponential", "MaxDelaySeconds": 60 },
						"Subscriptions": [
						  { "Name": "workers", "RoutingKeys": "#", "Dlq": true, "Concurrency": 1 },
						  { "Name": "billing", "RoutingKeys": ["Invoice.*", "Payment.#"], "Dlq": true, "Concurrency": 2, "Queue": "Koan.rabbit.billing" }
						]
					}
				}
			}
		}
	}
}
```

Environment variables (equivalents; use double underscores):

- Koan__Messaging__DefaultBus=rabbit
- Koan__Messaging__Buses__rabbit__ConnectionString=amqp://guest:guest@localhost:5672
- Koan__ConnectionStrings__RabbitMq=amqp://guest:guest@localhost:5672
- Koan__Messaging__Buses__rabbit__ConnectionStringName=RabbitMq
- Koan__Messaging__Buses__rabbit__RabbitMq__Exchange=Koan
- Koan__Messaging__Buses__rabbit__RabbitMq__Prefetch=100
- Koan__Messaging__Buses__rabbit__RabbitMq__ProvisionOnStart=true
- Koan__Messaging__Buses__rabbit__RabbitMq__Retry__MaxAttempts=5

## Usage

Declare messages with aliases; use attributes for headers, idempotency, partitioning, and optional delay.

```csharp
using Koan.Messaging;

[Message(Alias = "User.Registered", Version = 1)]
public sealed record UserRegistered(
		string UserId,
		[Header("x-tenant")] string Tenant,
		[IdempotencyKey] string EventId,
		[PartitionKey] string PartitionKey,
		[DelaySeconds] int DelaySeconds = 0);

// Send (uses DefaultBus)
await new UserRegistered("u-123", "acme", "evt-1", "acme:u-123").Send();

// Send to a specific bus
await new UserRegistered("u-456", "acme", "evt-2", "acme:u-456").SendTo("rabbit");
```

Handle messages via DI sugar:

```csharp
// Terse (no envelope)
builder.Services.On<UserRegistered>(msg => Console.WriteLine($"Welcome {msg.UserId}"));

// Or keep envelope when needed
builder.Services.OnMessage<UserRegistered>(async (env, msg, ct) =>
{
		// Idempotency key and correlation are available in env.Headers / env.CorrelationId
		// Do work; throw to trigger retry/DLQ per configured policy
		await Task.CompletedTask;
});
```

Batch send:

```csharp
var batch = new object[]
{
	new UserRegistered("u-789", "acme", "evt-3", "acme:u-789"),
	new UserRegistered("u-790", "acme", "evt-4", "acme:u-790")
};

await batch.Send();          // default bus
await batch.SendTo("rabbit"); // specific bus
```

Notes
- Subscriptions: if none are configured, the provider creates a default queue for `DefaultGroup` bound to `#`.
- Routing: the routing key is the message alias with an optional `.p{n}` partition suffix when `[PartitionKey]` is present.
- Scheduled send: set `[DelaySeconds]` to route through retry buckets for approximate delay delivery.
- Idempotency: set `[IdempotencyKey]` on messages and configure an inbox store to de-duplicate on the consumer side.

## Operations

- Health: a `RabbitMqHealth` contributor is registered; include health endpoints via Koan.Core.
- Diagnostics: effective plan and capabilities are published via `IMessagingDiagnostics`.
- DLQ/Retry: DLQs require `Dlq.Enabled=true` and subscriptions with `Dlq=true`; retries use TTL bucket queues behind a headers exchange.

## Distribution patterns (minimal configs)

Round-robin (competing consumers on one queue)

```json
{
	"Koan": {
		"Messaging": {
			"DefaultBus": "rabbit",
			"Buses": {
				"rabbit": {
					"ConnectionString": "amqp://guest:guest@localhost:5672",
					"RabbitMq": {
						"Exchange": "Koan",
						"Subscriptions": [
							{ "Name": "workers", "RoutingKeys": ["#"], "Concurrency": 4 }
						]
					}
				}
			}
		}
	}
}
```

- Implementation details: all service instances use the same `Name` (group) so they share a single queue; multiple consumers (Concurrency or many instances) compete, yielding round-robin delivery.

Broadcast (pub/sub to multiple groups)

```json
{
	"Koan": {
		"Messaging": {
			"DefaultBus": "rabbit",
			"Buses": {
				"rabbit": {
					"ConnectionString": "amqp://guest:guest@localhost:5672",
					"RabbitMq": {
						"Exchange": "Koan",
						"Subscriptions": [
							{ "Name": "billing", "RoutingKeys": ["User.Registered"], "Concurrency": 1 },
							{ "Name": "analytics", "RoutingKeys": ["User.Registered"], "Concurrency": 1 }
						]
					}
				}
			}
		}
	}
}
```

- Implementation details: each `Name` creates its own queue bound to the same routing key; every message is delivered to each queue (fan-out per group).

Selective topics (route by patterns)

```json
{
	"Koan": {
		"Messaging": {
			"Buses": {
				"rabbit": {
					"RabbitMq": {
						"Exchange": "Koan",
						"Subscriptions": [
							{ "Name": "billing",   "RoutingKeys": ["Invoice.*", "Payment.#"] },
							{ "Name": "fulfillment", "RoutingKeys": ["Order.Created", "Order.Shipped"] }
						]
					}
				}
			}
		}
	}
}
```

- Implementation details: topic exchange uses `*` (one segment) and `#` (many segments); bind only the patterns each group needs.

Partition-aware (shard by key)

```json
{
	"Koan": {
		"Messaging": {
			"Buses": {
				"rabbit": {
					"RabbitMq": {
						"Exchange": "Koan",
						"Subscriptions": [
							{ "Name": "p0-7",  "RoutingKeys": ["User.Registered.p0", "User.Registered.p1", "User.Registered.p2", "User.Registered.p3", "User.Registered.p4", "User.Registered.p5", "User.Registered.p6", "User.Registered.p7"] },
							{ "Name": "p8-15", "RoutingKeys": ["User.Registered.p8", "User.Registered.p9", "User.Registered.p10", "User.Registered.p11", "User.Registered.p12", "User.Registered.p13", "User.Registered.p14", "User.Registered.p15"] }
						]
					}
				}
			}
		}
	}
}
```

- Implementation details: messages decorated with `[PartitionKey]` get a stable `.p{0..15}` suffix on the routing key; split bindings across groups to shard load while preserving ordering within each queue. Use `Concurrency=1` for strict per-queue ordering.

## Troubleshooting

- Connection refused/timeout: verify `ConnectionString`/vhost and broker reachability; check firewall and credentials.
- Provisioning errors (missing exchange/queue): enable `ProvisionOnStart` or pre-create topology with equivalent names; ensure account has configure/bind permissions.
- Publisher confirms timeout: transient broker/backpressure; keep messages small, avoid long-running confirms; optionally set `PublisherConfirms=false` if your ops posture allows.
- DLQ not receiving: set both `Dlq.Enabled=true` and subscription `Dlq=true`; ensure handler throws on failures so the consumer can `Nack`.
- Scheduled send not delayed: require retry infra; set `Retry.MaxAttempts >= 2` to create TTL bucket queues; choose `FirstDelaySeconds`/`MaxDelaySeconds` appropriately.

## References
- Technical reference: `./TECHNICAL.md`
- Messaging core reference: `../Koan.Messaging.Core/TECHNICAL.md`
- Messaging overview: `/docs/reference/messaging.md`

