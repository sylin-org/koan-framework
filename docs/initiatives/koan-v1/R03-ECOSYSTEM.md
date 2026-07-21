---
type: REFERENCE
domain: framework
title: "R03 Entity and Modularity Design Mining"
audience: [architects, maintainers, framework-authors, ai-agents]
status: current
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: reviewed
  scope: focused primary-source comparison for Entity semantics and module composition
---

# R03 Entity and modularity design mining

This is design mining, not competitive scoring. Koan should adopt, adapt, integrate, complement, or
decline an approach only when it changes a concrete Koan decision. Sources were read on 2026-07-13;
links point to project-owned or Microsoft-owned primary documentation.

## Decision summary

| Source idea | Disposition | Koan decision |
|---|---|---|
| Rails Active Record and convention over configuration | adopt | Keep direct Entity reads/writes and convention-led defaults as the shortest path. |
| Rails lifecycle and transaction callback taxonomy | adapt | Separate validation/lifecycle hooks, deferred domain events, post-commit integration, and rollback behavior. |
| ABP entity versus aggregate-root responsibility | adapt | Keep one first-class `Entity<T>` entry, but distinguish instance, set, aggregate/invariant, workflow, and control-plane semantics. |
| ABP opt-in entity facets (`IHas...`) | adapt | Capabilities should be typed opt-ins or module-contributed facets, not permanent base-class bulk. |
| ABP application-service and repository layers | complement | Offer workflow/service and repository escape hatches for complex domains; do not require them as scaffolding. |
| ABP ambient conventional unit of work | adapt | Allow a host boundary to own an inspectable unit of work; retain explicit scopes for multi-step code and expert control. |
| ABP deferred local/distributed events | adopt/adapt | Entity raises business facts; Koan dispatches at a named transaction boundary. Integration delivery is separate and outbox-aware. |
| ABP explicit module dependency graph/lifecycle | adopt | Module authors declare dependencies and ordering; applications still express intent by reference rather than registration code. |
| ABP object/module entity extension dictionary | decline by default | Preserve compile-time properties and typed facets for app-owned models; consider dynamic extension only for third-party module customization. |
| ABP automatic entity-to-DTO/UI propagation | decline | Projection is explicit because agent/API exposure is a security boundary. |
| EF Core short-lived unit-of-work ownership | adopt | Host-scoped state must replace Entity/runtime statics that leak across repeated hosts. |
| EF Core change tracking/interceptors | integrate below the contract | Providers may use them internally; Entity semantics cannot depend on one ORM lifecycle. |
| C# 14 static and instance extension members | adopt | Modules contribute narrow Entity facets in the Entity language namespace, visible only when referenced. |

## ABP: what adds strategic value

ABP and Koan optimize different common paths. ABP's documented default is a layered, DDD-oriented
architecture with entities/aggregate roots, repositories, application services, DTOs, explicit module
dependencies, and conventional units of work. Koan intentionally compresses the first useful path into
the Entity grammar. The value to Koan is therefore boundary discipline, not additional layers.

### 1. Entity identity is smaller than application workflow

ABP's [Entities](https://abp.io/docs/latest/framework/architecture/domain-driven-design/entities)
documentation gives `Entity<TKey>` identity/equality responsibility, while aggregate roots own
invariants, children, and a transaction boundary. It encourages controlled mutation through domain
methods. Its `BasicAggregateRoot` versus `AggregateRoot` distinction also prevents concurrency and
dynamic-property features from becoming unavoidable base-class weight.

Koan disposition: **adapt**.

- Keep `Entity<T>` as the single first-class entry; adding a mandatory hierarchy would spend the
  cognitive budget Koan is trying to save.
- Define aggregate/invariant behavior as an optional typed facet or marker when the distinction changes
  persistence, events, or concurrency.
- Treat a verb as an Entity member only when it is about that entity instance or entity set. A workflow
  coordinating payments, notifications, multiple aggregates, or remote systems belongs to a plain
  application workflow/handler even if Entity remains its data language.

### 2. Repositories are a useful escape hatch, not Koan's golden path

ABP's current [repository guidance](https://abp.io/docs/latest/framework/architecture/best-practices/repositories)
prefers aggregate-specific repository interfaces in application code and discourages generic
repositories and `IQueryable` there. This buys explicit domain boundaries and provider independence at
the cost of more types and indirection.

Koan disposition: **complement**.

- Preserve `Data<T,TKey>` and repository/service APIs for complex queries, bulk work, custom provider
  contracts, and teams that want a layered boundary.
- Do not require an `IThingRepository` merely to call `Thing.Get` or `thing.Save`; that would turn the
  meaningful-small-step promise into scaffolding.
- Encourage a custom repository or query object when an operation has domain vocabulary, reusable
  policy, unusual cost, or provider-specific semantics that a generic Entity verb cannot explain.

### 3. Unit of work should be conventional but never mysterious

ABP's [Unit of Work](https://abp.io/docs/latest/framework/architecture/domain-driven-design/unit-of-work)
starts ambient units around controller actions, application-service methods, and repository methods;
nested operations participate in the current unit and commit/rollback together. EF Core likewise
documents `DbContext` as a short-lived, single
[unit of work](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/).

Koan disposition: **adapt**.

- A host boundary such as one HTTP request, agent tool invocation, job attempt, or message handler may
  own a conventional unit of work when the selected providers can honor it.
- Startup/operation explanation must state whether the boundary is atomic, deferred, best-effort, or
  unsupported. Cross-provider behavior cannot be implied.
- `EntityContext.Transaction(...)` remains the explicit multi-operation form. Provider/source/cache
  overrides move to an expert control plane rather than sharing the common business vocabulary.
- Host-scoped registries and contexts are mandatory; a disposed host must not remain reachable through
  Entity statics.

### 4. Domain events, lifecycle hooks, and integration messages are different promises

ABP's [Local Event Bus](https://abp.io/docs/latest/framework/infrastructure/event-bus/local) lets an
aggregate enqueue an event that is published during persistence, and warns that non-aggregate behavior
can differ by provider. Its
[Distributed Event Bus](https://abp.io/docs/latest/framework/infrastructure/event-bus/distributed)
uses serializable transfer objects and offers transactional outbox/inbox behavior. Microsoft's
[domain-event guidance](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation)
also distinguishes in-domain facts from integration messages and favors deferred dispatch around the
transaction boundary.

Koan disposition: **adopt/adapt**.

- Lifecycle hooks validate, normalize, protect, or observe one persistence operation. They are ordered,
  host-owned, and explicit about before/after/rollback behavior.
- Domain events are business facts raised by an entity/aggregate and deferred to the unit-of-work
  boundary. They do not call a broker directly.
- Integration messages cross process boundaries and require a declared delivery contract. Reliable
  publication uses an outbox or fails the capability check; `.Send()` is never a synonym for atomic
  persistence.
- Framework projections such as cache invalidation and embedding updates are framework-owned reactions,
  observable as composition, not business events hidden in application code.

### 5. Explicit module dependencies improve explanation without adding app ceremony

ABP modules use `[DependsOn]`; the framework walks the dependency graph and initializes/shuts down
modules in order, as documented in
[Modularity](https://abp.io/docs/latest/framework/architecture/modularity/basics). Koan already has
reference-led discovery and ordering, so this is reinforcement rather than a new feature.

Koan disposition: **adopt**.

- Module authors, not application developers, own dependency/order declarations.
- A reference remains the application's intent. Koan resolves the graph and reports requirements,
  elections, defaults, failures, and shutdown ownership.
- Missing or cyclic requirements fail with a corrective message; they do not trigger silent optional
  behavior.

### 6. Dynamic entity extension is powerful but wrong for Koan's common path

ABP's module/object extension systems can add dynamic properties to depended-module entities and map
them through persistence and DTOs. ABP's current customization guidance explicitly keeps DTO exposure
opt-in because an extra property can be sensitive:
[Customizing application modules](https://abp.io/docs/latest/framework/architecture/modularity/extending/customizing-application-modules-overriding-services).

Koan disposition: **decline by default; retain as a possible integration seam**.

- Application-owned business entities should use real typed properties. That is better for reading,
  refactoring, validation, agents, schemas, and IntelliSense.
- Third-party modules may eventually need a typed metadata/extension seam, but a string/object property
  bag must not become the main module-composition mechanism.
- API, MCP, event, and UI projection remain explicit security boundaries. A persistence extension never
  implies external exposure.

## Rails: preserve the speed, tighten the promises

Rails documents Active Record as the MVC model responsible for data and business logic, with naming
and schema conventions minimizing configuration:
[Active Record Basics](https://guides.rubyonrails.org/active_record_basics.html). This is the closest
precedent for the feeling Koan wants: readable domain objects with direct persistence and a productive
default path.

Koan disposition: **adopt the grammar and convention, not dynamic ambiguity**.

- Keep `Todo.Get`, `todo.Save`, relationships, validation, and domain methods close to the model.
- Prefer compile-time generics, expressions, capability tokens, and C# documentation over runtime
  macros or method-missing behavior.
- Make conventions explainable at startup and per operation; convention is not permission for invisible
  fallback.

Rails' [callback guide](https://guides.rubyonrails.org/active_record_callbacks.html) explicitly names
before/after/around and after-commit/rollback phases, and warns against save/update side effects inside
callbacks. Koan disposition: **adapt the phase clarity**. R03 should not replicate the callback count;
it should make transaction timing and safe side effects unmistakable.

## EF Core and the .NET ecosystem: collaborate below and beside Entity

EF Core's short-lived context, change tracker, transactions, and
[interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors) are
valuable connector implementation tools. They are not the Koan application contract because Koan also
targets document, key/value, file, vector, and remote providers.

Koan disposition: **integrate below the contract**.

- Relational connectors may translate Koan semantics into EF/provider-native behavior where useful.
- Provider-native capabilities remain inspectable and accessible through explicit escape hatches.
- Koan must not leak `DbContext`, change tracking, or relational transaction assumptions into universal
  Entity promises.
- Existing .NET libraries for identity, telemetry, hosting, brokers, and agents are collaboration
  surfaces. Koan composes and explains them; it should not recreate them for ownership's sake.

## C# 14: the enabling mechanism for honest module-grown IntelliSense

Microsoft's [extension-member documentation](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/extension-methods)
confirms that C# 14 supports instance and static extension members. A disposable .NET 10 compile probe
at R03 proved a constrained module extension can make both `Todo.Semantic` and
`new Todo().SimilarToSelf()` compile; the probe was removed afterward.

Koan disposition: **adopt with a strict admission contract**.

- Module Entity extensions live in the canonical Entity language namespace already imported by a
  normal Entity model. Referencing the module assembly then changes IntelliSense without a generated
  global-using file.
- Static extension properties group type-wide behavior into facets such as `Todo.Semantic` or
  `Todo.Cache`; instance extensions remain direct only for unmistakable receiver-local verbs.
- Receiver constraints must reject invalid types at compile time. `this object` and `where T : class`
  are not acceptable Entity-language receivers.
- A module gets a small name budget and compile-consumer tests. C# 14 makes growth possible; the
  contract prevents that possibility from becoming clutter.

## Ideas deliberately not imported

- Mandatory generated application layers, DTOs, repository interfaces, or base application services.
- A framework-owned dynamic property bag for normal business entities.
- Automatic API/agent/UI exposure merely because data is persisted.
- ORM-specific tracking or transaction behavior as a universal promise.
- Feature-count parity with ABP, Rails, or any enterprise application platform.

The strategic synthesis is narrow: Rails validates Koan's direct, convention-led model; ABP supplies
responsibility and transaction boundaries; EF Core reinforces host-scoped lifetime; and C# 14 supplies
the compile-time mechanism for module-grown Entity facets. Together they sharpen Koan's own identity
rather than pulling it toward another framework's application shape.
