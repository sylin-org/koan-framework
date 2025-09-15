# Bounded Contexts and Modules

Treat each bounded context as a cohesive Koan module with clear interfaces.

Principles
- Independent model + language per context. Avoid bleeding types across boundaries.
- Explicit contracts at seams: HTTP APIs (Koan.Web), messages (Koan.Messaging), or ACLs.
- Autonomy: each context chooses its storage adapter (Mongo/Relational/Json/etc.).

Composition with Koan
- Projects/namespaces: one module per bounded context (e.g., `Shipping`, `Billing`).
- Data adapters: pick the right adapter per context; configure via `ConnectionStrings` and adapter-specific options.
- Messaging: connect via `Koan.Messaging.*` or the `Koan.Mq.RabbitMq` transport for integration events.
- Web/API: expose use-cases via Koan.Web controllers or thin application services.

Context maps (integration patterns)
- Customer ↔ Billing: publish Integration Events from `Customer` outbox; `Billing` consumes via inbox.
- Shipping ↔ Inventory: `Shipping` ACL calls Inventory HTTP API; Inventory publishes stock-level events.

Boundaries checklist
- Does each context have a clear model and ubiquitous language?
- Are external schemas isolated behind an ACL?
- Are transactions local to a single aggregate? Cross-context flows are coordinated asynchronously.
- Is storage tailored to the context (document vs relational)?

## Terms in plain language
- Bounded Context: a named boundary with a specific meaning for terms.
- Context Map: a diagram/list of how contexts connect (events, HTTP, files).
- ACL: translation code that protects your model from an external one.
- Integration Event: a message sent to other contexts when something changes.
