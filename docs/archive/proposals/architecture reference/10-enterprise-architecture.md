# Enterprise Architecture (EA)

Objectives
- Domain‑agnostic ingestion, standardization, association, projection, and distribution
- Deterministic invariants; safe eventual consistency; replay and diagnostics
- Local‑first with production parity; optional PII/Compliance

Decisions and constraints
- Primary store: MongoDB (single instance); Koan Entity<> namespace defines collection names
- Messaging: RabbitMQ default in dev; queues per stage; DLQ patterns
- Policies: DB‑backed PolicyBundle with file fallback; hot reload; versioned
- Orchestration: workflow‑lite (e.g., Dapr Workflow)
- Observability: OpenTelemetry across all services

Capabilities
- Adapters (Koan, .NET 9) → IntakeRecord (REST/MQ)
- Standardization (parsing, healing) → StandardizedRecord
- Keying (derive AggregationKeys) → KeyedRecord
- Association (KeyIndex invariants) → ReferenceItem and ProjectionTask; Diagnostics on rejects
- Projection (idempotent by version) → ProjectionView
- Distribution API (views, lineage, diagnostics, policies)

Standards and ISO
- Architecture documentation: ISO/IEC/IEEE 42010 (stakeholders, concerns, views)
- Security: ISO/IEC 27001 controls (encryption at rest/in transit, least privilege, audit)
- Data quality: ISO 8000 (standardization/healing; diagnostics)
- Product quality: ISO/IEC 25010 (reliability, maintainability, performance, security)
- Data formats via PolicyBundle: ISO 8601, IETF BCP 47, ITU E.164, HL7 RSG (as applicable)

Compliance posture
- PII/Compliance is optional: masking/redaction, retention, consent checks
- Lineage and diagnostics provide full auditability without exposing secrets

Risks and mitigations
- Policy drift → Central DB‑backed PolicyBundle, version stamping, CI checks, hot reload
- Rule sprawl → ReasonCode taxonomy, governance, linting, testing and performance budgets
- Message duplication → Inbox/Outbox, idempotent handlers, deterministic versions
- Mongo multi‑document consistency → Prefer single‑write designs; transactions for critical coupling; Outbox with writes
