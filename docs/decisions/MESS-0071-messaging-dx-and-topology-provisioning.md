# ADR: Koan.Messaging Developer Experience and Topology Provisioning

## Context

Koan.Messaging aims to provide a simple, powerful, and provider-agnostic messaging abstraction for .NET. Current APIs focus on message send/receive and handler registration, with strong conventions and auto-registration. However, there is no public, agnostic API for consumers to declaratively request or provision custom MQ topologies (exchanges, queues, bindings) at runtime. There is also an opportunity to further improve developer experience (DX) with a more fluent, discoverable, and testable API surface.

## Decision

Augments original decision with concrete API & lifecycle definition:

1. **Public provisioning surface** (`ITopologyProvisioner`) exposing *idempotent* async methods:
	```csharp
	public interface ITopologyProvisioner {
		 Task DeclareExchangeAsync(string name, ExchangeType type = ExchangeType.Topic, bool durable = true, bool autoDelete = false, CancellationToken ct = default);
		 Task DeclareQueueAsync(string name, bool durable = true, bool exclusive = false, bool autoDelete = false, CancellationToken ct = default);
		 Task BindQueueAsync(string queue, string exchange, string routingKey, CancellationToken ct = default);
	}
	```
2. **Automatic planner** executes before app starts handling traffic: discover → plan → diff → apply (per `ProvisioningMode`).
3. **Primitive-oriented high-level façades** (on top of `IMessageBus`):
	- `ICommandBus.SendAsync(string targetService, object command, CancellationToken)`
	- `IAnnouncementBus.BroadcastAsync(object announcement, CancellationToken)`
	- `IFlowEventPublisher.PublishAsync(FlowEvent evt, CancellationToken)`
4. **Extension sugar** (ergonomic form): `await command.SendCommand("payment");`, `await announcement.Announce();`.
5. **Handler registration shortcuts**: `services.OnCommand<T>()`, `services.OnAnnouncement<T>()`, `services.OnFlowEvent<T>()` (internally standard `IMessageHandler<T>` registration).
6. **Diagnostics hooks**: `IMessagingDiagnostics.TopologyPlanned`, `.TopologyApplied`, `.PrimitiveSent`.
7. **In-memory provider** implementing full contract & provisioning no-ops (records plan for assertions in tests).
8. **Naming centralization** via `ITopologyNaming` (implemented by `DefaultTopologyNaming`).
9. **Opt-in advanced customization**: replace naming service or wrap provisioner; all are additive, never required for basic use.
10. **Safety modes**: explicit environment variable `Koan_MESSAGING_PROVISION=DryRun|CreateIfMissing|...` overrides config (dev containers / CI convenience).

## Rationale

- **Declarative minimalism:** Most users never call the provisioner directly; it exists as an escape hatch.
- **Determinism:** Central naming removes brittle string duplication and drift across services.
- **Observability-first:** Each topology application produces a structured event; auditing infra changes becomes trivial.
- **Parity testing:** In-memory provider allowing inspection of *planned* topology closes the gap between local/unit and integration tests.

## Consequences

- **Refactor:** Providers must implement provisioner & capability introspection (lightweight wrappers for brokers like RabbitMQ).
- **Docs rewrite:** Messaging reference split into primitives + topology lifecycle sections for clarity.
- **Testing Shift:** Teams can prefer in-memory provider for deterministic unit tests rather than broker containers.
- **Operational Review:** CI can run `DryRun` and diff previous plan artifact as a guardrail.

## Migration Notes

1. Introduce provisioner & planner; do not remove existing factories until parity validated.
2. Add in-memory provider and gate new samples to it for speed.
3. Flow: swap custom command dispatcher sections with primitive usage examples.
4. Stage rollout: enable `DryRun` in CI → human review → switch environments to `CreateIfMissing`.
5. Optionally archive old messaging how-to snippets under `docs/proposals/` for historical context.

### Edge Cases & Safeguards
| Scenario | Handling |
|----------|----------|
| Broker unavailable at startup | Fail fast (non-prod: warn + skip if configured `AllowStartupWithoutBroker=true`) |
| Conflicting queue ownership (different durability) | Emit diff with severity=error; require manual intervention unless `ForceRecreate` |
| Renaming alias with existing queue | Planner proposes create new + optionally bind old (compat window) unless `ForceRecreate` |
| Multiple services claim same command queue | Allowed if same group; otherwise warning (potential hot-spot) |

### Open Follow-ups (tracked outside this ADR)
- Analyzer to warn on sending anonymous object lacking `[Message]` metadata.
- Contract hash header for cross-service drift detection.
- Declarative "virtual topology" file export (`Koan topology export`).

---

**Status:**  
Accepted. Implementation and refactoring to begin immediately.

**Related:**

- `/docs/decisions/MESS-0070-messaging-topology-system-primitives-zero-config.md`
- `/docs/architecture/principles.md`
