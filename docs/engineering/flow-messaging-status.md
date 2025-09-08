# Flow Messaging Status & Remaining Work

## Status as of 2025-01-07

### Completed Components
- External ID Correlation: Source entity ID extraction, auto-population, policy-driven detection, identity link indexing.
- ParentKey Resolution: Cross-system parent lookup, entity parking, validation.
- Flow Messaging Core: MessagingInterceptors, FlowContext, TransportEnvelope, entity.Send() pattern, direct MongoDB integration.

### Remaining Work
- Queue binding documentation & topology example for dedicated FlowEntity queue (with scaling/backpressure guidance).
- ParentKey canonical ULID resolution in projections.
- Orchestrator discovery hardening (attribute-based + convention fallback).
- Backpressure metrics & lag thresholds for dedicated queue.
- Replay tooling for direct seeded envelopes.

### Recently Closed Gaps
- IQueuedMessage interface implemented and used by FlowQueuedMessage for dedicated queue routing.

See also: [External ID Correlation ADR](../decisions/DATA-0070-external-id-correlation.md), [Flow Messaging Refactor ADR](../decisions/WEB-0060-flow-messaging-refactor.md), [Direct Seeding ADR](../decisions/FLOW-0110-flow-direct-seeding-bypass-flowactions.md).
