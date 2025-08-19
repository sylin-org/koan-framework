# 0025 — Inbox contract, client behavior, and provider discovery

Status: Accepted

Context
- Sora supports consumer-side idempotency via an Inbox. We need a portable contract so providers (Redis/SQL/Mongo/microservice) remain pluggable.
- The Sora client should follow a minimal, robust flow without hard dependencies on any specific store.

Decision
- Introduce a minimal SDK contract (IInboxStore) for client-side integration, plus a standardized microservice wire protocol for external providers.

Client SDK contract
- IInboxStore (in Sora.Messaging.Core)
  - Task<bool> IsProcessedAsync(string key)
  - Task MarkProcessedAsync(string key)
- Behavior in RabbitMQ consumer
  - Compute key: {bus}:{group}:{alias}:{idKey} where idKey = [IdempotencyKey] header or MessageId fallback
  - Before handling: if IsProcessedAsync(key) → ack/skip
  - After success: MarkProcessedAsync(key)
  - On failure: do not mark; retries proceed via MQ policy

External microservice contract (v1)
- POST /v1/inbox/try-begin { key, owner?, leaseSeconds? } → { status: Acquired|Processed|Busy, leaseId?, expiresAt? }
- POST /v1/inbox/mark-processed { key, leaseId }
- POST /v1/inbox/release { key, leaseId }
- GET /v1/inbox/{key} → { status, attempts, firstSeen, lastSeen, leaseUntil? }
- Notes
  - Use 200 responses with status enums, avoid overloading HTTP codes
  - Announce auth hints (none/bearer), never secrets
  - Retention via TTL (Redis) or cleanup jobs (SQL/Mongo)

Keying & retention
- Include bus, group, alias to avoid cross-tenant collisions.
- Retain processed keys for 7–30 days (configurable).
- Lease length 30–120s (implementation-specific for external providers).

Discovery & selection
- Library continues to use SDK interfaces.
- For external microservices, selection is by explicit config; optional MQ discovery detailed in ADR-0026.

Consequences
- Inbox providers remain interchangeable.
- Minimal client behavior avoids double-processing and extra coupling.
- External microservices get a clear, versioned API to implement.
