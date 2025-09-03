# ADR: Messaging Topology, System Primitives, and Zero-Config Developer Experience

## Context

Sora.Flow and Sora.Messaging previously required some manual configuration and entity-centric modeling for messaging primitives. System-level primitives (Command, Announcement) were not first-class, and developers often needed to configure queues, exchanges, or bindings to get the stack working. This led to friction, risk of misconfiguration, and a suboptimal developer experience.

## Decision

- **Break and rebuild Sora.Messaging and Sora.Flow messaging layers.**
- **All message bus topology (exchanges, queues, bindings) is auto-created at startup by the framework.**
- **System primitives (Command, Announcement) are top-level, first-class concepts, not tied to FlowEntity or domain models.**
- **Expose a high-level, intent-driven API for all primitives:**
  - `SendCommand`, `OnCommand`
  - `Announce`, `OnAnnouncement`
  - `PublishFlowEventToAdapters`, `OnFlowEventFromAdapters`
- **No out-of-box configuration is required by the developer.** Sane, production-safe defaults are always provided, with extension points for advanced customization.
- **Handler registration is by primitive type, not entity.**
- **All message bus details are abstracted away from the developer.**

## Rationale

- **Developer Experience:** Zero-config, intent-driven APIs reduce friction and risk, and make onboarding easy.
- **Robustness:** Auto-managed topology ensures no message loss or cross-talk due to misconfiguration.
- **Separation of Concerns:** System primitives are not polluted by domain/entity logic.
- **Extensibility:** Advanced users can customize via options/DI, but never need to for basic use.

## Consequences

- **Breaking change:** Existing code that configures topology or models system primitives as entities must be updated.
- **Migration:** Provide a migration guide and update all samples/docs.
- **Future-proof:** New primitives or event types can be added without changing user code or requiring manual config.

## Migration Notes

- Remove all manual queue/exchange setup from user code and compose files.
- Refactor Command/Announcement to use new top-level APIs.
- Register handlers by primitive type, not entity.
- Use extension points for any advanced customization.

---

**Status:**  
Accepted. Implementation to begin in next major release cycle.

**Related:**

- `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- `/docs/decisions/WEB-0035-entitycontroller-transformers.md`
- `/docs/architecture/principles.md`
