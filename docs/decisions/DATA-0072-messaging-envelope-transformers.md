# Decision Record: DATA-0072-messaging-envelope-transformers

## Title
Sora Messaging Envelope and Transformer Architecture

## Status
Accepted

## Context
Sora.Messaging previously required clients to construct transport envelopes and inject metadata manually. This led to duplicated logic, tight coupling, and inconsistent onboarding. Sora.Flow and other domain modules need a unified, metadata-rich transport pattern, but messaging should remain agnostic to domain-specific envelopes and types.

## Decision
- **Envelope Ownership:**
  - `TransportEnvelope` is a domain concern (e.g., Sora.Flow), not a messaging concern.
  - Only domain modules construct envelopes and inject metadata.
- **Messaging Transformers:**
  - Sora.Messaging maintains a transformer/interceptor registry keyed by type, open generic, or interface.
  - On `.Send()`, Sora.Messaging inspects the object's type and interfaces, matches against registered interceptors, and applies the first/best match before sending.
  - Type/interface resolution is cached for performance; types do not change at runtime.
  - Sora.Messaging is agnostic to domain types and envelope structure.
- **Client Usage:**
  - Clients/adapters use standard `.Send(ct)` semantics only.
  - No envelope construction or metadata injection in client code.
- **FlowAdapter Metadata:**
  - Sora.Flow registers an interceptor for its types/interfaces that wraps payloads in a `TransportEnvelope` and injects `[FlowAdapter]` metadata.
- **Onboarding Handlers:**
  - Arrival handlers in Sora.Flow inspect envelope metadata and route accordingly.
  - Both `FlowEntity` and `DynamicFlowEntity` are handled via the same onboarding pipeline, differentiated only by envelope metadata.
- **Legacy Removal:**
  - Remove all legacy envelope construction from samples/adapters.
  - Centralize magic values and constants.
- **Testing & Documentation:**
  - All samples/adapters use `.Send(ct)` only.
  - Tests validate interceptor invocation and onboarding routing.
  - Documentation updated to reflect envelope structure, interceptor registration, and onboarding handler routing.

## Consequences
- Messaging is decoupled from domain envelope logic and types, improving maintainability and extensibility.
- Domain modules have full control over envelope structure, metadata, and registration conventions.
- Unified onboarding and routing based on envelope metadata.
- Reduced duplication and legacy complexity.
- Type/interface resolution is cached for performance, eliminating runtime overhead.

## References
- DATA-0061-data-access-pagination-and-streaming.md
- ARCH-0040-config-and-constants-naming.md
- WEB-0035-entitycontroller-transformers.md
