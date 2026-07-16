---
type: ARCHITECTURE
domain: data
title: "Entity Semantics Contract"
audience: [architects, developers, framework-authors, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.19.0
validation:
  date_last_tested: 2026-07-16
  status: reviewed
  scope: normative Entity language, capability lifting, context, Lifecycle, Events, Transport, and inspectability rules
---

# Entity Semantics Contract

`Entity<T>` is Koan's first-class application language. It is the shortest path to meaningful
business behavior and the primary IntelliSense discovery surface for capabilities whose subject is an
entity. This contract keeps that advantage without turning Entity into a catalog of everything Koan
can do.

The contract is normative for new APIs and changes to existing APIs. It does not claim the v0.17
surface already conforms. The [R03 inventory](../initiatives/koan-v1/R03-ENTITY-INVENTORY.md) identifies
the original deltas. [ARCH-0113](../decisions/ARCH-0113-entity-capability-communication.md) supersedes
the earlier lifecycle/event/messaging split and records the greenfield rebuild order.

R07-14 applies the same lifting law to Jobs without pretending Jobs is Communication: scalar
`entity.Job.Submit()` and direct finite/async source `Submit()` converge only at the Jobs-owned ledger
acceptance chokepoint. The source terminal captures context once, preserves order and multiplicity,
uses bounded backpressure, and returns a fixed-size accepted-prefix summary. The static `.Jobs` facet
remains the type-level control plane. No collection atomicity, handler completion, or bounded ledger
growth is implied.

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
matters. `Save`, `Remove`, `entity.Cache.Evict`, `FindSimilar`, and a media object's `Url` are representative
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
Article.Lifecycle.BeforeUpsert(...);
var facts = Article.Explain.Capabilities;
```

Facets make scope visible: `Article.Cache.Flush()` is about Article's cache, while a cache-cluster purge
belongs to the cache control plane. Facet names are short domain/capability nouns, not `Manager`,
`Helper`, or `Service` wrappers.

Static facet syntax never licenses a process-global mutable registry. Communication receiver behavior
uses stable business-named typed handlers discovered by the host. A future Entity-shaped composition
facade is admitted only if it is genuinely host-bound and replaces rather than duplicates that path.

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

R04's checked-in .NET 10 consumer suite proves static and instance extension members on constrained
Entity subtypes, module absence/presence/removal, invalid receivers, and collision behavior. Every new
facet must extend those cells for its own scalar, type, set, and stream forms.

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

Core owns the immutable typed ambient carrier for one logical Koan flow through
`Koan.Core.Context.KoanContext`; applications normally use module vocabulary such as
`Tenant.Use(...)` rather than raw context access. The ambient state is deliberately logical-flow scoped; an explicit outer scope may
span calls into more than one host. `EntityContext` remains the Entity/Data-facing facade for source,
adapter, partition, and transaction intent because that is its semantic role, not for compatibility.
It is not the owner of every cross-cutting concern, exposes no generic module-slice bag, and is never a
global service locator.

### Context classes

- **Business/request scope:** typed module-owned values such as tenant, actor, classification, or
  correlation.
- **Logical data scope:** a named partition/source only when it has application meaning and stable
  semantics.
- **Unit of work:** a named transaction boundary with declared provider coverage and commit behavior.
- **Execution override:** adapter selection, cache bypass, and provider diagnostics. These remain
  expert controls rather than ordinary business scope.

Context is immutable and inherit-unless-overridden. Every push returns a disposable or
async-disposable scope, and invalid combinations fail immediately. Secrets and connection values never
enter the portable context or its description.

Each module owns capture, validation, versioning, restore, and suppression of its durable slice. The
Core registry transports opaque values and never names tenant or another axis. An absent registered
axis means explicit suppression and is valid; an unknown axis, malformed encoding, or unsupported
version fails before application code. Opaque syntax validation does not prove integrity. A
security-sensitive axis crossing a process boundary requires authenticated adapter provenance or the
route/ingress fails closed. Each carrier declares its generic ingress-trust requirement, allowing the
router to compare it with adapter capabilities without learning the axis meaning. Restore spans the
complete handler and disposal runs in reverse order. The host-owned registry's `Descriptors` surface
exposes only ordinal axis identity and minimum ingress trust; values remain opaque. Shared runtime-fact
and startup projection of those descriptors is a later Communication concern.

### Host ownership

Entity operations resolve through the current host scope. Registries, lifecycle handlers,
subscriptions, receiver groups, provider elections, and cached services are host-owned and disposed
with that host. A missing or disposed host produces one Koan error naming the attempted Entity
operation and corrective action; no stale static service provider may remain reachable.

Host isolation does not redefine ambient context as host-local. A host cannot mutate or dispose another
host's registrations, services, or drain state, and host disposal cannot clear a caller-owned outer
context scope. Durable capture carries module values into the receiving host's target flow; it never
carries host services.

An Entity operation may resolve implicitly only when one live host is eligible. ASP.NET requests,
Jobs, and Communication handlers establish an ambient host scope automatically. Multiple-host tests or
applications establish it deliberately; Koan never chooses a process-static winner.

Static generic metadata may be process-wide only when it is immutable and independent of services,
configuration, environment, or host lifecycle.

### Transaction and operation boundaries

One Entity write is atomic to the degree declared by the selected provider. Multi-operation atomicity
uses an explicit Data transaction or documented host boundary and never implies cross-provider
atomicity.

Events and Transport do not inspect a Data transaction coordinator. The base contract treats their
acceptance independently from persistence. A later Save-plus-signal guarantee must use one neutral
Core operation-completion seam in which Data and Communication enlist independently. Process
after-commit, durable outbox, inbox, saga, and compensation guarantees are named and negotiated rather
than inferred from a concise API.

## Lifecycle, Events, and Transport contract

Five mechanisms remain distinct:

| Mechanism | Purpose | Owner |
|---|---|---|
| Entity invariant/domain method | keep one Entity or aggregate valid | synchronous business code on the model |
| Persistence Lifecycle | validate, normalize, protect, or observe load/upsert/remove | Data |
| Event | state a typed business occurrence associated with an Entity | Communication Events lane |
| Entity Transport | distribute an immutable copy of Entity state | Communication Transport lane |
| Integration contract | cross a DDD bounded context with an explicit versioned schema | application/integration boundary |

Framework reactions—cache invalidation, indexing, embeddings, audit facts, and job wakeups—are
separately identified. They appear in composition and operation explanation and do not pretend to be
application-authored domain facts.

The public grammar communicates intent:

```csharp
Todo.Lifecycle.BeforeUpsert(...);

public sealed class RecordCompletion : IHandleEntityEvent<Todo, TodoCompleted> { /* business code */ }
await todo.Events.Raise<TodoCompleted>(ct);

public sealed class ImportTodo : IReceiveEntity<Todo> { /* business code */ }
await todo.Transport.Send(ct);
```

Implementation status at R07-08: Lifecycle and both process-local Communication lanes ship. Local
Events prove payloadless/explicit details, occurrence identity, subscription fan-out, zero-subscriber
success, isolated copies, context carriage, acceptance, settlement, and independent bounded ingress.
Stable distributed receiver aliases, connector manifests/election, retries, and broker conformance
remain specified but unimplemented. Distributed paragraphs below are admission contracts for later
slices, not current capability claims.

Lifecycle registration is deterministic, idempotent per owner, removable, host-scoped, and invoked by
the canonical Data operation path. Before-hooks may reject with a stable code and correction.
After-hook names state whether the provider write or a real commit has occurred; enlistment alone is
not `AfterUpsert` or `AfterCommit`.

Every explicit `Raise<E>()` is a new occurrence. Retries retain its occurrence identity, and every
logical subscription becomes a delivery target under the elected channel's reported delivery class.
The source Entity already supplies identity and snapshot, so a zero-payload fact is the common path;
an overload accepts explicit details when the fact carries additional business information.
`Raise<E>()` treats `E` as an event-kind token and does not instantiate it. A contract declared as
details-required rejects the zero-details overload before dispatch. An Event with no subscriptions is
a valid zero-target occurrence and is reported as such.

Every explicit `Send()` is a new logical send of the immutable accepted snapshot. The route creates one
logical copy target per receiver group; the elected channel reports whether arrival is best-effort,
acknowledged, or durably accepted. Retries retain delivery identity and deduplicate within the declared
receiver scope. This is retry idempotence, not silent content-hash coalescing or exactly-once handler
side effects. Local delivery serializes/deserializes so it cannot expose a shared mutable reference.
Standard Transport with no receiver group fails correctively instead of succeeding as a silent no-op.

`Raise` and `Send` lift pointwise across one Entity, a finite sequence, and a genuine lazy async stream.
They preserve source ordinal/multiplicity, capture context once, use bounded backpressure, and report an
accepted prefix without claiming collection atomicity or handler execution order. A normal await ends
at channel publication acceptance. The returned receipt is a bounded operation summary; per-item
identity and receiver dedupe/completion/failure/retry/dead-letter transitions are incremental correlated
settlement facts, never an unbounded receipt collection. A capability-gated operation-scoped settlement
wait makes local tests deterministic; a separate host drain serves shutdown. Source, route, adapter,
and cancellation failures throw typed lane exceptions carrying the bounded partial receipt, with
cancellation still catchable as `OperationCanceledException`.

The sole V1 receiver path is a typed, auto-discovered business handler with stable application identity,
usable unchanged in-process or through a connector. A type-derived default group identity is
refactor-sensitive and reported; long-lived distributed groups declare an explicit business alias and
version. Lambdas are deferred. The full laws, adapter
rules, and greenfield deletion map are in
[ARCH-0113](../decisions/ARCH-0113-entity-capability-communication.md).

Events or Transport may be physically local, networked, or broker-backed. Adapters cannot change
occurrence/snapshot identity, fan-out/receiver-group cardinality, copy, context, receipt, or failure
semantics. Crossing a process does not itself cross a bounded context; crossing a bounded context
requires the explicit integration contract rather than silently publishing a persisted Entity schema.

The V1 foundation provides the complete local ring through one faithful in-process adapter. R07-07
provides local `Send`; R07-08 provides local `Raise`; both use auto-discovered typed handlers and zero
routing configuration. A future build-generated application communication manifest distinguishes direct
PackageReference/ProjectReference connector intent from transitive availability. One eligible outbound
adapter is elected per logical channel; every local group binds once to the same channel. Publishers
publish once and never infer remote receiver acceptance. InProcess implements that same contract when
no external connector exists.

Best-effort, durable, ordered, acknowledged, and other postures remain distinct facts; fan-out/groups,
copy, context/provenance, and contract safety cannot be weakened. An unavailable selected external
channel never silently falls back to process-local reach, and connector health participates in
readiness. Endpoint, credentials, and trust remain deployment configuration or orchestration discovery,
not values Koan invents.

V1 uses an inferred default channel, an optional business-named channel at the terminal, and one
host/deployment binding. Sender selection uses standard LINQ or a truthful Data query. A named typed
receiver may apply `Where` at ingress after authenticated deserialization; `false` is a terminal
filtered settlement, never retry/dead-letter, and never a confidentiality boundary. V1 performs no
adapter predicate pushdown, provider lowering, automatic `When` routing, mirroring, or failover.

Mesh identity derives from stable application identity plus environment. V1 is limited to replicas
sharing one application communication manifest and trust boundary; heterogeneous/cross-application
communication requires a future explicit integration manifest. Wire identity is mesh/application,
channel, versioned contract, operation/item, correlation, and sealed context—not a deployment plan
hash. The plan hash remains diagnostic so compatible rolling configuration can coexist.

Boot currently emits both selected local Communication lanes, assurance, separate bounds, typed Event
subscription and Transport receiver groups, and context carriage. Later mesh work must expand this into
the complete contract/channel/outbound-adapter/local-
group/inbound-binding matrix through the same startup, operator, and authorized agent projections.
Every surface reuses the same reason codes and safe corrections.

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
- A method named `Stream` performs bounded incremental enumeration. Materializing a complete result
  and yielding it afterward is not a streaming implementation, even when the return type is
  `IAsyncEnumerable<T>`.

## AI embedding and indexing

AI syntax follows business meaning rather than visual symmetry with Events or Transport. Ordinary
indexing is a consequence of saving an Entity declared with `[Embedding]`; the canonical path is
therefore `[Embedding]` + `Save()`, owned by Lifecycle. Explicit finite-set and whole-collection
rebuilds are migration control-plane operations with aggregate outcomes.

There is no generic source `.Index()` or `.Embed()` terminal in the V1 contract. Scalar
`EntityAi.Embed(entity)` remains an on-demand transform, not a persistence synonym. A future source
operation must first prove a distinct application intent, bounded source behavior, provider
negotiation, partial outcomes, and a real consumer; IntelliSense symmetry alone is insufficient.

## Backend negotiation and operation explanation

Every Entity operation can be described by a stable fact envelope containing, as applicable:

- entity type, operation, and correlation identifier;
- logical scope without secret values;
- selected source/adapter and the reason it won;
- application communication manifest, logical channel, outbound election, local inbound group binding,
  receiver-filter posture, and considered connector candidates;
- required and available capabilities;
- execution mode: native/pushdown, streamed, hybrid, in-memory, deferred, or rejected;
- transaction/event/projection participation;
- boundedness, limits, retries, receipt state, idempotency scope, ordering, and carried context axes;
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
- input/output schemas, limits, authorization, acceptance/delivery meaning, and dry-run support;
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
9. scalar, finite sequence, sync-yield, and async-yield forms preserve one semantic contract where the
   capability claims to lift;
10. partial source/adapter failure, cancellation, backpressure, context capture, acceptance receipts,
    and later settlement observation are asserted for lifted side effects; and
11. XML documentation and agent schema describe the same effects and limits.

## Greenfield evolution and removal

Koan is pre-1.0. A current API, package, sample, or document is evidence to assess, not a compatibility
anchor. When it conflicts with the coherent contract, the owning work item classifies it as:

- keep and harden in place;
- move behind the correct facet or layer;
- absorb into a smaller shared mechanism;
- rebuild under the canonical intent; or
- delete.

Compatibility aliases are not automatic. A forwarding period is justified only by demonstrated public
adoption and only when it does not leave two plausible common paths. False-success, unsafe, invented,
or semantically misleading surfaces are removed in the same coherent change that introduces their
replacement.

The current rebuild specifically permits:

- `Entity.Events` persistence lifecycle to become `Entity.Lifecycle` with no alias;
- arbitrary-object Messaging to be replaced by Entity `Events` and `Transport`;
- only Pipeline's Entity-cardinality normalization to move into Data.Core while each typed capability
  owns its execution/results, then delete the public generic DSL; and
- adapters and bridge packages to be deleted when they cannot satisfy the shared semantics.

Migration proceeds as meaningful vertical slices: establish the lower context/Data boundary, prove one
faithful in-process capability, then add network breadth. Each slice leaves a useful application state
and removes or supersedes the mechanism it replaces.

## Proposal checklist

Before approving a new Entity capability, record:

- business sentence and meaningful result;
- semantic location and receiver granularity;
- module/package and namespace;
- direct member, instance extension, static facet, attribute, or interface choice;
- provider/cost/transaction/event contract;
- scalar/set/stream lifting decision and partial-receipt behavior;
- context capture, isolation, idempotency, ordering, and adapter guarantees where communication is
  involved;
- startup and per-operation explanation;
- agent/API projection boundary;
- compile/runtime evidence and unsupported scenarios;
- compatibility, module-removal, and deprecation plan.

If those answers are longer than the business outcome because the capability is mainly infrastructure,
it likely belongs off Entity.
