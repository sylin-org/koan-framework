# Solution Architecture (SA)

Neutral vocabulary (collections and flow)
- IntakeRecord → StandardizedRecord → KeyedRecord → Association (KeyIndex + ReferenceItem) → ProjectionTask → ProjectionView
- RejectionReport on any failure path (reasonCode, message, evidence, policyVersion)

Containers and responsibilities
- adapters/* (Koan, .NET 9): publish IntakeRecord via REST/MQ
- core/intake-gateway: validate, publish to intake exchange
- core/standardizer: PolicyBundle parsers/healing → StandardizedRecord
- core/keying: derive AggregationKeys → KeyedRecord
- core/association: enforce KeyIndex invariants, update ReferenceItem, create ProjectionTask; emit diagnostics on reject
- core/projection: build ProjectionView idempotently using ReferenceItem.Version
- core/distribution-api: serve views, lineage, diagnostics; policy admin and replay
- infra/rabbitmq: stage exchanges/queues + DLQs
- infra/mongodb: single instance; Koan Entity<> namespace = collection name
- infra/workflow-lite: Dapr Workflow (recommended)
- infra/otel-collector: tracing/metrics; optional Jaeger/Tempo, Prom/Grafana

Mongo model (Koan Entity<> namespaces)
- intake.IntakeRecord
- standardization.StandardizedRecord
- keying.KeyedRecord
- association.KeyIndex (unique AggregationKey; index ReferenceId)
- association.ReferenceItem (ReferenceId, Version; RequiresProjection)
- projection.ProjectionTask, projection.ProjectionView.<viewName>
- diagnostics.RejectionReport
- policies.PolicyBundle
- infra.Outbox, infra.Inbox

Indexes
- KeyIndex: unique(AggregationKey); idx(ReferenceId)
- ReferenceItem: idx(ReferenceId, Version); partial(RequiresProjection=true)
- RejectionReport: idx(reasonCode, createdAt)
- Outbox/Inbox: idx(messageId unique)

Invariants (collision rules)
- MULTI_OWNER_COLLISION: keys map to multiple ReferenceIds
- SAME_KEY_MISMATCH: same keyId, conflicting value for the selected ReferenceId
- NO_KEYS: no AggregationKeys present

Between-steps hooks
- Synchronous, in‑process policy hooks at Post‑Standardization, Pre‑Keying, Pre‑Association, Post‑Association, Pre‑Publish
- Outcomes: Allow (optional transform), Reject(reasonCode, evidence), Defer/Retry (rare)
- Deterministic, bounded cost, observable via OTEL; versioned with ruleId, policyVersion

Observability (OTEL)
- Spans for each step; correlation/causation IDs; metrics for throughput, lag, rejects, projection latency, E2E p50/p95
- PII‑safe logs with redaction when enabled

Run profiles
- local‑full (default): all services + RabbitMQ + MongoDB (replica set) + workflow + OTEL
- local‑slim: core collapsed; MQ still on
- cloud: same topology; scale per queue
