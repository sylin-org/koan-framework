# Agentic AI Developer Playbook (Koan‑first)

Purpose
- Enable a code‑assistant to build the solution from zero to completion, consistently and safely, using the agreed architecture and Koan as the adapter baseline.

Gaps in the existing docs (now addressed here)
- Missing end‑to‑end scaffolding steps with folder layout and naming
- No baseline environment variables and connection conventions
- No minimal JSON Schema stubs for core contracts
- No policy bundle seed/example and hot reload instructions
- No explicit hook interfaces and reasonCode taxonomy
- No run profiles or smoke test checklist tailored to automation

## 1) Repository layout scaffold
- adapters/
  - sample-adapter/ (Koan .NET 9; emits IntakeRecord)
- services/
  - intake-gateway/
  - standardizer/
  - keying/
  - association/
  - projection/
  - distribution-api/
- shared/
  - Contracts/ (JSON Schemas + C# DTOs)
  - Policies/ (PolicyBundle loader, seed)
  - Observability/ (OTEL setup)
  - Storage/ (Mongo repos: KeyIndex, ReferenceItem, Outbox, Inbox)
- infra/
  - docker-compose.local.yaml (Mongo replica set, RabbitMQ, OTEL, workflow)
  - dapr/ (workflow configs)
- docs/
  - reference-architecture/ (this set)

## 2) Environment and configuration (defaults)
- MongoDB
  - MONGO_URL=mongodb://mongo-replset-0:27017,mongo-replset-1:27017,mongo-replset-2:27017/?replicaSet=rs0
  - Single database name per solution; Koan Entity<> namespace defines collection names
- RabbitMQ
  - RABBITMQ_URL=amqp://guest:guest@rabbitmq:5672
  - Exchanges: intake, standardized, keyed, association, projection, diagnostics
- Workflow (Dapr)
  - DAPR_HTTP_PORT, DAPR_GRPC_PORT; workflow component refs
- OTEL
  - OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
  - OTEL_SERVICE_NAME set per service
- Policies
  - POLICY_SOURCE=db|file; POLICY_FILE=/policies/seed.json; POLICY_HOT_RELOAD=true

## 3) Core contract stubs (JSON Schemas)
- IntakeRecord: { sourceId, correlationId?, occurredAt, payload (object) }
- StandardizedRecord: { sourceId, correlationId, standardized (object), lineage[] }
- KeyedRecord: { sourceId, correlationId, standardizedRef, aggregationKeys[] }
- ProjectionTask: { referenceId, version, reason }
- ProjectionView: { referenceId, version, viewName, data }
- RejectionReport: { reasonCode, message, evidence, policyVersion, correlationId, createdAt }
- PolicyBundle: { id, version, isActive, effectiveFrom, parsers{}, healingMaps{}, keyDefinitions[], decisionRules[] }

Note: Store schemas under shared/Contracts and validate at runtime for ingress/egress.

## 4) PolicyBundle seed
- Provide a minimal seed JSON with:
  - parsers (ISO 8601 for date fields, E.164 for phones as examples)
  - healingMaps (common terms → canonical values)
  - keyDefinitions (one or two keys with priorities)
  - decisionRules (reject when no keys)
- Loader attempts DB first, falls back to file, and hot‑reloads on change.

## 5) Hook interfaces
- Synchronous hooks per step:
  - PostStandardizationHook(StandardizedRecord, context) → Allow/Transform/Reject
  - PreKeyingHook(StandardizedRecord, context) → Allow/Reject
  - PreAssociationHook(KeyedRecord, context) → Allow/Reject
  - PostAssociationHook(ReferenceItem delta, context) → Allow/Transform/Reject
  - PrePublishHook(ProjectionView, context) → Allow/Transform/Reject
- Outcomes model:
  - { decision: allow|reject|defer, reasonCode?, message?, evidence?, transforms? }
- Determinism and time budgets enforced; ruleId and policyVersion logged.

## 6) Reason code taxonomy (starter)
- NO_KEYS, MULTI_OWNER_COLLISION, SAME_KEY_MISMATCH, POLICY_REJECT, VALIDATION_ERROR, TRANSIENT_FAILURE

## 7) Build, run, and smoke test (automation‑friendly)
- Bring up infra and services using docker‑compose.local.yaml
- Post PolicyBundle seed (or mount POLICY_FILE)
- Post a minimal IntakeRecord fixture → expect StandardizedRecord → KeyedRecord
- With no collisions, expect ReferenceItem and ProjectionTask → ProjectionView
- With constructed collision (two ReferenceIds for same key), expect RejectionReport MULTI_OWNER_COLLISION
- OTEL traces present for each step; check queue depths non‑increasing

## 8) Adapter template (Koan)
- Project scaffolding:
  - Koan service with config for source polling/webhooks
  - Mapper from source payload → IntakeRecord
  - Health endpoints (/healthz, /readyz)
  - Emission via REST to IntakeGateway or AMQP publish to intake exchange
- Tests: mapping tests and contract validation against IntakeRecord schema

## 9) Association invariants (unit tests)
- Multiple owners for any key → reject
- Single owner but keyId value mismatch → reject
- No keys → reject
- Happy path merges → outbox ProjectionTask created exactly once per Version

## 10) CI guardrails
- Validate JSON Schemas and example fixtures
- Policy lint (syntax, allowed functions, time budget)
- Invariant tests and mutation tests for Association
- Container build, scan, SBOM; integration tests with ephemeral Mongo+RabbitMQ

## 11) Deployment checklists
- Mongo replica set is healthy; create indexes (KeyIndex unique, etc.)
- RabbitMQ exchanges/queues exist; DLQ bindings configured
- OTEL collector up; service env points to collector
- PolicyBundle loaded and version is visible in Distribution API

## 12) Troubleshooting plays
- Track a correlationId through Jaeger
- Inspect diagnostics by reasonCode and time
- Replay a window with pinned policyVersion
- Check Outbox backlog and consumer lag for bottlenecks

With these conventions and checklists, an agentic assistant can scaffold, configure, build, and verify the solution end‑to‑end with minimal human ambiguity while staying aligned to the reference architecture.
