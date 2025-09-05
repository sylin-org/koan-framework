# Executive Overview

This reference architecture provides a neutral, domain‑agnostic pattern to ingest data from multiple sources, standardize and heal values, associate records using configurable keys, and publish read‑optimized projections with full lineage and diagnostics.

Why this matters
- Faster delivery: local‑first, containerized stack mirrors production (MongoDB, RabbitMQ, workflow‑lite, OTEL).
- Safer data: strict key‑ownership invariants prevent accidental cross‑linking; idempotent projections.
- Governable change: DB‑backed PolicyBundle (parsers, healing maps, key definitions, rules) with hot reload and file fallback.
- Observability by default: OpenTelemetry traces, metrics, and structured diagnostics.
- Composable compliance: optional PII/Compliance module adds masking, retention, and consent without core changes.

Where to use it
- Multi‑adapter integration: 5–10 adapters per solution; up to ~1M records per domain.
- Mixed ingestion: adapters can POST to REST or publish to MQ; both are supported.
- Eventual consistency: acceptable for canonical composites and projections; deterministic versions ensure idempotency.
- Regulated or mixed sensitivity: enable PII/Compliance when needed.

Key concepts
- IntakeRecord → StandardizedRecord → KeyedRecord → ReferenceItem → ProjectionTask → ProjectionView
- KeyIndex enforces single owner per AggregationKey.
- RejectionReport captures reasonCode, evidence, and policyVersion for non‑processable items.

Getting started
- Read the EA and EI documents to understand capabilities and integrations.
- Use the SA document to map components to containers and data models.
- Follow SE practices for testing, CI/CD, and day‑one DX.
