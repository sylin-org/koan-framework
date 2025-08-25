---
uid: reference.modules.sora.messaging.abstractions
title: Sora.Messaging.Abstractions — Technical Reference
description: Core contracts for messaging, inbox/outbox, and dispatch.
since: 0.2.x
packages: [Sylin.Sora.Messaging.Abstractions]
source: src/Sora.Messaging.Abstractions/
---

## Contract
- Messaging capabilities negotiation, message contracts, and inbox/outbox primitives.
- Headers: correlation/causation IDs, idempotency keys, partition keys.
- Message shapes: value + headers + metadata; versioning guidance.

## Delivery semantics
- At-least-once baseline; exactly-once only when transport and storage allow.
- Idempotency: consumers should de-dupe using keys and persistence windows.

## Security
- Authentication/authorization is transport-specific; encrypt sensitive fields at rest where supported.
- Redact secrets in logs; avoid PII in headers.

## References
- MESS-0021..0027: `/docs/decisions/` MESS series
