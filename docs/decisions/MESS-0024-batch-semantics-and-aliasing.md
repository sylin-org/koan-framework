---
id: MESS-0024
slug: MESS-0024-batch-semantics-and-aliasing
domain: MESS
status: Accepted
title: Batch semantics, handlers, and aliasing
---

# 0024 - Batch semantics, handlers, and aliasing

Context

- We want simple, explicit grouped processing for large/paginated payloads.
- Per-item streaming (IEnumerable<T>.Send()) must remain predictable.
- Topics/aliases should self-describe grouped messages.

Decision

- Introduce a generic Batch<T> message shape.
- Producer helpers:
  - SendBatch/SendBatchTo and SendAsBatch/SendAsBatchTo to send one grouped Batch<T>.
- Consumer helpers:
  - OnBatch<T>(...) is the primary grouped handler.
  - OnMessages<T>(...) is a thin alias to OnBatch<T> that passes IReadOnlyList<T> (ergonomic option).
- Alias scheme for grouped messages:
  - Batch<T> resolves to alias: "batch:{alias(T)}".
  - alias(T) follows existing rules: [Message(Alias)] if present; otherwise full type name.
  - Consumers resolve "batch:{alias(T)}" back to Batch<T>.
- Streaming stays explicit:
  - IEnumerable<object>/List<object>.Send() publishes each element as an individual message; it does not invoke batch handlers.

Consequences

- Operational clarity: batch messages are atomic at the message level (DLQ/retry as a unit), while streaming isolates failures per item.
- Cognitive clarity: the presence of "batch:" in the alias indicates grouped payloads.
- Users choose chunk sizes and idempotency patterns appropriate to their broker limits and processing needs.

Notes

- RabbitMQ provider uses the alias registry, so routing keys include the batch: prefix (subject to any normalization rules applied by the transport).
- Mixed-type batches are not supported (Batch<T> requires a single T).
