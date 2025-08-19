# Architecture Decision Records (ADRs)

Index of decisions:

- 0001: Rename generic `TAggregate` → `TEntity`
- 0002: Introduce `QueryCapabilities` and `IQueryCapabilities`
- 0003: Introduce `WriteCapabilities`, `IWriteCapabilities`, and bulk marker interfaces
- 0009: Unify on `IEntity<TKey>`; remove `IAggregateRoot<TKey>`
- 0010: Introduce meta packages (`Sora`, `Sora.App`) for simplified installs
- 0011: Layering for logging (Core) and secure headers (Web); app-level policies remain in apps
- 0012: Web launch templates and rate limiter registration lives in apps
- 0013: Health announcements (push) with static one-liners; merged into readiness
- 0014: Samples port allocation scheme (reserved blocks starting at 5034)
- 0015: Default IConfiguration fallback via StartSora; host configuration takes precedence
- 0016: Entity extensions naming and parity (concise helpers; IEnumerable Save/SaveReplacing)
- 0017: Storage naming conventions (adapter defaults and overrides)
- 0018: Centralized storage naming registry and developer ergonomics
 - 0019: Outbox helper conventions and defaults
 - 0020: Outbox provider discovery and priority selection
 - 0021: Messaging capabilities and framework negotiation
 - 0022: MQ provisioning defaults, type aliases/attributes, and dispatcher
 - 0023: Alias defaults, default group, auto-subscribe, and OnMessage sugar
 - 0024: Batch semantics, handlers, and aliasing
 - 0025: Inbox contract, client behavior, and provider discovery
 - 0026: Optional discovery-over-MQ (ping/announce) policy and gating
 - 0027: Standalone MQ services and naming (Inbox service; Publisher Relay vs Outbox)
 - 0028: Service project naming and conventions (Sora.Service.*)
 - 0036: Sylin.* prefix for NuGet package IDs
 - 0037: Tiny* template family (TinyApi, TinyApp, TinyDockerApp, TinyWorker)

- 0041: GraphQL module (Sora.Web.GraphQl): controller-hosted schema from IEntity<>, typed filters/sorts, display field

- 0042: GraphQL naming & discovery policy: storage-based names, IEntity-only, collision handling

Template: see `0000-template.md`.
