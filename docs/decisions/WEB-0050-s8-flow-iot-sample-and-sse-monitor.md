---
id: WEB-0050
slug: s8-flow-iot-sample-and-sse-monitor
domain: Web
status: draft
date: 2025-08-30
title: S8 Flow IoT sample with multi-key aggregation, namespaced tags, and SSE monitor
---

## Context

We want a sample (S8) that demonstrates Koan Flow’s ingest → associate → project pipeline on an IoT scenario with multiple device kinds and multiple adapters producing different payload shapes. Devices are identified by inventory number and serial number. We also want a live UI “monitor” and a firehose view, ideally via Server‑Sent Events (SSE) as a reusable Koan module.

## Decision

- Adopt namespaced ubiquitous tags for payloads to enable clean JSON nesting and Mongo querying:
  - device.identifier.inventory, device.identifier.serial
  - device.manufacturer, device.model, device.kind, device.code
  - sensor.code, sensor.unit, sensor.reliability
  - reading.capturedAt, reading.value, reading.source
- Configure FlowOptions.AggregationTags = [device.identifier.inventory, device.identifier.serial].
- Implement two thin adapters with divergent shapes (BMS batched multi-sensor; OEM single-reading), emitting fake but consistent data for multiple devices across at least two kinds (e.g., MRI and CT), plus optional Cryo.
- Materialize domain JSON to Mongo (fallback JSON provider): DeviceDoc, SensorDoc, ReadingDoc.
- Add a reusable SSE module (Koan.Web.Sse) and expose SSE endpoints for firehose, flow events, and adapter health.
- Build a Lit-based monitor UI (device dashboard, firehose, adapter health) under wwwroot behind controllers only.

## Scope

- Sample projects: S8.Canon.Shared (constants), S8.Canon.Api (host, adapters, materializers, controllers, wwwroot).
- Module: Koan.Web.Sse with in-memory broadcaster; optional Dapr-backed broadcaster later.
- Demonstrate conflict handling: NO_KEYS, MULTI_OWNER_COLLISION, KEY_OWNER_MISMATCH via adapter toggles.

## Consequences

- Pros: Clear illustration of Flow’s provider-neutral aggregation; assets domain with multiple device kinds; real-time DX via SSE without polling; clean JSON via namespaced tags.
- Cons: SSE scaling requires sticky sessions or pub/sub; event ordering is best-effort; additional sample materializers add moving parts-kept intentionally thin.

## Implementation notes

- Avoid string queries to remain compatible with the JSON adapter; prefer statics and All()+filter.
- Use per-view sets for canonical/lineage; keep KeyIndex/ReferenceItem/ProjectionTask root-scoped.
- Coalesce SSE per (referenceId,sensor) with small debounce; bounded per-subscriber queues and keep-alives.
- Centralize route/keys constants; no magic strings in controllers or adapters.

## Follow-ups

- Add Dapr/RabbitMQ/Redis pub/sub backed SSE broadcaster for horizontal scale.
- Add CI sample smoke tests; optional Docker compost for Mongo.
- Extend UI with basic charts once stability is proven.

## References

- ARCH-0042 per-project companion docs
- WEB-0035 EntityController transformers
- DATA-0061 pagination and streaming
- AI-0002 API contracts and SSE
