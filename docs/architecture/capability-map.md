---
type: ARCHITECTURE
domain: framework
title: "Koan Capability Map"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: source-first
validation:
  date_last_tested: 2026-07-17
  status: reviewed
  scope: current packable graph, golden application path, and concern-owned capability boundaries
---

# Koan capability map

Koan is an opinionated meta-framework for agentic .NET applications. Its organizing rule is
**Reference = Intent**: add a functional package, call `AddKoan()`, and let that package contribute its
capability through the shared composition lifecycle. Contract packages remain inert.

The target experience is a direct translation from business intent to code:

```csharp
builder.Services.AddKoan();

public sealed class Todo : Entity<Todo>;
public sealed class TodosController : EntityController<Todo>;
```

Everything below should preserve that ratio: package references and small business-named declarations
add behavior; infrastructure mechanics remain inspectable framework responsibilities.

## Start here

| Intent | Reference | Meaningful result |
|---|---|---|
| console, worker, or module host | `Sylin.Koan` | Core + Entity data + local Events/Transport + bounded JSON persistence |
| web application | `Sylin.Koan.App` | the foundation plus controller-first ASP.NET Core projection |
| durable embedded data | `Sylin.Koan.Data.Connector.Sqlite` | SQLite replaces the bounded JSON default by provider election |
| project template | `Sylin.Koan.Templates` | `dotnet new` acquisition path for the supported application shapes |

`Sylin.Koan.Core` is a module-author surface. Most applications receive it through one of the entry
bundles rather than referencing it directly.

## Capability layers

| Layer | Primary packages | Responsibility |
|---|---|---|
| composition foundation | `Sylin.Koan.Core` | module discovery, options, context, provenance, health, runtime facts, service discovery, shared adapter lifecycle |
| Entity data | `Sylin.Koan.Data.Abstractions`, `Sylin.Koan.Data.Core` | Entity/repository contracts, routing, provider election, lifecycle, relationship and stream negotiation |
| Data providers | `Sylin.Koan.Data.Connector.*`, `Sylin.Koan.Data.Vector.Connector.*` | one backend choice and its capabilities, health, discovery, and limitations |
| Communication | `Sylin.Koan.Communication`, `.Connector.RabbitMq` | local-first Entity Events and Transport; optional network transport provider |
| Web projection | `Sylin.Koan.Web`, `.OpenApi`, `.Extensions`, `.Sse`, `.OpenGraph` | controllers, HTTP projection, health/well-known surfaces, and optional web behaviors |
| identity and access | `Sylin.Koan.Web.Auth*`, `Sylin.Koan.Identity*`, `Sylin.Koan.Security.Trust`, `Sylin.Koan.Tenancy*` | authentication, identity domain behavior, authorization/trust, and activated isolation contributors |
| AI and agents | `Sylin.Koan.AI*`, `Sylin.Koan.Data.AI`, `Sylin.Koan.Mcp*` | model/provider routing, agent workflows, Entity AI capabilities, and MCP projections |
| operations | `Sylin.Koan.Jobs`, `Sylin.Koan.Cache*`, `Sylin.Koan.Observability`, `Sylin.Koan.Data.Backup`, `Sylin.Koan.Storage*` | durable work, caching, telemetry, backup/restore, and object storage |
| domain capabilities | `Sylin.Koan.Canon*`, `Sylin.Koan.Media*`, `Sylin.Koan.Classification` | opt-in higher-level domain pipelines and projections |
| development tooling | `Sylin.Koan.Orchestration.*`, `Sylin.Koan.Testing*` | DevHost planning/providers/exporters and test harnesses; not application runtime defaults |

Package availability is not a support or maturity claim. Consult the [product surface](../reference/product-surface.md)
for current evidence and the package's own README for its explicit limits.

## Composition laws

### Functional packages activate; contracts do not

A functional package owns a `KoanModule` and contributes through ordinary composition. A contracts or
abstractions package supplies vocabulary to another module but has no module and performs no work.
Cross-module optional contracts live in isolated contract projects so consumers never need an `Inert`
project-reference flag or accidental functional dependency.

### Providers describe; concern owners decide

Providers declare identity, priority, capabilities, normalization, and health. The concern runtime
owns election and compiles one plan. Data, Communication, Cache, Storage, and future provider families
must not let each provider invent a second precedence policy.

`Koan.Core.Services.KoanServiceAttribute` describes service-backed providers once for runtime discovery
and optional development tooling. It lives in Core so an application connector does not depend on the
DevHost CLI contract.

### Layered capabilities are inert until their engine is active

An adapter may implement compatibility with an optional engine such as ZenGarden or a future tenancy
contributor. That code remains deterministically inactive until the corresponding functional module is
referenced and enabled. Adapters do not scan assemblies or infer activation from type availability.

### Cross-cutting guarantees contribute at concern chokepoints

Tenancy is the canonical example: when enabled, it contributes segmentation to Data, Cache,
Communication, and other affected pipelines. Each concern still owns its runtime pipeline and compiles
the contributor set for the current context. Business code sees the guarantee—tenant A cannot leak to
tenant B—not routing mechanics.

## Entity-centered developer surface

`Entity<T>` is the first-class semantic anchor. Referenced modules add discoverable extension rings so
IntelliSense and coding agents reveal the capabilities that are actually present:

```csharp
await todo.Save(ct);
await todo.Events.Raise<TodoCompleted>(ct);
await todo.Transport.Send(ct);

await Todo.Where(todo => !todo.Done).Transport.Send(ct);
```

Events express something that happened to an entity. Transport expresses delivery of an entity
snapshot. Local providers make both useful in a new process; adding a network provider changes routing
mechanics, not application intent. Runtime facts and startup reporting expose the selected mechanics
and bounded guarantees.

## Data provider choices

- `Json`: immediate bounded local persistence used by the foundation bundle; not a universal durable
  production store.
- `Sqlite`: durable embedded relational persistence.
- `InMemory`: process-local testing and bounded ephemeral work.
- `Mongo`, `Postgres`, `SqlServer`, `Cockroach`, `Couchbase`, `Redis`: separately elected backend
  providers with provider-specific capability and topology limits.
- `Vector` providers: InMemory, SQLite-Vec, Qdrant, Milvus, and Weaviate, plus search-engine vector
  integrations where explicitly supported.

Entity streaming, relationship loading, query pushdown, transactions, and schema creation are
capability-qualified. Koan rejects an unsupported guarantee before pretending to provide it.

## Web and agent projections

`EntityController<T>` is the shortest HTTP projection. `Sylin.Koan.Web.OpenApi` adds a wire-faithful OpenAPI document
and development-default UI; `Sylin.Koan.Web.Extensions` adds terse `[RestEntity]` exposure plus optional moderation,
audit, soft-delete, and capability policy. `Sylin.Koan.Web.Sse` projects controller-owned async streams through one
`Sse.Stream(...)` result model. None burden the base Web reference.

`Sylin.Koan.Mcp` projects Entity operations and the same redacted runtime facts to coding agents.
Operator HTTP facts and MCP facts consume one canonical envelope; neither should infer success from
startup prose or loaded assemblies.

## Development-host tooling boundary

`Sylin.Koan.Orchestration.Abstractions` is inert vocabulary for hosting providers, artifact exporters,
plans, and profiles. Docker, Podman, Compose rendering, CLI execution, and Aspire integration are
separate functional choices. Referencing application Core does not activate any of them.

## Choosing deliberately

Reach for Koan when the application is primarily business entities and workflows and the team wants to
add persistence, APIs, communication, AI, jobs, identity, and operations in meaningful small steps.

Do not choose a capability merely because a package exists. Avoid Koan where the application requires
an unsupported backend guarantee, demands complete manual ownership of every infrastructure seam, or
cannot accept the framework's Entity- and module-centered conventions. Corrective failures are part of
the product contract; silent fallback is not.

## Related references

- [First use](../getting-started/quickstart.md)
- [Entity access and streaming](../guides/data/entity-access-and-streaming.md)
- [Runtime facts](../engineering/runtime-facts.md)
- [Adding a connector](../engineering/adding-a-connector.md)
- [Product surface](../reference/product-surface.md)
