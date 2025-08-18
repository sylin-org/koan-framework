# Sora.Messaging.Inbox.InMemory

In-memory Inbox implementation for Sora Messaging.

- Purpose: fast, single-process dedup for dev/tests.
- Registration: auto-discovered by `AddSora()` when the package is referenced; no explicit call needed.
- Scope: not suitable for multi-instance production deployments.

Behavior
- Tracks processed keys in a local concurrent dictionary.
- Key selection in Sora: `{bus}:{group}:{alias}:{idKey}` where `idKey` is `[IdempotencyKey]` header or `MessageId` fallback.
- On receipt: skip if processed; after success: mark processed.

Notes
- For durable scenarios, use a DB-backed Inbox microservice (see ADR-0025/0026).
