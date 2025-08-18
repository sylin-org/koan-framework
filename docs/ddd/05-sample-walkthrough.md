# Sample Walkthrough (S2, S3)

S2 (API + Mongo + Client)
- Aggregate: `Item` (simple example). Repository uses Mongo adapter.
- API: Sora.Web exposes CRUD + seed/clear; totals via `CountAsync`; paging flags via header.
- Client: proxies API, shows observability snapshot and `Sora-Trace-Id`.

S3 (RabbitMQ sample)
- Messaging: RabbitMQ transport; publish/consume integration events.
- Reliable processing: use outbox (writer) and inbox (consumer) to achieve at-least-once with idempotency.

What to look for
- Boundaries: API concerns separated from domain; adapter configuration per bounded context.
- Observability: OTEL enabled via options; local collector recipe available in S2.Compose.

## Terms in plain language
- Sample Stack: a minimal but realistic app to demonstrate the ideas.
- Adapter: the piece that connects Sora to a specific storage/messaging system.
- Seed/Clear: create sample data, then delete it to start over.
