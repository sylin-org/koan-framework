## Messaging Primitives (Contract)

Inputs / Concepts:
- Message types implementing marker interfaces or annotated with `[Message]`.
- Target service/domain/adapter names (string identifiers; centralize stable names in constants per ARCH-0040).

Outputs:
- Routed messages with standardized headers.
- Tracing + diagnostic events for send / handle.

Error Modes:
- Invalid target (command) → rejection.
- Serialization failure → logged + DLQ (if configured) after retries.

Success: Message delivered (at-least-once) and handler completes without throwing.

### Marker Interfaces
```csharp
public interface ICommandPrimitive {}
public interface IAnnouncementPrimitive {}
public interface IFlowEventPrimitive {} // for adapters (usually wrapped)
```
Records implement one as needed; no base class inheritance required.

### Sending APIs
```csharp
await command.SendCommand("payment");
await new UserRegistered(...).Announce();
await flowEvent.PublishFlowEvent();
```
All map to `IMessageBus.SendAsync(...)` after applying routing & headers.

### Routing Key Derivation
| Primitive | Pattern | Example |
|----------|---------|---------|
| Command | `cmd.{service}.{alias}[.vX]` | `cmd.payment.process-order.v1` |
| Announcement | `ann.{domain}.{alias}[.vX]` | `ann.user.user-registered` |
| FlowEvent | `flow.{adapter}.{alias}` | `flow.iot.temperature-reading` |

Alias comes from `[Message(Alias=.., Version=1)]` (version suffix only when enabled globally).

### Standard Headers
See ADR MESS-0070. Set automatically unless already present (user-supplied headers win):
`x-sora-kind`, `x-correlation-id`, `x-trace-id`, `x-command-target`, `x-retry-count`, `x-flow-adapter`, `x-flow-event-alias`.

### Handler Registration
```csharp
services.OnCommand<ProcessOrder>(async (cmd, ct) => { /* work */ });
services.OnAnnouncement<UserRegistered>(evt => Index(evt.UserId));
services.OnFlowEvent<TemperatureReading>(evt => _cache.Store(evt.DeviceId, evt.Value));
```

`On<T>()` remains valid; semantic variants improve readability and searchability.

### Retry & DLQ
Primitive semantics do not change retry behavior—policies remain per queue. Commands & announcements both respect configured `RetryOptions`.

### Idempotency
Use `[IdempotencyKey]` on a property for natural keys (e.g., external event ID). For composite keys, compute and stamp a header manually (`x-idempotency-key`).

### Flow Bridge
Flow builds a `FlowEvent` (normalized bag) and calls `PublishFlowEvent`. Bridge adds adapter identity headers & enforces naming policy. Consumers can treat certain FlowEvents as announcements if needed.

### Edge Cases
| Case | Resolution |
|------|------------|
| Command target missing | Reject + log; optional fallback service mapping hook |
| Duplicate idempotency key | Handler should early exit; infrastructure may short-circuit if inbox present |
| Version toggle mid-flight | Dual consumers may coexist; remove legacy binding after cutover |

### Minimal Example
```csharp
record ProcessOrder(string OrderId, decimal Amount) : ICommandPrimitive;
record UserRegistered(string UserId) : IAnnouncementPrimitive;

builder.Services
    .OnCommand<ProcessOrder>(c => _log.LogInformation("Process {0}", c.OrderId))
    .OnAnnouncement<UserRegistered>(u => _log.LogInformation("User {0}", u.UserId));

await new ProcessOrder("o-1", 42m).SendCommand("payment");
await new UserRegistered("u-1").Announce();
```

### References
- decisions/MESS-0070-messaging-topology-system-primitives-zero-config.md
- decisions/MESS-0071-messaging-dx-and-topology-provisioning.md
