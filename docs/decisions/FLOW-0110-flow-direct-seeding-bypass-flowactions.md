# FLOW-0110: Direct Seeding of Flow Transport Envelopes (Bypass FlowActions)

Status: Proposed

## Context

Previous Flow ingestion used `FlowActions` to enqueue and process staged actions before entity materialization. The new messaging refactor introduces a low-latency path: `FlowMessagingInitializer` directly consumes transport envelope JSON and seeds intake records, skipping the intermediate FlowActions queue.

## Problem

- Extra indirection (FlowActions) added latency and complexity.
- Debugging required tracing two message forms (action + entity).
- Backpressure handling duplicated logic present in materialization pipeline.

## Decision

Adopt direct seeding for transport envelopes produced by Flow entity interception:

1. Interceptor wraps entity → JSON `TransportEnvelope<T>`.
2. Envelope routed to dedicated `Koan.Flow.FlowEntity` queue.
3. `FlowMessagingInitializer` identifies envelope type and writes intake record directly.
4. Downstream association/materialization unchanged.

## Rationale

- Lower latency (single queue hop instead of two).
- Simpler diagnostics (one correlation surface).
- Eliminates duplicate transformation of payload.
- Queue specialization enables clearer scaling policy.

## Consequences

Positive:

- Reduced code paths; easier to reason about ingest.
- Lower broker utilization.
- Cleaner debugging (single envelope artifact).

Negative / Mitigations:

- Loss of FlowActions replay hook → Provide explicit replay tool (future work).
- Any orchestration logic embedded in FlowActions must be migrated (tracked in status doc).

## Alternatives Considered

- Keep FlowActions as optional layer (complex option matrix, rejected).
- Replace with outbox pattern per adapter (adds persistence overhead, not required now).

## Open Items

- Backpressure metrics on dedicated queue.
- Replay/reprocess command for parked or archived envelopes.
- Envelope schema version negotiation for future breaking changes.

## References

- WEB-0060 Flow Messaging Refactor
- DATA-0070 External ID Correlation
- flow-messaging-status.md (remaining work)
