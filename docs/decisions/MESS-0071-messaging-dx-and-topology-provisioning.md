# ADR: Sora.Messaging Developer Experience and Topology Provisioning

## Context

Sora.Messaging aims to provide a simple, powerful, and provider-agnostic messaging abstraction for .NET. Current APIs focus on message send/receive and handler registration, with strong conventions and auto-registration. However, there is no public, agnostic API for consumers to declaratively request or provision custom MQ topologies (exchanges, queues, bindings) at runtime. There is also an opportunity to further improve developer experience (DX) with a more fluent, discoverable, and testable API surface.

## Decision

- **Expose a minimal, provider-agnostic topology API in Sora.Messaging.Core** (e.g., `ITopologyProvisioner` with `DeclareExchange`, `DeclareQueue`, `BindQueue`).
- **Ensure all providers implement this API** and that it is available to consumers (e.g., Sora.Flow) for advanced topology provisioning.
- **Retain and improve zero-config, convention-based defaults** for basic publish/subscribe scenarios.
- **Introduce a fluent, intent-driven API surface** for common messaging actions (e.g., `.Publish<T>()`, `.Subscribe<T>()`, `.OnCommand<T>()`).
- **Provide in-memory/mock bus for testing** with the same API as production.
- **Enhance documentation and samples** to focus on real-world, example-driven usage and minimal boilerplate.
- **Maintain strong typing, diagnostics, and extensibility** as core principles.

## Rationale

- **DX:** Fluent, discoverable APIs and zero-config onboarding make Sora.Messaging easy and enjoyable for all .NET developers.
- **Flexibility:** Advanced users and frameworks (like Sora.Flow) can declaratively provision and inspect topology as needed.
- **Testability:** In-memory/mock bus enables fast, reliable unit and integration tests.
- **Extensibility:** Core remains simple, with extension points for advanced scenarios.

## Consequences

- **Breaking change:** New interfaces and refactoring required in Sora.Messaging.Core and provider packages.
- **Migration:** Update documentation, samples, and dependent frameworks (e.g., Sora.Flow) to use new APIs.
- **Future-proof:** Enables new primitives, patterns, and providers without further breaking changes.

## Migration Notes

- Add `ITopologyProvisioner` and related APIs to Sora.Messaging.Core.
- Refactor providers (e.g., RabbitMQ) to implement the new interface.
- Update Sora.Flow and other consumers to use the new provisioning API for advanced topologies.
- Update documentation and samples to highlight new DX features and usage patterns.

---

**Status:**  
Accepted. Implementation and refactoring to begin immediately.

**Related:**

- `/docs/decisions/MESS-0070-messaging-topology-system-primitives-zero-config.md`
- `/docs/architecture/principles.md`
