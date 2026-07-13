---
type: ARCHITECTURE
domain: data
title: "Entity Semantics Contract"
audience: [architects, developers, framework-authors, ai-agents]
status: current
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: reviewed
  scope: normative Entity language, module extension, context, event, and inspectability rules
---

# Entity Semantics Contract

`Entity<T>` is Koan's first-class application language. It is the shortest path to meaningful
business behavior and the primary IntelliSense discovery surface for capabilities whose subject is an
entity. This contract keeps that advantage without turning Entity into a catalog of everything Koan
can do.

The contract is normative for new APIs and changes to existing APIs. It does not claim the v0.17
surface already conforms. The [R03 inventory](../initiatives/koan-v1/R03-ENTITY-INVENTORY.md) identifies
current deltas, and R04 owns migration in dependency order.

## Product outcome

After adding a relevant module, a developer or coding agent should be able to start from an Entity and
discover the new business-aligned capability through IntelliSense. The application should gain a
meaningful result without registration scaffolding, while startup and operation diagnostics explain
the provider, guarantees, defaults, cost class, and failures behind the concise code.

Reading the application should reveal business state and rules. It should not require mentally
filtering repositories, service locators, provider setup, framework base services, or generated layers
out of every use case.

## The five semantic locations

Every application-facing capability has one primary location.

| Location | Owns | Typical shape |
|---|---|---|
| Entity instance | identity, state, invariants, and operations whose receiver is essential | `order.Approve()`, `await order.Save()`, `await photo.Url()` |
| Entity type/set | reads, queries, set policy, and type-wide entity capabilities | `Order.Get(id)`, `Order.Semantic.Search(...)`, `Order.Cache.Flush()` |
| Entity operation context | transaction, logical scope, typed ambient policy, cancellation, and deliberate execution override | `using var tx = EntityContext.Transaction("approve-order")` |
| Application workflow | a business use case coordinating multiple entities, people, services, or systems | `await FulfillOrder.Execute(orderId, ct)` |
| Framework/operator control plane | provider administration, migrations, backup catalogs, topology, health, elections, and diagnostics | an explicit module service/tool/CLI, never a misleading entity instance verb |

The first three form the Entity language. Workflows remain business code but are not Entity extensions
unless the receiver itself owns the rule. Control planes remain inspectable and may be entity-typed,
but they do not masquerade as ordinary domain behavior.

## Entity admission test

A new member, extension, attribute, or facet enters the Entity language only when every applicable
answer is yes:

1. **Subject:** Is the entity instance or entity type genuinely the subject of the operation?
2. **Granularity:** Does the syntax truthfully distinguish one entity, an entity set, and application
   infrastructure?
3. **Compile-time validity:** Can receiver constraints and types reject invalid use before runtime?
4. **Business readability:** Would a domain-aware reviewer understand the intent without translating
   infrastructure vocabulary?
5. **Provider honesty:** Are required semantics portable, capability-negotiated, or explicitly scoped
   to a provider?
6. **Cost honesty:** Can Koan state whether execution is bounded, pushed down, streamed, hybrid, or an
   opted-in full scan?
7. **Context safety:** Is scope/lifetime behavior correct across HTTP requests, jobs, messages, agent
   calls, parallel tests, and repeated hosts?
8. **Inspectability:** Can the same fact model explain availability, selection, defaults, degradation,
   and corrective action to humans and agents?
9. **Name budget:** Is the member more valuable than the IntelliSense and overload space it consumes?
10. **Evidence/removal:** Is there a consumer compile probe, behavior/failure evidence, ownership, and
    a removal story?

Failure does not mean the capability is unimportant. It means it belongs on a typed adjacent facet,
workflow, service, or control plane instead of the Entity surface.

## Language rules

### 1. Direct instance verbs require an essential receiver

An instance extension is appropriate when the operation acts on that instance and its identity/state
matters. `Save`, `Remove`, `Uncache`, `FindSimilar`, and a media object's `Url` are representative
shapes.

- The receiver is constrained to the narrowest semantic interface or Entity form.
- `this object`, `where T : class`, reflection-first acceptance, and unused receivers are prohibited.
- A verb may not perform a type-wide or application-wide action while appearing instance-local.
- The canonical common-path verb is documented once. Storage synonyms do not multiply merely because
  the underlying provider uses different terminology.

For the current persistence vocabulary, `Save` is the business-facing canonical instance verb and
`Remove` is the canonical deletion verb. `Upsert`, raw operations, bulk replacement, and provider
instructions remain explicit advanced/data-facade vocabulary unless a later decision demonstrates a
distinct common business meaning. The unconstrained `Delete(this object)` shape has no place in the
contract.

### 2. Type-wide behavior uses a small static facet

Adding every type-wide operation as another top-level static method will eventually make `Entity<T>`
unreadable. A module therefore contributes one or a very small number of noun facets:

```csharp
// Illustrative contract shape; individual APIs graduate through their own work items.
var related = await Article.Semantic.Search("food security", ct);
await Article.Cache.Flush(ct);
var facts = Article.Explain.Capabilities;
```

Facets make scope visible: `Article.Cache.Flush()` is about Article's cache, while a cache-cluster purge
belongs to the cache control plane. Facet names are short domain/capability nouns, not `Manager`,
`Helper`, or `Service` wrappers.

### 3. Modules extend the language; Data.Core does not predict them

Koan targets .NET 10/C# 14. Module assemblies use constrained
[static and instance extension members](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/extension-methods)
to add their facets. The declaring extension container lives in the canonical Entity language
namespace, currently `Koan.Data.Core.Model`, so a normal Entity source file already has the required
namespace in scope.

Consequences:

- referencing a module makes its Entity facet available to IntelliSense;
- removing the reference removes the facet at compile time;
- Data.Core does not expose a placeholder facet that fails later because the module is absent;
- packages do not inject broad global usings into the application;
- module control-plane APIs remain in the module's own namespace;
- two modules may not claim the same facet/member without an explicit composition decision.

A disposable .NET 10 probe compiled both a static `Todo.Semantic` extension and an instance extension
on a constrained Entity subtype. R04 must turn that proof into checked-in consumer compilation tests
before migrating public APIs.

### 4. Attributes declare policy, not hidden workflows

An Entity attribute is appropriate for stable, reviewable type/property policy such as storage naming,
indexing, caching, embedding source, tenancy scope, or projection exposure.

- The attribute is inert metadata; a named module owns interpretation.
- Startup reports whether it was applied, ignored, defaulted, or rejected.
- Persistence metadata never automatically grants REST, MCP, event, or UI exposure.
- Attributes do not hide multi-step business workflows or network calls.
- Security-sensitive projection is opt-in and visible in machine-readable inspection.

### 5. Interfaces and typed facets add semantics, not badges

An interface such as a job/media/aggregate capability is appropriate when it lets the compiler enforce
a real contract and when providers/framework code alter behavior because of it. Marker-only interfaces
that exist solely for discovery require a clear reason and analyzer/test support.

Optional concerns such as concurrency, auditing, versioning, or aggregate-root behavior should not
inflate every Entity base type. Prefer typed opt-in facets whose guarantees are inspectable.

## Context contract

`EntityContext` is the scoped carrier for one logical Entity operation flow. It is not a global service
locator.

### Context classes

- **Business/request scope:** typed module-owned values such as tenant, actor, classification, or
  correlation. Applications use named module vocabulary such as `Tenant.Use(...)`, not raw dictionary
  keys.
- **Logical data scope:** a named partition/source only when it has application meaning and stable
  semantics.
- **Unit of work:** a named transaction boundary with declared provider coverage and commit behavior.
- **Execution override:** adapter selection, cache bypass, and provider diagnostics. These are expert
  controls and are presented as overrides, not ordinary business scope.

Context is immutable and inherit-unless-overridden. Every push returns a disposable/async-disposable
scope, and invalid combinations fail immediately. Cross-thread dispatch has explicit
capture/restore/suppress behavior. Secrets and connection values never enter the context description.

### Host ownership

Entity operations resolve through the current host scope. Registries, lifecycle handlers, provider
elections, and cached services are host-owned and disposed with that host. A missing or disposed host
produces one Koan error naming the attempted Entity operation and corrective action; no stale static
service provider may remain reachable.

Static generic metadata may be process-wide only when it is immutable and independent of services,
configuration, environment, or host lifecycle.

### Transaction boundary

One Entity write is atomic to the degree declared by the selected provider. Multi-operation atomicity
uses an explicit `EntityContext.Transaction(...)` or a documented host boundary (HTTP request, agent
tool, job attempt, or message handler) that reports its unit-of-work behavior.

Koan never implies cross-provider atomicity. It negotiates it, rejects it, or names a saga/outbox/
compensation contract. Disposal, commit, rollback, and exception behavior are automated tests, not
incidental implementation.

## Lifecycle and event contract

Four mechanisms remain distinct:

| Mechanism | Purpose | Timing/ownership |
|---|---|---|
| Entity invariant/domain method | keep one entity/aggregate valid | synchronous business code on the model |
| Persistence lifecycle hook | validate, normalize, protect, or observe a load/save/remove | ordered host-owned pipeline around one operation |
| Domain event | state a business fact for in-process domain/application reactions | raised by Entity, deferred to the declared unit-of-work boundary |
| Integration message | communicate outside the process/bounded context | serializable contract with declared immediate/outbox delivery semantics |

Framework projections—cache invalidation, search indexing, embeddings, audit facts—are separately
identified framework reactions. They appear in composition/operation explanation and do not pretend to
be application-authored domain rules.

Lifecycle registration is deterministic, idempotent per owner, removable, and host-scoped. Ordering is
inspectable. Before-hooks may reject with a stable code and corrective message; after-hooks state
whether they run before commit, after commit, or on rollback. A hook must not recursively save the same
entity unless an explicit guarded contract permits it.

Domain events carry business data, not providers or services. Integration messages require a message
contract; a broad `.Send()` on every class is outside this contract.

## Relationships and execution cost

Relationship methods are entity-centered, but their convenience does not authorize silent unbounded
work.

- Provider pushdown is preferred and reported.
- A bounded in-memory fallback may be explicit when the caller supplies/accepts a limit and the result
  states its execution mode.
- An unbounded load-all-and-filter fallback fails capability negotiation by default.
- Ambiguous relationships require a typed relationship or explicit property selector rather than a
  runtime guess.
- Expansion through REST/MCP follows the same visibility, authorization, limit, and cost facts as
  in-process code.

## Backend negotiation and operation explanation

Every Entity operation can be described by a stable fact envelope containing, as applicable:

- entity type, operation, and correlation identifier;
- logical scope without secret values;
- selected source/adapter and the reason it won;
- required and available capabilities;
- execution mode: native/pushdown, streamed, hybrid, in-memory, deferred, or rejected;
- transaction/event/projection participation;
- boundedness, limits, retries, and idempotency facts;
- degraded/defaulted decisions and corrective actions.

Application code need not request this envelope on every call. The same underlying facts project to
startup reporting, structured logs/traces, health, `koan.lock.json`, tests, exceptions, MCP resources,
and reviewer tooling. Explanation is part of the operation contract, not prose assembled independently
by each surface.

## Application workflows: business code without framework scaffolding

Entity-first does not mean every use case becomes an extension. A workflow coordinating several
entities or external systems is a plain business-named class/function; Koan base classes and generated
layers are optional.

```csharp
// Illustrative boundary, not a required framework base class.
public sealed class FulfillOrder(Payments payments, Shipping shipping)
{
    public async Task Execute(string orderId, CancellationToken ct)
    {
        var order = await Order.Get(orderId, ct)
            ?? throw new OrderNotFound(orderId);

        order.AuthorizeFulfillment();          // domain invariant
        await payments.Capture(order, ct);     // external coordination
        await shipping.Schedule(order, ct);
        await order.Save(ct);                  // Entity persistence grammar
    }
}
```

Putting `CapturePaymentAndScheduleShipping()` in a general Order extension would hide dependencies and
make unrelated infrastructure look intrinsic to every Order. The separate workflow is still dense
business code; it is not scaffolding.

## Agent-facing contract

Coding agents should be able to infer valid Entity code from types and XML documentation without
searching internal registries. Runtime agents receive the same semantic model through explicit
projection:

- which Entity facets exist because of referenced modules;
- which operations are read-only, mutating, destructive, idempotent, or deferred;
- input/output schemas, limits, authorization, and dry-run support;
- selected backend/capabilities and why an operation is unavailable;
- stable error codes and a safe corrective action.

An Entity capability is not automatically exposed as an agent tool. Projection and authorization are
explicit. Agent-visible errors never leak secrets, provider credentials, internal paths, or private
data.

## Naming and overload budget

- One canonical common-path verb per intent.
- A direct member beats an extension when the behavior belongs permanently to Data.Core.
- A facet beats multiple module-specific top-level static verbs.
- Advanced options use an options object/builder after the simple overload count becomes ambiguous.
- Cancellation is consistently last; async methods follow one naming policy per public layer.
- XML documentation begins with the business outcome, then scope/cost, prerequisites, failures, and
  inspection link.
- Obsolete aliases name the replacement and removal condition; they do not survive indefinitely as
  undocumented convenience.

## Consumer verification

Every module contributing Entity language must add compilation and runtime evidence for these cells:

1. base Entity only: the module facet is absent;
2. module referenced: the facet is present with the normal Entity namespace and no registration code;
3. invalid receiver: compilation fails;
4. all supported modules referenced: no ambiguous member/facet collision;
5. missing runtime prerequisite: startup or invocation fails with a Koan corrective error;
6. module removed: compilation/configuration/lockfile drift is understandable;
7. repeated hosts: no registration or service-provider residue crosses host boundaries;
8. provider capability absent: fail-closed or explicitly opted-in fallback behavior is asserted;
9. XML documentation and agent schema describe the same effects and limits.

## Compatibility and migration

This contract does not authorize abrupt removal of pre-1.0 APIs. R04 will classify each inventory row:

- keep and harden in place;
- move behind a facet with a compatibility forwarding period;
- move to workflow/control plane;
- strongly type and deprecate a broad receiver;
- remove immediately only when behavior is false-success, unsafe, or explicitly unshipped.

Migration order begins with false-success and host-lifetime hazards, then broad receivers and missing
capability checks, then facet/naming cleanup. The golden V0-to-V1 proof becomes the release gate for
the resulting language.

## Proposal checklist

Before approving a new Entity capability, record:

- business sentence and meaningful result;
- semantic location and receiver granularity;
- module/package and namespace;
- direct member, instance extension, static facet, attribute, or interface choice;
- provider/cost/transaction/event contract;
- startup and per-operation explanation;
- agent/API projection boundary;
- compile/runtime evidence and unsupported scenarios;
- compatibility, module-removal, and deprecation plan.

If those answers are longer than the business outcome because the capability is mainly infrastructure,
it likely belongs off Entity.
