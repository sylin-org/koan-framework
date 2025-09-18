# Koan.Messaging.RabbitMq - Technical reference

Contract

- Inputs: Connection string; alias→routing; subscriptions; retry/DLQ options; provisioning flag.
- Outputs: `IMessageBus` using RabbitMQ exchanges/queues/bindings; health; optional consumer loop.
- Delivery: at-least-once; FIFO per-queue; sharded ordering via partition suffix.

Options (under `Koan:Messaging:Buses:{code}:RabbitMq` unless noted)

- Connection: `ConnectionString` (amqp://user:pass@host:port/vhost) or `ConnectionStringName` (looks up `ConnectionStrings:{name}` from the root `IConfiguration`).
- Exchange: `Exchange` (default `Koan`), `ExchangeType` (`topic` default).
- Publisher: `PublisherConfirms` (default true), `MaxMessageSizeKB` (optional safety cap).
- Consumers: `Prefetch` (QoS per-channel), `Subscriptions` (array per group):
  - `Name` (group), `Queue` (optional explicit name), `RoutingKeys` (array or comma-separated), `Dlq` (bool), `Concurrency` (consumers per queue).
- Reliability: `Dlq.Enabled` (default false); `Retry.{MaxAttempts,FirstDelaySeconds,Backoff=fixed|exponential,MaxDelaySeconds}`.
- Provisioning: `ProvisionOnStart` (bool). When not explicitly set: defaults to true in non-Production; false in Production unless `Koan:AllowMagicInProduction=true`.
- Management (for topology inspection): `ManagementUrl` (e.g., `http://host:15672`), `ManagementUsername`, `ManagementPassword`. If omitted, the inspector derives `http://{amqpHost}:15672`, vhost from the AMQP URI path, and credentials from AMQP user info.
- Cross-section: `Koan:Messaging:{DefaultBus,DefaultGroup}` affect auto-subscribe when `Subscriptions` is empty.

Sample config

```json
{
  "Koan": {
    "Messaging": {
      "DefaultBus": "rabbit",
      "DefaultGroup": "workers",
      "Buses": {
        "rabbit": {
          "ConnectionString": "amqp://guest:guest@localhost:5672",
          "RabbitMq": {
            "Exchange": "Koan",
            "ExchangeType": "topic",
            "PublisherConfirms": true,
            "Prefetch": 100,
            "ProvisionOnStart": true,
            "Dlq": { "Enabled": true },
            "Retry": {
              "MaxAttempts": 5,
              "FirstDelaySeconds": 2,
              "Backoff": "exponential",
              "MaxDelaySeconds": 60
            },
            "Subscriptions": [
              {
                "Name": "workers",
                "RoutingKeys": ["#"],
                "Dlq": true,
                "Concurrency": 1
              }
            ]
          }
        }
      }
    }
  }
}
```

Aliasing, routing, and provisioning

- Aliases: message alias maps to AMQP `type` and base routing key; when `[PartitionKey]` is present, a stable suffix `.p{0..15}` is appended.
- Provisioning: when enabled, declares the main exchange, optional DLX, retry buckets (headers exchange + TTL queues), queues/bindings for each subscription.
- Inspection and reconciliation: when management is reachable, the provider inspects current exchanges/queues/bindings to support reconciliation.
  - Modes:
    - CreateIfMissing/ReconcileAdditive: create missing exchanges/queues/bindings; do not delete or alter existing ones. Duplicate bindings are skipped.
    - ForceRecreate: deletes extra or mismatched entities, then recreates according to the desired plan. Guarded by production safety checks in the factory.
- Auto-subscribe: when `Subscriptions` is empty and provisioning is on, a single queue is created for `DefaultGroup` with `#` binding.
- Subscriptions example: define multiple groups with specific routing keys and explicit queue names when needed for isolation.
- DLQ: when `Dlq.Enabled` is true and subscription `Dlq=true`, queues are declared with `x-dead-letter-exchange` bound to `{Exchange}.dlx`.

Publishing and batching

- Single publish: routing key is `{alias}` with optional `.p{n}` suffix; publisher confirms are used when enabled.
- Scheduled delivery: if `[DelaySeconds] > 0`, messages are routed through retry TTL buckets to approximate delay before final routing.
- Batch publish: `IEnumerable<object>.Send()`/`SendTo(code)` iterates messages on the chosen bus; headers and confirms apply per-message.
- Size guard: if `MaxMessageSizeKB` is configured and a payload exceeds it, the publish path throws.

Consumer concurrency and QoS

- Concurrency: `Subscriptions[].Concurrency` controls parallel consumers per declared queue; ordering remains FIFO within each queue.
- Prefetch: `Prefetch` sets basic.qos per channel to bound in-flight deliveries and backpressure consumers.
- Registration sugar: `services.On<T>(...)` (terse), `services.OnMessage<T>(...)` (with envelope), and aliases `OnCommand<T>`/`OnEvent<T>`/`Handle<T>` are provided by Messaging.Core and map to the same handler registration.

Headers and metadata

- Standard headers promoted from attributes: `x-idempotency-key`, `x-correlation-id` (also mapped to AMQP `CorrelationId`), `x-causation-id` (defaults to MessageId when absent), custom `[Header("name")]` fields.
- Retry/schedule headers: `x-attempt` (1..N), `x-retry-bucket` (seconds for TTL bucket selection).

Error modes

- Transient (connection resets, channel closures): publisher confirms or consumer retries will backoff per `Retry` with headers exchange buckets.
- Non-transient (auth, missing exchange when provisioning disabled): fail fast; operator action required.
- Oversize payload: if `MaxMessageSizeKB` is set and exceeded, publishing throws.

Edge cases

- Empty `Subscriptions` with provisioning disabled: no consumers are created; publishing works but nothing consumes until queues/bindings exist.
- Connection string via `ConnectionStringName` not found: provider fails start-up; define `ConnectionStrings:{name}` or use `ConnectionString` directly.
- DLQ disabled but subscription requests `Dlq=true`: queue is created without DLX; messages will be requeued/exhaust retries and then be dropped or stuck depending on consumer behavior.
- Excessive prefetch with low concurrency: can increase redelivery bursts on restart; tune `Prefetch` and `Concurrency` together.

Operations

- Health: broker connectivity and channel liveliness via a provider health contributor.
- Metrics: publish/consume throughput, acks/nacks, retry counts, DLQ rates, consumer lag.
- Logs: include alias, exchange/queue, routing key, delivery tag, attempt.

Distribution patterns (minimal configs)

Round-robin (competing consumers)

```json
{
  "Koan": {
    "Messaging": {
      "Buses": {
        "rabbit": {
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

- All instances share one group/queue; multiple consumers (or instances) compete for messages (per-queue FIFO maintained).

Broadcast (multiple groups receive the same messages)

```json
{
  "Koan": {
    "Messaging": {
      "Buses": {
        "rabbit": {
          "RabbitMq": {
            "Exchange": "Koan",
            "Subscriptions": [
              { "Name": "billing", "RoutingKeys": ["User.Registered"] },
              { "Name": "analytics", "RoutingKeys": ["User.Registered"] }
            ]
          }
        }
      }
    }
  }
}
```

- Each group has its own queue bound to the same routing key; each queue gets a copy (fan-out).

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
              { "Name": "billing", "RoutingKeys": ["Invoice.*", "Payment.#"] },
              {
                "Name": "fulfillment",
                "RoutingKeys": ["Order.Created", "Order.Shipped"]
              }
            ]
          }
        }
      }
    }
  }
}
```

- Topic wildcards `*` and `#` bind only what each group needs.

Partition-aware sharding

```json
{
  "Koan": {
    "Messaging": {
      "Buses": {
        "rabbit": {
          "RabbitMq": {
            "Exchange": "Koan",
            "Subscriptions": [
              {
                "Name": "p0-7",
                "RoutingKeys": [
                  "User.Registered.p0",
                  "User.Registered.p1",
                  "User.Registered.p2",
                  "User.Registered.p3",
                  "User.Registered.p4",
                  "User.Registered.p5",
                  "User.Registered.p6",
                  "User.Registered.p7"
                ]
              },
              {
                "Name": "p8-15",
                "RoutingKeys": [
                  "User.Registered.p8",
                  "User.Registered.p9",
                  "User.Registered.p10",
                  "User.Registered.p11",
                  "User.Registered.p12",
                  "User.Registered.p13",
                  "User.Registered.p14",
                  "User.Registered.p15"
                ]
              }
            ]
          }
        }
      }
    }
  }
}
```

- Messages with `[PartitionKey]` get `.p{0..15}` suffix. Split bindings across groups to shard load; set `Concurrency=1` per queue to preserve strict order.

Troubleshooting

- Connection refused/timeout: verify broker reachability and vhost auth; check firewalls and `ConnectionString`/`ConnectionStringName` resolution.
- Provisioning failures (missing exchange/queue): enable `ProvisionOnStart` or pre-create topology; ensure the user has configure/bind permissions.
- Inspector returns empty: configure `ManagementUrl` or ensure the management plugin is enabled and reachable; verify credentials.
- Publisher confirms timeouts: indicates broker pressure or network slowness; consider reducing message size, increasing timeouts, or disabling confirms if acceptable.
- DLQ not seeing messages: require both `Dlq.Enabled=true` and subscription `Dlq=true`; handlers must throw for the consumer to `Nack`.
- Delays ineffective: ensure `Retry.MaxAttempts >= 2` and TTL buckets are provisioned; set reasonable `FirstDelaySeconds`/`MaxDelaySeconds`.

References

- Messaging core: `../Koan.Messaging.Core/TECHNICAL.md`
- Overview: `/docs/reference/messaging.md`
- Decisions MESS-0021..0027: `/docs/decisions/`
