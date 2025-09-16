---
id: MESS-0022
slug: MESS-0022-mq-provisioning-aliases-and-dispatcher
domain: MESS
status: Accepted
date: 2025-08-17
title: MQ provisioning defaults, type aliases/attributes, and dispatcher
---
 
# 0022 — MQ provisioning defaults, type aliases/attributes, and dispatcher

## Context
We introduced messaging with capability negotiation and a RabbitMQ provider. We need:
- Safe provisioning defaults (like relational schema creation) with production guardrails.
- A simple type alias mechanism with message attributes for DX.
- A minimal dispatcher to invoke typed handlers.

## Decision
- Provisioning defaults:
  - If `ProvisionOnStart` is absent, default to `true` except when the environment is Production (`ASPNETCORE_ENVIRONMENT`/`DOTNET_ENVIRONMENT`).
  - In Production, default to `false` unless `Koan:AllowMagicInProduction=true`.
  - Explicit `ProvisionOnStart` always wins.
- Type aliases and attributes:
  - Add `[Message(Alias, Version, Bus, Group)]` and property-level `[PartitionKey]`, `[Header(name)]`, `[Sensitive]`, `[DelaySeconds]`, `[IdempotencyKey]`.
  - Provide a default `ITypeAliasRegistry` that maps types to aliases and resolves alias to Type.
- Dispatcher:
  - RabbitMQ provider binds configured subscriptions and consumes messages.
  - Resolves message type via alias, deserializes JSON, and invokes `IMessageHandler<T>.HandleAsync`.
  - Supports basic per-subscription `Concurrency` (parallel consumers per queue).
  - Publishes promote `[Header]` properties to transport headers and suffix routing keys with a stable partition shard when `[PartitionKey]` is present (e.g., `.pN`).
  - On handler failure: first attempt requeues; subsequent redeliveries route to DLQ when enabled (using broker redelivery semantics). Otherwise, we requeue.

## Consequences
- Dev/test friction is low with provisioning on by default; production safety preserved.
- Handlers are easy to wire via DI; routing keys can use stable aliases.
- Next steps: surface EffectiveMessagingPlan diagnostics, per-subscription concurrency/retry settings, and a unified consumer host for multiple providers.
 - Implemented now: an `IMessagingDiagnostics` service records the EffectiveMessagingPlan per bus for inspection.
