# Ubiquitous Language (Sora ↔ DDD)

Use this shared dictionary to keep conversations and code aligned.

Core DDD terms → Sora mappings
- Aggregate, Aggregate Root — A consistency boundary that owns invariants. Map to a single Entity type stored via a Repository; expose via Sora.Web controllers or app services.
- Entity — An identity-bearing object that changes over time. Map to `TEntity : IEntity<TKey>` in Sora.Data; persisted by an adapter (Mongo, Relational, Json, etc.).
- Value Object — Identity-free object defined by value. Model inside your entity types; persist as embedded docs/columns.
- Repository — Collection-like persistence facade per aggregate. In Sora, use repositories via the `Data<TEntity, TKey>` facade or explicit repository interfaces.
- Domain Event — An event emitted by your domain (inside a transaction). Persist via outbox when integrated with other contexts. In Sora, outbox/inbox components support reliable publish/consume.
- Command — Request to change state. Implement as application-service methods or controller actions that call repositories and domain logic.
- Query — Request to read state. Use repositories’ query/filter APIs; prefer pushdown to storage; Sora emits `Sora-InMemory-Paging` header when slicing locally.
- Bounded Context — A semantic boundary with its own model. Map to a Sora module (projects/namespaces), with its own adapters and messaging bindings.
- Context Map — How bounded contexts integrate. Use messaging (integration events), HTTP APIs, and ACLs.
- Anti-Corruption Layer (ACL) — Translation layer to keep your model clean. Implement at edges (web DTOs, messaging contracts) to decouple external schemas.
- Projection / Read Model — Optimized read model for queries. Implement with dedicated sets/collections/tables; populate via events or background processors.
- Policy / Saga (Process Manager) — Long-running coordination reacting to events. Implement with messaging handlers and application services; ensure idempotency.

Sora specifics
- Count ergonomics — Prefer `await Item.Count()` or repository `CountAsync` for totals; see ADR 0032 for paging behavior.
- Observability — Sora adds `Sora-Trace-Id` headers and an observability snapshot endpoint; see ADR 0033.
- Capabilities — Adapters advertise capabilities (e.g., paging pushdown). Fallbacks are explicit and observable.

## Terms in plain language
- Ubiquitous Language: one shared vocabulary across code and conversations.
- Entity: a thing with an ID that can change over time.
- Value Object: a small object defined only by its data (no identity).
- Aggregate Root: the main entity that controls a group (aggregate) and its rules.
- Repository: a gateway to store/load aggregates without knowing the database details.
- Domain Event: a record that “X happened” in the domain, often used to trigger reactions.
- Command vs Query: change something vs read something.
