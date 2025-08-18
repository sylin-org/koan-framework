# 0020: Outbox provider discovery and priority selection

Date: 2025-08-17

Status: Accepted

## Context

We want switching to a durable outbox (e.g., Mongo) to be as simple as adding a package reference, mirroring how data adapters are discovered and selected by priority.

## Decision

- Introduce `IOutboxStoreFactory` with `[ProviderPriority]`.
- Register a default `InMemoryOutboxFactory` (priority 0).
- Each provider package contributes its own factory (e.g., Mongo at priority 20) via module initializer.
- Resolve the active outbox at runtime via `OutboxStoreSelector`, which picks the highest-priority factory (then stable type-name tie-break).

## Consequences

- DX: “just reference the package” switches the outbox provider.
- Consistency with data adapter selection.
- Clear override path: add or replace factories, or register `IOutboxStore` directly.

## Migration

- Durable outbox packages should provide a factory and (optionally) a convenience `AddXyzOutbox()` registration method.
- Apps can remove manual `IOutboxStore` registrations and rely on discovery.
