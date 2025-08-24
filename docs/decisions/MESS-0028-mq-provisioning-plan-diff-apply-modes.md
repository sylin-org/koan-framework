---
id: MESS-0028
slug: MESS-0028-mq-provisioning-plan-diff-apply-modes
domain: MESS
status: Accepted
date: 2025-08-24
title: MQ provisioning — plan/diff/apply architecture and modes
---

# 0028 — MQ provisioning: plan/diff/apply and modes

## Context
Messaging needs predictable, inspectable provisioning with production guardrails. We want a first-class, testable architecture to declare desired topology (exchanges, queues, bindings, DLQ/retry), inspect the current broker state, compute a diff, and apply changes under explicit modes. We already set high-level defaults (see MESS-0022). This decision defines the contracts and modes to implement across providers, starting with RabbitMQ.

## Decision
Introduce a provider-agnostic provisioning pipeline with four roles and explicit modes.

- Contracts
  - Desired topology model (provider-neutral):
    - ExchangeSpec: name, type (direct|topic|fanout|headers), durable, autoDelete, arguments.
    - QueueSpec: name, durable, exclusive, autoDelete, arguments; optional DlqSpec; optional RetryBucketsSpec; optional MessageTtl.
    - BindingSpec: from (exchange), to (exchange|queue), routingKey, arguments.
  - Current topology snapshot: mirror of the above, as discovered from the broker.
  - Diff model: additions, updates (argument changes), removals, and destructive operations flagged separately.

- Roles
  - Planner: Builds DesiredTopology from bus/subscription options and conventions (including alias registry, partition suffixing, DLQ/retry policies).
  - Inspector: Queries broker to capture CurrentTopology.
  - Differ: Computes Diff = Desired − Current (safe/additive vs. destructive changes clearly separated).
  - Applier: Applies allowed parts of Diff based on ProvisioningMode; records diagnostics and effective plan.

- Modes (ProvisioningMode)
  - Off: Do nothing (skip Inspector/Differ/Applier). Startup proceeds to consumers immediately.
  - DryRun: Plan + Inspect + Diff, but do not apply; emit diagnostics (structured log + IMessagingDiagnostics snapshot).
  - CreateIfMissing: Apply only additive create operations (exchanges, queues, bindings that don’t exist). No updates or removals.
  - ReconcileAdditive: Apply additive creates and non-destructive updates (argument additions that are safe). No removals or type changes.
  - ForceRecreate: Apply full diff including destructive changes (drop/recreate). Requires explicit enablement and production override.

- Defaults and guards
  - Default mode: CreateIfMissing for Development/Test; Off for Production environments unless Sora:AllowMagicInProduction=true.
  - An explicit configured mode always wins. ForceRecreate requires both an explicit mode and production override.
  - Startup order: Provisioning (according to mode) completes before consumers start.

- Diagnostics
  - Emit EffectiveMessagingPlan per bus with: timestamp, environment, mode, hash of DesiredTopology, and the computed Diff (when available).
  - Expose via IMessagingDiagnostics for inspection and logging.

- RabbitMQ specifics (initial implementation)
  - DLQ: Set x-dead-letter-exchange/routing-key on primary queues when DlqSpec is present; create DLQ exchange/queue/binding pair.
  - Retry: Model retry buckets via per-delay exchanges/queues (e.g., retry.5s, retry.30s) or dead-letter backoff; publish delay via [DelaySeconds] or header.
  - Partition suffixing: When [PartitionKey] is present on a message, append a stable shard suffix to routing keys (e.g., .pN) for binding patterns; provision matching queue bindings when requested.

## Scope
Applies to all messaging providers; first delivery in RabbitMQ. Does not redesign wire formats or delivery semantics. Broker plugins and transport-specific constraints are honored by the provider.

## Consequences
- Dev/test speed with safe defaults, while prod requires intentional changes.
- Clear observability of planned vs. actual topology and applied operations.
- Repeatable deployments with DryRun for change review in CI/CD.

## Implementation notes
- Contracts live in Sora.Messaging.Core.Abstractions; provider-specific Applier/Inspector live with the provider (e.g., Sora.Messaging.RabbitMq).
- Inspector may use management APIs when available; otherwise rely on AMQP declaration pass to infer existence and arguments.
- Emit concise, stable names; reuse alias registry from MESS-0022 for routing keys and exchange names.
- Log a single-line summary per bus: mode, adds/updates/removals counts, and any blocked destructive ops.

## Follow-ups
- CLI/diagnostic endpoint to dump EffectiveMessagingPlan/Diff.
- Extend RetryBucketsSpec with policy shorthands (fixed, exponential).
- Add per-subscription safety flags to opt-in to binding expansions in ReconcileAdditive.
- Add Kafka and Redis providers to the same contracts.

## References
- MESS-0022 — MQ provisioning defaults, type aliases/attributes, and dispatcher
- MESS-0027 — Standalone MQ services and naming
- DATA-0019 — Outbox helper and defaults
- ARCH-0042 — Per-project companion docs
