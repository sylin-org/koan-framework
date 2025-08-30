# Enterprise Integration (EI)

Patterns
- RabbitMQ choreography with exchanges per stage: intake, standardized, keyed, association, projection, diagnostics (fanout)
- Queue‑per‑stage; horizontal consumers; DLQ for poison messages
- Workflow‑lite orchestrator for retries/backoff/timeouts (no business logic inside)

APIs
- IntakeGateway (REST): POST IntakeRecord; schema validation; publishes to MQ
- Distribution API: ProjectionView, lineage, diagnostics; admin endpoints for policies and replay
- Security: OAuth2/JWT, mTLS/TLS; PII/Compliance interceptors when enabled

Contracts (no codegen)
- JSON Schemas: IntakeRecord, StandardizedRecord, KeyedRecord, ProjectionTask, ProjectionView, RejectionReport, PolicyBundle
- Runtime validation at boundaries; contract tests in CI

Idempotency
- Inbox/Outbox stores in Mongo (infra.Inbox/infra.Outbox)
- Deterministic messageId and correlationId propagation
- Projection idempotency via ReferenceItem.Version (deterministic hash)

Routing and replay
- Routing keys by sourceId/segment if needed; diagnostics fanout for ops tools
- Replay by reasonCode/time window; pin policyVersion to reproduce decisions

DLQ
- Consumers summarize DLQ reasons into diagnostics; support manual/automated requeue after remediation
