---
uid: reference.modules.Koan.messaging.core
title: Koan.Messaging.Core - Technical Reference
description: Messaging runtime, dispatcher, provisioning and aliasing.
since: 0.2.x
packages: [Sylin.Koan.Messaging.Core]
source: src/Koan.Messaging.Core/
---

## Contract

- Dispatcher and router: alias-based topic/queue routing; provider plug-ins resolve aliases to transport-specific resources.
- Batching: configurable batch size/time for producers and prefetch for consumers.
- Delivery: at-least-once baseline with idempotency keys; per-partition ordering when the transport supports it.
- Headers: correlationId, causationId, messageId, idempotencyKey, partitionKey, contentType, timestamp.
- Inbox/Outbox: transactional outbox for producer reliability; inbox dedupe with bounded windows (see MESS decisions).

## Error modes

- Transient transport errors (timeouts, throttling): retried with backoff.
- Non-retryable errors (schema invalid, auth): fail fast → DLQ.
- Poison-after-retries: routed to DLQ with failure metadata and last exception class/message captured in headers.

## Retries and DLQ

- Retries: exponential backoff with jitter; caps for maxAttempts and maxBackoff; respect transport-specific retry headers where applicable.
- DLQ: dead-letter destination per alias; include headers: retryCount, lastErrorType, lastErrorMessage, failedAt, originalAlias.

## Ordering and partitioning

- Ordering is guaranteed only within a partition/shard; cross-partition ordering is not guaranteed.
- Partition keys should be stable hashes of the business key to co-locate related messages.

## Idempotency and exactly-once

- At-least-once delivery requires consumer idempotency keyed by messageId or idempotencyKey.
- Exactly-once is only achievable when both transport and storage support idempotent writes with dedupe windows; otherwise treat as at-least-once.

## Outbox and inbox

- Outbox: enqueue records within the producer’s DB transaction; a background dispatcher publishes to the broker.
- Inbox: persist processed message IDs with TTL; drop duplicates during the window.

## Operations

- Health: transport connectivity, alias provisioning, backlog depth, consumer lag.
- Metrics: delivery latency (p50/p95/p99), throughput, retry counts, DLQ rates, handler duration, batch sizes, prefetch effectiveness.
- Logs: include correlation/causation IDs; never log payload secrets; include alias and partition identifiers.

## References

- Decisions MESS-0021..0027: `/docs/decisions/`
- Engineering: `/docs/engineering/index.md`
