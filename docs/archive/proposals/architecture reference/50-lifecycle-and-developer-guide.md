# Lifecycle and Developer Guide

This guide describes the end‑to‑end flow, how to define contracts and policies, how to build adapters and core services, and how to run, test, and deploy the solution.

## End‑to‑end lifecycle (high level)
1) Intake: Adapters emit IntakeRecord via REST to IntakeGateway or directly to RabbitMQ “intake”.
2) Standardization: Parsers and healing maps (from PolicyBundle) produce StandardizedRecord.
3) Keying: AggregationKeys derived per keyDefinitions → KeyedRecord.
4) Association: Enforce KeyIndex invariants → update ReferenceItem, create ProjectionTask, or emit RejectionReport.
5) Projection: Build ProjectionView idempotently using ReferenceItem.Version.
6) Distribution: APIs serve ProjectionView, lineage, diagnostics; admins manage PolicyBundle and replays.
7) Observability: OTEL spans/metrics across all steps; diagnostics stored for rejects.

## Internal functionality (by step)
- IntakeGateway
  - Validates IntakeRecord schema
  - Adds correlation/causation IDs
  - Publishes to MQ “intake” exchange
- Standardizer
  - Loads active PolicyBundle (DB‑backed, file fallback)
  - Applies parsers (e.g., ISO 8601 dates, ITU E.164 phones) and healing maps
  - Emits StandardizedRecord to “standardized”
- Keying
  - Resolves keyDefinitions (ordered by priority)
  - Builds AggregationKeys as keyId:value pairs; emits KeyedRecord to “keyed”
- Association
  - Looks up KeyIndex for incoming keys
  - Invariants:
    - MULTI_OWNER_COLLISION → RejectionReport
    - SAME_KEY_MISMATCH → RejectionReport
    - NO_KEYS → RejectionReport
  - On success: update ReferenceItem, compute Version (hash), write Outbox entry; publisher enqueues ProjectionTask
- Projection
  - Consumes ProjectionTask; loads ReferenceItem by ReferenceId+Version
  - Materializes ProjectionView; idempotent on same Version
- Distribution API
  - REST endpoints: GET views/lineage/diagnostics; POST policies/replay
  - PII/Compliance interceptors (optional)

## Contracts and schemas (no codegen)
- Define JSON Schemas in repo for:
  - IntakeRecord, StandardizedRecord, KeyedRecord
  - ProjectionTask, ProjectionView
  - RejectionReport, PolicyBundle
- Validate at runtime on ingress/egress boundaries
- Add contract tests in CI that validate example payloads and fixtures

## Creating DDD contracts (neutral)
- Identify standardized elements and their selectors (JSON paths) for PolicyBundle
- Define keyDefinitions in PolicyBundle with priorities; keys become AggregationKeys
- Ensure invariants are meaningful for the domain (e.g., which keys carry identity or linkage semantics)
- Add lineage capture points for critical fields

## Between‑steps hooks (domain rules)
- Add synchronous policy rules in PolicyBundle for Post‑Standardization, Pre‑Keying, Pre‑Association, etc.
- Rules return allow/transform or reject with reasonCode and evidence; stamp policyVersion
- Keep rules deterministic and bounded in cost; no network I/O inside hooks

## Developer workflow (DX)
- Start local stack:
  - docker‑compose (Mongo replica set, RabbitMQ, workflow‑lite, OTEL, core services, sample adapters)
- Hot reload:
  - dotnet watch for services; adapters scaffolded with Koan templates
- Seed data:
  - Seed a base PolicyBundle and IntakeRecord fixtures; verify flows via OTEL/Jaeger
- Inspect and replay:
  - Use Distribution API or CLI to list diagnostics and trigger replays (pin policyVersion for determinism)

## Build and deploy
- CI
  - Build, unit/integration tests (ephemeral Mongo+RabbitMQ), schema validation, policy linting
  - Container image build, scan, SBOM
- CD
  - Deploy containers; ensure Mongo replica set and RabbitMQ are healthy
  - Feature flags: enable PII/Compliance, adjust consumer counts per queue
  - Canary/cautious rollout for policy changes; monitor diagnostics and metrics

## Observability and troubleshooting
- Trace a record across stages using correlationId (OTEL/Jaeger)
- Check queue depths and consumer lag for backpressure
- Review RejectionReports by reasonCode; replay after policy corrections

## Security, compliance, and ISO
- TLS in transit; least‑privilege Mongo/RabbitMQ users
- PII/Compliance module for masking/redaction, retention, consent checks (ISO/IEC 27001 alignment)
- Data formats per PolicyBundle align to ISO 8601, IETF BCP 47, ITU E.164, HL7 RSG as needed

## FAQ
- Q: Can I disable RabbitMQ locally?
  - A: Use the local‑slim profile; Channels can replace MQ, but default is MQ for parity.
- Q: How do I add a new adapter?
  - A: Use the Koan adapter template, map to IntakeRecord, validate schemas, and point to IntakeGateway or MQ intake.
- Q: How do I evolve keys without breaking linkage?
  - A: Add keys with lower priority; keep PolicyBundle versioned; monitor collisions and adjust rules; use replay for backfills.
