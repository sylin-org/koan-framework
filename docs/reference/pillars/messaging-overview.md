# Messaging Reference

Canonical APIs and patterns for Sora messaging: primitives, topology lifecycle, batching, aliasing, inbox, diagnostics.

> For focused deep dives see: `messaging-primitives.md` and `messaging-topology-lifecycle.md`.

## Flow envelope and reserved keys

See ADR [FLOW-0105](../decisions/FLOW-0105-external-id-translation-adapter-identity-and-normalized-payloads.md) for the full decision. Summary of reserved keys used by Flow adapters and the ingestion pipeline:

### Adapter identity

- Stamped on every message from a class annotated with `[FlowAdapter(system, adapter)]`:
  - `adapter.system` — stable system identifier (e.g., `oem`, `opcua`, `sap`).
  - `adapter.name` — adapter variant/name (e.g., `iot-oem-hub`).

Centralize system names in `Infrastructure/Constants` per ARCH-0040.

### External identifiers (envelope metadata)

- `identifier.external.<system>` — external/native ID values from the producing adapter.
  - Multiple entries allowed per message for different systems.
  - Examples:
    - `identifier.external.oem = OEM-00001`
    - `identifier.external.erp = DEV-42`

These keys are preserved end-to-end for audit and used to populate the ExternalId index when a canonical entity is created/updated.

### Contractless (normalized bag) payloads

When sending a normalized payload instead of a strong-typed model, use a JSON-path-like bag with reserved keys:

- `model` — canonical entity key (e.g., `Keys.Device.Key`, `Keys.Sensor.Key`).
- `reference.<entityKey>.external.<system>` — external ID of a referenced canonical entity to resolve `[ParentKey]` properties.
  - Example: `reference.device.external.oem = OEM-00001`
- Arbitrary field paths (e.g., `inventory`, `serial`, `SerialNumber`) carry model data.

The ingestion pipeline maps bag fields to the model contract and resolves canonical references via the ExternalId index before persistence.

### Parent keys and canonical resolution

- Mark canonical relationships in models with `[ParentKey(targetKey)]`. - Example: `Sensor.DeviceId` is marked with `[ParentKey]`.
- Adapters do not set canonical IDs.
- The resolver fills canonical parent keys by:
  1.  Checking for an existing canonical value;
  2.  Looking for `reference.<targetKey>.external.<system>` in contractless bags;
  3.  Falling back to envelope `identifier.external.*` context if sufficient;
  4.  Deferring if unresolved (retry policy applies).

### Error modes and policies

- Unknown external ID: defer with retry; after threshold, dead-letter with `reason=external-id-not-found`.
- Duplicate external ID mapping: reject or overwrite per configuration.
- Validation: missing required `model` for normalized payloads is a hard error.

Ensure all literals used for systems and keys are pulled from centralized constants.

## Contract (Concise)

Inputs:

- Message types annotated with `[Message]` or implementing primitive marker interfaces.
- Handler registrations (`IMessageHandler<T>` or sugar `On<T>()`).
- Configuration: `Sora:Messaging:*` and optional `SORA_MESSAGING_PROVISION` env override.

Outputs:

- Planned + applied broker topology (exchanges, queues, bindings, DLQs) per bus.
- Delivered primitives with enriched headers.
- Diagnostics events: topology planned/applied, primitive sent, handler failures.

Error Modes:

- Provisioning unreachable broker → startup fail (unless explicit override).
- Handler exception → retry / DLQ per policy.
- Unknown primitive target service (command) → rejection + diagnostic `command-target-not-found`.
- Oversized batch (provider limit) → chunked transparently; final chunk failure surfaces aggregate exception.

Success Criteria:

- All registered handlers bound exactly once per group.
- No unplanned topology drift (diff hash stable) unless config changed.
- Primitive round-trip traceable via correlation ID.

## Primitive Overview

| Primitive    | Semantics                               | Routing Pattern              | Delivery              |
| ------------ | --------------------------------------- | ---------------------------- | --------------------- |
| Command      | Directed work to a target service/group | `cmd.{service}.{alias}[.vX]` | At-least-once (queue) |
| Announcement | Broadcast / fan-out                     | `ann.{domain}.{alias}[.vX]`  | At-least-once (topic) |
| FlowEvent    | Adapter → flow ingestion                | `flow.{adapter}.{alias}`     | Stream/fan-out        |

See `messaging-primitives.md` for expanded details.

## Examples

```csharp
// Publish a single message
await bus.SendAsync(new OrderPlaced(orderId, total), ct);

// Batch send
await using var batch = bus.CreateBatch();
batch.Add(new ItemAdded(orderId, sku));
batch.Add(new ItemAdded(orderId, anotherSku));
await batch.SendAsync(ct);

// Register a handler using OnMessage sugar (startup)
services.OnMessage<OrderPlaced>(async (msg, sp, ct) =>
{
		var logger = sp.GetRequiredService<ILogger<OrderPlaced>>();
		logger.LogInformation("Order {OrderId} placed for {Total}", msg.OrderId, msg.Total);
});
```

Minimal bus config (per-bus, appsettings.json):

```json
// appsettings.json
{
  "Sora": {
    "Messaging": {
      "DefaultBus": "rabbit",
      "DefaultGroup": "workers",
      "Buses": {
        "rabbit": {
          "ConnectionString": "amqp://guest:guest@localhost:5672/",
          "RabbitMq": {
            // optional; dev defaults provision automatically when not Production
            "ProvisionOnStart": true,
            // optional explicit catch-all subscription; if omitted, a default will be used in dev
            "Subscriptions": [
              {
                "Name": "workers",
                "RoutingKeys": "#",
                "Concurrency": 1,
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

Environment variable equivalents:

```
Sora__Messaging__DefaultBus=rabbit
Sora__Messaging__DefaultGroup=workers
Sora__Messaging__Buses__rabbit__ConnectionString=amqp://guest:guest@localhost:5672/
Sora__Messaging__Buses__rabbit__RabbitMq__ProvisionOnStart=true
```

## Notes

- Explicit Subscriptions disable auto-subscribe.
- Use aliases to interop across languages when you can’t share type names.
- If no ConnectionString is set for RabbitMQ, the provider falls back to `amqp://guest:guest@localhost:5672/` in development scenarios.

## Edge Cases

| Case                           | Behavior                     | Mitigation                                            |
| ------------------------------ | ---------------------------- | ----------------------------------------------------- |
| Missing alias                  | Falls back to full type name | Add `[Message(Alias=...)]` for portability            |
| Handler throws repeatedly      | Retries then DLQ             | Keep handlers idempotent; leverage idempotency keys   |
| Broker temp outage during send | Transient exception surfaced | Upstream retry / circuit breaker                      |
| Mixed version alias toggle     | Dual bindings temporarily    | Use `DryRun` plan diff before enabling version suffix |
| Command to unknown target      | Logged + rejected            | Ensure registration or service discovery mapping      |

## Testing (In-Memory)

Enable deterministic tests:

```csharp
services.UseInMemoryMessaging();
// Assert topology
var topo = sp.GetRequiredService<IInMemoryTopologySnapshot>();
Assert.Contains("cmd.payment.process-order", topo.RoutingKeys);
```

## Provisioning Modes (Summary)

| Mode              | Action                               |
| ----------------- | ------------------------------------ |
| Off               | Skip plan/apply                      |
| DryRun            | Log plan & diff only                 |
| CreateIfMissing   | Create absent objects (never delete) |
| ReconcileAdditive | Create + add missing bindings/queues |
| ForceRecreate     | Drop & recreate (dangerous)          |

## References

- decisions/MESS-0021-messaging-capabilities-and-negotiation.md
- decisions/MESS-0022-mq-provisioning-aliases-and-dispatcher.md
- decisions/MESS-0023-alias-defaults-default-group-and-onmessage.md
- decisions/MESS-0024-batch-semantics-and-aliasing.md
- decisions/MESS-0025-inbox-contract-and-client.md
- decisions/MESS-0027-standalone-mq-services-and-naming.md
- decisions/MESS-0070-messaging-topology-system-primitives-zero-config.md
- decisions/MESS-0071-messaging-dx-and-topology-provisioning.md
