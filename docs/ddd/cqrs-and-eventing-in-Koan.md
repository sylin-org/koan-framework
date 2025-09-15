# CQRS and Eventing in Koan

Command/Query split
- Commands mutate state through repositories; Queries read via filters/projections.
- Koan’s CQRS decorator forwards reads to the read-side and uses outbox for reliable publish.

Outbox/Inboxes
- Outbox: store domain/integration events with the write operation; a background publisher forwards to the bus.
- Inbox: ensure idempotent consumption; record processed message IDs to avoid duplicates.
- Available components: `Koan.Data.Cqrs.Outbox.Mongo`, `Koan.Inbox.Redis`, `Koan.Service.Inbox.Redis`.

Messaging
- Abstractions in `Koan.Messaging.*`; RabbitMQ via `Koan.Mq.RabbitMq`.
- Design events as integration contracts; version carefully; prefer additive changes.

Projections & Read models
- Build read-optimized views from events or transactional reads. Keep them independent from write models.

Paging & totals
- Pushdown when possible; when fallback applies, Koan emits `Koan-InMemory-Paging: true`.
- Use `await Item.Count()` or repository `CountAsync` for totals.

Observability
- Traces/metrics via `AddKoanObservability`; `Koan-Trace-Id` header for correlation; snapshot under `/.well-known/Koan/observability`.

Testing
- Command tests assert state transitions and emitted events.
- Query tests assert filters/paging behavior and pushdown vs in-memory signals.

## Terms in plain language
- CQRS: split reads and writes so each can scale/optimize independently.
- Outbox: a table/collection to store events with your write so you can publish reliably later.
- Inbox: a table/collection to remember processed messages and avoid duplicates.
- Idempotent: safe to process the same message more than once.
- Projection: a read-optimized view (like a cached summary) built from events.
