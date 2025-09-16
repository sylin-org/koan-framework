# Flow Messaging Architecture Guide

## Overview

Koan Flow system orchestrates entities through intake, association, keying, and projection stages. Messaging is handled via Koan.Messaging with strong-typed models, transformers, and interceptors.

## Key Components

- Messaging System: RabbitMQ provider, `services.On<T>(handler)`, `message.Send()`, MessagingInterceptors (preferred) and legacy MessagingTransformers.
- Flow Entities: `FlowEntity<T>` (aggregates/value objects), aggregation tags, canonical projections, external ID correlation.
- Queue Routing: Interceptors wrap entities into `FlowQueuedMessage` → dedicated `Koan.Flow.FlowEntity` queue.
- Transport Envelopes: `TransportEnvelope<T>` / `DynamicTransportEnvelope<T>` separate metadata from payload.

## Capability Matrix (concise)

| Area             | Capability                                | Implementation                            | Notes                                |
| ---------------- | ----------------------------------------- | ----------------------------------------- | ------------------------------------ |
| Send API         | `await entity.Send()`                     | `MessagingExtensions.Send` + interceptors | Zero-config for Flow entities        |
| Queue Routing    | Dedicated FlowEntity queue                | `FlowQueuedMessage` + `IQueuedMessage`    | Bypass type fan-out                  |
| Interception     | Type + interface interceptors             | `MessagingInterceptors`                   | Replaces string transformer registry |
| Dynamic Entities | Dictionary → DynamicFlowEntity → envelope | `Send<T>(this Dictionary<string,object>)` | For adapter raw payloads             |
| Orchestration    | Envelope processing & direct seeding      | `FlowMessagingInitializer`                | Skips intermediate FlowActions       |
| Context          | Adapter identity propagation              | `FlowContext` (AsyncLocal + fallback)     | Used for metadata stamping           |
| External IDs     | Auto correlation & indexing               | Registry + correlation policies           | ADR DATA-0070                        |

## Interceptors vs Transformers

Use interceptors for type-safe wrapping. Transformers remain for descriptor-based adaptation but should be phased out for Flow entity messaging.

## Implementation Details

- Dedicated queue: All Flow entity envelopes land on `Koan.Flow.FlowEntity` (single consumption locus; orchestrator fan-out inside Flow runtime).
- Queue provisioning: Treated like a workload queue; scaling guidance TBD (will reference backpressure metrics once implemented).
- Orchestrator pattern: `[FlowOrchestrator]` (planned hardening) discovers processing components.
- Metadata separation: Source/system kept in envelope metadata; model payload remains pure.
- Direct Seeding: `FlowMessagingInitializer` processes JSON envelope and writes intake records directly (Mongo path currently) for low-latency ingest. See ADR FLOW-0110.
- Case Sensitivity: Ensure key extraction handles camelCase vs PascalCase (see troubleshooting).

## Testing & Troubleshooting

- Use in-memory or dev RabbitMQ with interception logs enabled.
- Validate envelope JSON contains `model`, `payload`, `metadata.system`.
- See [Key Resolution Troubleshooting](../../support/troubleshooting/flow-key-resolution.md).

## Provisional Backpressure Guidance (Interim)

Until dedicated metrics ship:

- Monitor queue depth of `Koan.Flow.FlowEntity`; trigger scale-out when sustained depth > (ingest rate \* 2m).
- Track average envelope processing latency (intake → materialized) via application logs; aim < 1s p95 in dev workloads.
- If parked item ratio (`PARENT_NOT_FOUND` / total) > 5% over 10m window, investigate parent resolution lag or missing correlation policies.
- Avoid consumer over-scaling past point where Mongo (or target store) write IOPS saturate; prioritize balanced concurrency.

References: [Flow Messaging Refactor ADR](../../decisions/WEB-0060-flow-messaging-refactor.md), [External ID Correlation ADR](../../decisions/DATA-0070-external-id-correlation.md), [Direct Seeding ADR](../../decisions/FLOW-0110-flow-direct-seeding-bypass-flowactions.md).
