# Building Messaging Queue Adapters

Implementing a messaging transport or inbox provider in Koan.

Surfaces
- Producer: IBus and IMessageBatch plumbing; advertise batching and delivery semantics.
- Consumer/inbox: IMessageInbox (pull) or webhook inbox; idempotency, deduplication window, and poison handling.
- Options: endpoint, credentials, queue/topic names via StorageNameRegistry, retry/backoff policies.

Steps
1) Package and DI
   - Create src/Koan.Messaging.<Adapter>/ (transport) or src/Koan.Messaging.Inbox.<Adapter>/. 
   - Add Add<Adapter>Messaging() / Add<Adapter>Inbox() DI extensions with options and health checks.

2) Naming
   - Resolve logical bus names and queues via StorageNameRegistry; allow overrides via attributes/options.

3) Producer path
   - Implement send/publish with correlation and causation IDs; support CreateBatch with best‑effort or atomic per transport.
   - Set capability flags (Batching, Delay/Schedule if supported). Observe CancellationToken.

4) Consumer/inbox path
   - Implement leasing/visibility timeout; at‑least‑once delivery by default; dedupe store; poison queue/escalation.
   - Webhook inbox: implement signature verification, retry, and backoff; align with HTTP status handling.

5) Observability
   - LoggerMessage events; ActivitySource spans with messaging.system, destination, operation, and message size tags.

6) Testing
   - Local dev harness with dockerized broker (Testcontainers) or in‑memory shim.
   - Contract tests: send -> receive; batch send; poison path assertions.

References
- Messaging reference: reference/messaging.md
- RabbitMQ sample: src/Koan.Messaging.RabbitMq/
- Inbox contracts: src/Koan.Messaging.Inbox.*
