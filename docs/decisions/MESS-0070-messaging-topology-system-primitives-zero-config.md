# ADR: Messaging Topology, System Primitives, and Zero-Config Developer Experience

## Context

Koan.Canon and Koan.Messaging previously required some manual configuration and entity-centric modeling for messaging primitives. System-level primitives (Command, Announcement) were not first-class, and developers often needed to configure queues, exchanges, or bindings to get the stack working. This led to friction, risk of misconfiguration, and a suboptimal developer experience.

## Decision

Core decisions (accepted) plus refined architecture clarifications:

1. **Greenfield rebuild of messaging layers (Koan.Messaging + Flow bridge)** retaining reuse where practical.
2. **Automatic, idempotent topology plan/diff/apply at startup** (exchanges, queues, bindings) under a configurable `ProvisioningMode`:

- `Off`, `DryRun`, `CreateIfMissing`, `ReconcileAdditive`, `ForceRecreate`.

3. **System primitives are first-class and provider‑agnostic:**

- `Command` (targeted, point-to-point / work queue semantics)
- `Announcement` (fan-out, broadcast)
- `FlowEvent` bridge (Flow → Messaging adapter emission) exposed via a dedicated façade.

4. **Intent-driven APIs** (additive to existing sugar):

- Sending: `SendCommand(targetService, command)`, `Announce(payload)`, `PublishFlowEvent(flowEvent)`
- Handling: `OnCommand<T>()`, `OnAnnouncement<T>()`, `OnFlowEvent<T>()` (alias forms retained: `On<T>`).

5. **Zero-config developer path**: A project with `services.AddMessagingCore()` and at least one messaging provider automatically provisions a sensible topology in non-production.
6. **Explicit escape hatches & advanced control** via `ITopologyProvisioner` (manual declarations) and `ProvisioningOptions`.
7. **Deterministic naming & namespace strategy** centralized in a `TopologyNaming` helper (single source of truth, no scattered literals).
8. **Standard headers & envelope enrichment** for primitives (see Standard Headers below) eliminate ad-hoc correlation & tracing code.
9. **In-memory provider (full-fidelity)** for tests and local workflows (`services.UseInMemoryMessaging()`), mirroring production semantics.
10. **Flow integration** consumes primitives (no bespoke command bus example code in Flow docs).

### Standard Headers

| Header               | Purpose                                                  |
| -------------------- | -------------------------------------------------------- |
| `x-Koan-kind`        | Primitive kind (`command`, `announcement`, `flow-event`) |
| `x-correlation-id`   | Distributed correlation (propagated / generated)         |
| `x-trace-id`         | Trace root/span bridge to OpenTelemetry                  |
| `x-command-target`   | Target service / group for commands                      |
| `x-retry-count`      | Delivery attempt (incremented at handler dispatch)       |
| `x-flow-adapter`     | (FlowEvent) Origin adapter/system identity               |
| `x-flow-event-alias` | (FlowEvent) Logical event alias/version                  |

### Naming Conventions

All routing keys are lower-kebab-case (alias processed) and version suffix optional (controlled by `MessagingOptions.IncludeVersionInAlias`).

| Primitive    | Pattern                      | Example                        |
| ------------ | ---------------------------- | ------------------------------ |
| Command      | `cmd.{service}.{alias}[.vX]` | `cmd.payment.process-order.v1` |
| Announcement | `ann.{domain}.{alias}[.vX]`  | `ann.user.user-registered.v1`  |
| FlowEvent    | `flow.{adapter}.{alias}`     | `flow.iot.temperature-reading` |

Queues (default group workers): `{primitive}.{service|domain|adapter}.{alias}[.vX].q.{group}`

DLQ naming: `{queue}.dlq` (same exchange unless provider requires separate).

### Provisioning Lifecycle (High Level)

1. Scan registered handlers + explicitly added message types.
2. Derive desired topology objects (exchanges, queues, bindings, DLQs).
3. Compare with provider-reported current topology → `TopologyDiff`.
4. Apply per `ProvisioningMode` (log structured diagnostics `messaging.topology.applied`).
5. Cache last applied plan hash for fast no-op startup.

### Safety & Production Posture

- Default provisioning mode: `CreateIfMissing` in non-production, `Off` in production unless explicitly overridden.
- `DryRun` prints diff and exits early (build pipelines / infra review).
- `ForceRecreate` reserved for controlled migrations (drops & re-adds objects where supported).

### Backward Compatibility

Legacy `Send` / `On<T>` remain; primitives layer sits atop them. Deprecation (compiler `[Obsolete]`) deferred until adoption stabilizes.

## Rationale

- **DX:** Zero-config + primitives shrink conceptual load; new contributors focus on business messages, not broker wiring.
- **Reliability:** Centralized planner prevents drift & snowflake topologies; diff visibility aids ops.
- **Observability:** Standard headers create consistent tracing & retry metrics surface.
- **Extensibility:** Provisioner + naming helper allow advanced customization without abandoning sane defaults.

## Consequences

- **Breaking:** Manual topology config & custom command abstractions become redundant.
- **Docs & Samples:** Must be rewritten to use primitives & provisioning lifecycle.
- **Operational Change:** Provisioning now a first-class, logged phase (pipelines may gate on `DryRun`).
- **Deprecations:** Future removal path for legacy extension overloads once telemetry shows low usage.

## Migration Notes

1. Remove bespoke queue/exchange declarations (compose manifests may keep infrastructure definitions-framework will reconcile missing objects).
2. Replace ad-hoc command senders with `SendCommand(target, command)` (or `command.SendCommand(target)` once extension sugar added).
3. Replace broadcast patterns (topic `#`) with `Announce(payload)`.
4. Flow: use new `PublishFlowEvent(...)` from bridge instead of manual send.
5. For production clusters previously relying on manual provisioning: start with `ProvisioningMode=DryRun` → inspect plan → switch to `CreateIfMissing`.
6. Adopt new headers only if not already using conflicting custom names (framework-layer injection is additive).

Edge considerations:

- Mixed-version rollout: earlier services (no primitives) still receive messages; aliases unchanged, only routing key shape differs for new primitives.
- DLQ name collisions: if existing convention differs, set a temporary override via options until switchover.

---

**Status:**  
Accepted. Implementation to begin in next major release cycle.

**Related:**

- `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- `/docs/decisions/WEB-0035-entitycontroller-transformers.md`
- `/docs/architecture/principles.md`
