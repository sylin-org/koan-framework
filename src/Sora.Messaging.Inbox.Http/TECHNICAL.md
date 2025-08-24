Sora.Messaging.Inbox.Http — Technical reference

Contract
- Inputs: HTTP POST deliveries with message envelope and headers.
- Outputs: 2xx on accepted/processed, 4xx on validation/auth errors, 5xx on transient failures.
- Guarantees: at-least-once with idempotency keys; retries delegated to sender or infrastructure.

Architecture
- Controller-based inbox endpoint; validates signature/authorization and enqueues for processing.
- Uses Messaging.Abstractions/Core for message contract and handler pipeline; optional outbox/inbox patterns.

Options
- Route path, auth scheme, signature verification keys, max payload size, timeouts.

Error modes
- Duplicate deliveries (use idempotency keys), invalid signature, schema mismatch, handler failures.

Operations
- Health probe; structured logging with message id/correlation id; DLQ strategy via configured store.

References
- ./README.md
- ../Sora.Messaging.Core/TECHNICAL.md
- /docs/reference/messaging.md