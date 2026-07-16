# ARCH-0113 — Entity capability lifting and the Communication boundary

**Status**: Accepted
**Date**: 2026-07-15
**Deciders**: Framework maintainer
**Scope**: Entity Lifecycle, Events, Transport, scalar/set/stream composition, ambient context, and communication adapters
**Related**: ARCH-0100, ARCH-0105, ARCH-0106, ARCH-0111, ARCH-0112, R07

---

> **Implementation update (2026-07-15):** [DATA-0107](DATA-0107-provider-bounded-entity-streams.md)
> now satisfies the provider-query streaming prerequisite for SQLite, PostgreSQL, SQL Server,
> CockroachDB, MongoDB, and Couchbase. InMemory, JSON, and Redis reject before query/yield. The
> That work unlocked the qualified query-stream Transport cell without claiming universal adapter
> parity; R07-07 now exercises the resulting local terminal.

> **Implementation update (R07-06, 2026-07-15):** Data.Core now owns only lazy scalar/set/stream
> Entity-cardinality normalization. The generic `PipelineBuilder<T>`, mutable envelope/feature bags,
> and AI/Data/Vector/Messaging/Observability pipeline extensions are deleted. Ordinary embedding is
> Lifecycle-owned and explicit rebuilds are Data.AI migration operations. This substrate is now
> consumed by R07-07's Transport terminal.

> **Implementation update (R07-07, 2026-07-15):** `Koan.Communication` now ships the foundation's
> process-local Transport floor. The compile contract fixes `IReceiveEntity<TEntity>`, receiver
> `Where`/`Receive`, scalar/set/stream `.Transport.Send()`, `TransportAcceptance`, and
> `WaitForSettlement`. The real `AddKoan()` suite proves serialized per-group copies, source order and
> multiplicity, bounded backpressure, context capture/restoration and absence suppression, typed
> filtering, partial cancellation, fail-loud zero receivers, handler-failure settlement, boot facts,
> graceful drain, and repeated-host isolation. The built-in provider performs no retries, so broker
> retry/dedupe conformance remains part of the connector slice rather than a local durability claim.

> **Implementation update (R07-08, 2026-07-15):** the same package now ships process-local Entity
> Events without collapsing them into Transport. The compile contract fixes
> `IHandleEntityEvent<TEntity,TEvent>`, payloadless and explicit-details `.Events.Raise`, and
> scalar/set/stream discovery from the normal Entity namespace. The real `AddKoan()` suite proves
> per-Entity occurrence identity, fan-out identity, per-subscription copies, zero-subscriber success,
> details-required pre-enumeration rejection, filtering/failure settlement, context isolation, bounded
> partial cancellation, separate Event/Transport lanes, composition facts, drain, and repeated hosts.
> No connector, retry, durability, or broker claim follows from the local floor.

## Context

Koan's Entity-first language has the right center but the wrong boundaries around communication.
At decision time:

- `Entity.Events` means persistence lifecycle only;
- Messaging adds `Send<T>(this T) where T : class` to every reference type;
- message registration is an `IServiceCollection.On<T>` infrastructure API with one handler per CLR
  type;
- the InMemory provider broadcasts the same mutable object reference, while RabbitMQ serializes one
  copy into a type-named competing-consumer queue;
- `PipelineBuilder<T>` adds another composition model with mutable string-keyed feature bags and no
  aggregate receipt;
- cross-hop ambient capture has sound behavior but lives in Data.Core; and
- `AllStream` and `QueryStream` materialized the complete query before yielding while their
  `batchSize` parameter did not create a real streaming boundary (resolved for qualified adapters by
  DATA-0107; see the implementation update above).

These pieces cannot support one stable business meaning across a local process, a tenant-isolated
host, and a broker. Preserving their names or package boundaries would preserve the architectural
confusion.

The desired application grammar is simpler. Source-side intent stays on Entity; receiver behavior is a
stable business-named handler discovered by the host:

```csharp
Order.Lifecycle.BeforeUpsert(...);

public sealed class RecordApproval : IHandleEntityEvent<Order, OrderApproved> { /* business code */ }
await order.Events.Raise<OrderApproved>(ct);

public sealed class ImportOrder : IReceiveEntity<Order> { /* business code */ }
await order.Transport.Send(ct);
```

The same source-side operation should remain meaningful for one Entity, a finite set, and a lazy
stream. How a fact or snapshot moves is an internal choice. What it means is not.

Koan is pre-1.0 and greenfield. This decision intentionally prefers one sustainable model over public
API compatibility.

## Decision

### 1. Three public intents replace the current overlap

| Intent | Public subject | Owner | Meaning |
|---|---|---|---|
| Persistence lifecycle | `Order.Lifecycle` | Data | Ordered behavior around Data load/upsert/remove operations |
| Business occurrence | `Order.Events` / `order.Events` | Communication | A typed fact occurred to an Entity |
| Model distribution | `Order.Transport` / `order.Transport` | Communication | Distribute an immutable copy of Entity state |

The grammar is asymmetric by design:

| Intent | Host composition | Entity instance | Finite sequence | Async stream |
|---|---|---|---|---|
| Lifecycle | `Order.Lifecycle.BeforeUpsert(...)` | automatic | automatic per Data operation | automatic per Data operation |
| Events | stable typed Event handler | `order.Events.Raise<E>()` | `orders.Events.Raise<E>()` | `stream.Events.Raise<E>()` |
| Transport | stable typed Entity receiver | `order.Transport.Send()` | `orders.Transport.Send()` | `stream.Transport.Send()` |

Typed handlers compose behavior for an Entity type. `Raise` and `Send` originate from Entity sources.
A sequence facet does not expose subscription verbs.

Bare static `Order.Events.On(...)` and `Order.Transport.Receive(...)` are not V1 registration APIs: a
standalone static call either does nothing or requires the process-global state this decision deletes.
A future Entity-shaped declaration facade is admitted only if a compile/runtime proof binds it to one
host and replaces—rather than duplicates—the canonical handler path. Final handler and result type
names are compile-probed before implementation, but this semantic matrix is fixed.

### 2. Pointwise capability lifting is a framework law

A capability lifts from scalar to finite sequence and async stream only when all of these remain true:

1. Entity identity or state is essential to the scalar operation.
2. Sequence use means exactly one scalar operation for each yielded Entity.
3. Source ordinal and multiplicity are observed; Koan does not invent a collective domain fact or
   imply handler execution order.
4. Context is captured and sealed at terminal invocation, not rediscovered inconsistently per item.
5. Enumeration is one-pass, lazy, cancelable, backpressured, and bounded in memory.
6. Physical batching is private to the selected adapter and cannot change logical per-Entity identity.
7. The owning capability reports per-item progress or outcomes appropriate to its meaning without
   implying collection atomicity.
8. Invalid receivers are rejected by the type system; arbitrary objects do not acquire the surface.

A genuine fact about a group belongs to a modeled group Entity:

```csharp
await shipment.Events.Raise<OrdersPacked>(ct);
```

It does not emerge accidentally because an `IEnumerable<Order>` was the receiver.

Data.Core owns one minimal Entity-cardinality adapter beside `Entity<T>`. It normalizes scalar,
`IEnumerable<TEntity>`, and `IAsyncEnumerable<TEntity>` into an async Entity source and owns no
operation callbacks, receipts, routing, feature bags, or pillar policy. Each pillar owns execution and
results for its verbs. This lets Jobs, AI, Relationships, Cache, and Communication lift without
depending on one another.

Koan does not introduce a public `EntitySelection`, Flow DSL, or new pipeline container for this
purpose. Provider-backed Data selections remain Data-owned sources.

### 3. Event semantics are occurrence semantics

An Event states that a typed occurrence happened to the source Entity.

```csharp
public sealed record OrderApproved;

await order.Events.Raise<OrderApproved>(ct);
```

The source already supplies its type, identity, and immutable snapshot. The router supplies occurrence
identity, time, correlation, causation, and sealed ambient context. Requiring
`new OrderApproved(order.Id)` as the common path repeats information Koan already owns.

When a fact needs information not represented by the Entity, an explicit-details overload remains
valid:

```csharp
await order.Events.Raise(new ApprovalRejected(reason), ct);
```

`Raise<E>()` uses `E` as an event-kind token; it does not construct an empty `E`. A contract that
declares details as required cannot use that overload. Composition rejects it before dispatch and the
consumer analyzer should reject it at build time. The exact marker and overload spelling are
compile-probed, but the distinction between a payload-free occurrence and required business details is
fixed.

Event laws:

- every deliberate `Raise` creates a new occurrence;
- the Entity is snapshotted when accepted from the source; later sender mutation cannot change it, and
  separate subscriptions never share a mutable instance;
- internal retries retain that occurrence ID and deduplicate per logical subscription;
- raising twice, or yielding the same Entity twice, creates two occurrences;
- every logical subscription is one delivery target; the selected channel's reported delivery class
  determines whether acceptance, attempted delivery, or confirmed delivery is guaranteed;
- zero Event subscriptions are a valid no-target occurrence and are explicit in the receipt and boot
  facts; an Event does not require a listener to be a valid business fact;
- replicas of the same stable subscription identity compete within that subscription group;
- handler failure follows the selected retry/dead-letter contract and does not retroactively undo the
  business decision that raised the event;
- an Event implies neither persistence, event sourcing, broker publication, nor a Data transaction;
  and
- a synchronous rule that may reject a state change is an invariant/domain method or Lifecycle hook,
  not an Event listener.

Events may execute locally or cross a process. That physical choice does not change occurrence
identity or fan-out semantics.

### 4. Transport semantics are immutable snapshot semantics

Transport distributes the state the source Entity had when it was accepted from the source:

```csharp
await order.Transport.Send(ct);
```

Transport laws:

- local delivery serializes and deserializes too; handlers never share the sender's mutable object;
- the route creates one logical copy target for every stable receiver group; the selected channel's
  delivery class determines whether arrival is best-effort, acknowledged, or durable;
- replicas of the same receiver group compete for that group's copy;
- receiving a copy does not automatically persist it;
- every deliberate `Send` or yielded item is a new logical send;
- internal retries retain the same delivery ID and are deduplicated per receiver group;
- the isolation-context fingerprint participates in delivery identity, so Tenant A and Tenant B can
  never collide; and
- an unset or invalid Entity identity fails correctively rather than weakening routing or dedupe.

Standard Entity Transport with zero **declared** receiver groups is a corrective route failure, not a
silent no-op. The build-generated application communication manifest declares stable groups, so the
common same-application mesh can diagnose that without discovering live remote receivers. A declared
group with no currently live replica follows the elected adapter's reported queue/loss behavior. An
explicitly different best-effort broadcast/datagram lane may define no-listener behavior later; it does
not change `Transport.Send()`.

For V1, **idempotent Send means retry idempotence**, not silent cross-call state coalescing. Content
hash coalescing is unsafe: state can legitimately move A → B → A, and the second A must not disappear.
A later state-convergence mode requires a monotonic Entity revision or an explicit business
idempotency key and must be reported as a separate negotiated guarantee.

Transport does not promise exactly-once arbitrary handler side effects. A crash between an external
side effect and acknowledgement still requires an inbox transaction or business idempotency.

Entity Transport is for processes that share the model contract and trust boundary. Crossing a DDD
bounded context requires an explicit, versioned integration contract. A network hop alone does not
create a bounded context, and a bounded-context crossing must not be hidden behind Entity snapshot
transport.

### 5. Await means acceptance; settlement remains observable

`await Raise(...)` and `await Send(...)` mean:

> The enumerated source prefix was accepted by the effective route or operation boundary according to
> its reported durability contract.

They do not mean every handler has finished.

Events and Transport expose lane-named acceptance results over one internal fact shape. An acceptance
receipt records only facts known at that boundary:

- capability and versioned contract;
- operation and correlation identity;
- fixed-size aggregate enumerated, publication-accepted, and publication-rejected counts;
- selected logical channel, outbound adapter, and delivery-assurance class;
- whether acceptance was memory-only, acknowledged, or durably recorded; and
- whether source enumeration completed.

Per-item identities and source ordinals are derived from or correlated to the operation identity and
emitted incrementally through the bounded-retention/sampling observation seam; they are not mandatory
per-item logs, and an arbitrarily large receipt never accumulates them. Receiver binding, dedupe,
delivery attempts, filtering, handler completion/failure, retry
exhaustion, and dead-lettering are **settlement facts**. They are never invented on the publisher's
immediate receipt.

A host-owned observation surface exposes settlement to tests, graceful shutdown, operators, and
authorized agents. When the elected adapter supports correlated settlement, the receipt offers an
explicit operation-scoped wait; the built-in in-process adapter always does. This lets a test await only
the operation it just raised or sent. Normal host shutdown separately invokes a bounded whole-host
drain and reports accepted work left unsettled; applications wire neither mechanism.

A source fault, route rejection, adapter-acceptance fault, or cancellation throws a lane-specific typed
exception carrying the accepted-prefix receipt. Cancellation remains catchable as
`OperationCanceledException`. Accepted items are never reported as rolled back. An infinite stream has
no final success receipt until it completes; cancellation or failure exposes its partial receipt.

### 6. Ambient execution context moves below Data and Communication

Core owns one immutable typed ambient context and the durable capture/restore/suppress registry. The
working public name is `KoanContext`; named modules normally hide it behind business vocabulary such
as `Tenant.Use(...)`. Its `AsyncLocal` state is deliberately scoped to the current logical execution
flow, not to a host singleton. An explicit outer scope may intentionally apply while code invokes more
than one host.

- Each module owns the meaning, validation, portable encoding, and restoration of its slice.
- The registry sees opaque versioned strings and never names tenant, actor, classification, or another
  axis.
- Data routing, cache behavior, and transaction state become module-owned slices/facades rather than
  fields in a cross-pillar carrier.
- `EntityContext` remains because it is the Entity/Data operation facade for source, adapter,
  partition, and transaction intent—not as a compatibility alias. It is no longer the owner of every
  ambient concern, and its generic cross-module slice API is removed.
- An absent registered axis means explicit suppression and is valid. An unknown axis, malformed
  encoding, or unsupported version fails before application code runs.
- A syntactically valid opaque value proves format, not integrity. Security-sensitive axes crossing a
  process boundary require an adapter with authenticated provenance; otherwise the route is ineligible
  or ingress fails before application code.
- A carrier declares its generic minimum ingress-trust requirement. The router compares that metadata
  with adapter provenance capabilities without learning whether the axis means tenant, actor, or
  classification.
- Restore or explicit suppression spans the complete handler invocation and is disposed in reverse
  order.
- In-process provenance is trusted only within its owning host. A raw tenant header is never an
  authorization boundary.

The developer guarantee is:

> A signal accepted in Tenant A runs every Koan receiver under Tenant A; it cannot inherit or leak the
> carrier thread's Tenant B context.

That guarantee does not require one physical queue per tenant. It requires sealed context, fail-closed
ingress, and context-aware dedupe.

Host ownership applies to registries, routes, handlers, services, drains, and disposal. Ambient context
remains lexical and flow-global: a host cannot mutate or dispose another host's registrations, and host
disposal cannot clear a caller-owned outer context scope. Durable capture carries values, never host
services; the receiving host restores them into its own target flow.

Entity operations may resolve an implicit host only when exactly one live host is eligible. ASP.NET
requests, Jobs, and Communication handlers establish an explicit ambient host scope automatically.
Tests or applications running multiple hosts must establish that scope deliberately; Koan fails with
one corrective host-selection error instead of choosing a process-static winner.

The first Communication slice does not claim automatic participation in a Data transaction. If Koan
later coordinates Save plus Raise/Send, one neutral Core operation-completion seam owns that join. Data
must never invoke Communication, and Communication must never inspect a Data transaction coordinator.
Crash-atomic outbox behavior remains a separately negotiated capability.

### 7. One Communication pillar owns two semantic lanes

The target dependency direction is:

```text
Koan.Core
  └─ ambient context, host ownership, capabilities, facts
       └─ Koan.Data.Core
            ├─ Entity, selection, persistence, Lifecycle
            └─ Koan.Communication
                 ├─ Events, Transport, router, receipts
                 ├─ faithful built-in in-process adapter
                 └─ connector packages such as RabbitMQ
```

`Koan.Communication` is one pillar, not a third public Entity noun. Applications see `Events` and
`Transport`; “communication,” “dispatch,” “signal,” and “messaging” describe framework mechanics.

One package initially owns the Entity facets, semantic coordinators, router, receipt model, and
in-process adapter. No separate Events, Transport, Abstractions, or InMemory package is created until a
demonstrated dependency or distribution need earns it. Connector packages depend on this pillar.

Generic provider precedence belongs below every pillar, not in Data. The existing
`ProviderPriorityAttribute` moves from Data.Abstractions to Core during the canonical 0.18 break.
Core owns only stable provider identity and priority metadata; Communication still owns lane/channel
eligibility, assurance, reach, health, and the election itself. Koan does not introduce a universal
provider resolver whose lowest-common-denominator rules would erase concern semantics.

The internal runtime has six chokepoints:

1. Entity-cardinality normalization supplied by Data.Core;
2. immutable envelope normalization and stable versioned contract identity;
3. lane policy for occurrence or snapshot identity/cardinality;
4. one host-owned router that negotiates required guarantees;
5. receiver ingress for trust validation, dedupe, context restoration, fresh-object materialization,
   and handler invocation; and
6. one receipt/fact recorder.

Avoid separate public buses, publishers, proxies, envelope factories, topology builders, and pipeline
builders. The router necessarily has an internal host-owned handler catalog; it is not a process-static
public registry.

The sole V1 application receiver path is a business-named typed handler discovered by `AddKoan()`, with
a default receiver-group identity derived from its application contract type. The same handler works
in-process and through a connector. A type rename changes that default identity and is reported in boot
facts and the build manifest; long-lived distributed groups use an explicit stable business alias and
version. Lambdas are deferred. R07-07 fixes the V1 local handler spelling as
`IReceiveEntity<TEntity>` with optional synchronous `Where(TEntity)` and asynchronous
`Receive(TEntity, CancellationToken)`. Stable distributed aliases remain deferred until the first
connector gives that identity a real second consumer.

### 8. Data owns Lifecycle and truthful selection

The current persistence `Entity.Events` surface is renamed to `Entity.Lifecycle` with no compatibility
alias. Its registry and execution are rebuilt as host-owned Data behavior.

Lifecycle invocation moves to the canonical Data operation path so direct Entity calls, batch calls,
Web, MCP, and future projections cannot bypass it. Hook timing names what actually happened. An
`AfterUpsert` hook cannot run merely because a write was enlisted for later commit; a distinct
after-commit phase requires a real completion boundary.

Data also rebuilds provider streaming before provider queries are advertised as lazy sources. The
current materializing `AllStream`/`QueryStream` implementation is not retained under a streaming claim.
Scalar, finite collection, and a genuine caller-provided async stream can prove Communication first;
the `QueryStream(...).Transport/Events` cell is now unlocked on DATA-0107-qualified adapters, whose
consumer-paced numbered pages and cancellation/disposal behavior passed conformance. Unsupported
resident/key-value adapters remain corrective rejections rather than hidden materialization.

`Order.Where(...)` may later become a shorter Koan-owned selection spelling. This decision does not
extend Communication terminals to arbitrary `IQueryable<T>` implementations.

### 9. The complete ring works with zero external adapters

A new application referencing the Koan foundation package has a complete process-local semantic ring.
The built-in in-process adapter requires no external connector, Koan registration, or routing
configuration beyond `AddKoan()`. Typed application handlers are discovered as normal business
composition:

```csharp
public sealed class RecordApproval : IHandleEntityEvent<Order, OrderApproved> { /* business code */ }
public sealed class ImportOrder : IReceiveEntity<Order> { /* business code */ }

await order.Events.Raise<OrderApproved>(ct);
await order.Transport.Send(ct);
```

The local adapter dispatches Events to every local logical subscription and Transport snapshots to
every local logical receiver group. It uses bounded channels, serialized copies, the same context
ingress, identities, dedupe, and receipts required of a network adapter. Its facts honestly state
process-only reach, memory-only acceptance, and no restart durability.

The in-process adapter is the Communication concern's built-in provider floor, registered by the
pillar itself at the reserved minimum priority. It is not a connector the application must reference.
This follows the existing concern-owned Memory/Jobs precedent while retaining Data's explicit-binding
and deterministic-priority rules.

This is the V0 floor, not a toy alternate API. An application can fully exercise composition,
filtering, failure behavior, tests, facts, and agent inspection before choosing infrastructure.

### 10. Connector references form a deterministic default mesh

Referencing a connector directly from the application contributes a channel candidate to the
host-owned Communication mesh. A build-generated application communication manifest records direct
`PackageReference` and `ProjectReference` intent so runtime assembly discovery never guesses whether a
connector was transitive. It also records versioned contracts, typed handler groups/aliases, and named
channels. The same build seam feeds package locking and source-build tests. Transitive connectors
remain available for an explicit deployment binding but cannot hijack the default.

Normal applications add one conforming connector package and write no application routing code or Koan
registration. Endpoint, credentials, trust material, and production availability remain deployment
configuration or orchestration discovery; “zero configuration” never means invented secrets:

```text
no external connector  -> built-in in-process default channel
one eligible connector -> that connector implements the default mesh channel
several connectors     -> deterministic capability election per logical channel
```

The mesh is the logical routing fabric. A **channel** is a named or inferred route policy. An
**adapter** is its physical implementation, such as InProcess, UDP, or RabbitMQ. Domain code normally
names the required outcome or a logical channel, not a provider class.

One outbound adapter is elected per logical channel. The publisher submits each Event occurrence or
Transport snapshot once to that channel; it does not need a distributed registry of remote receivers.
Every stable local subscription/receiver group binds independently to exactly one inbound adapter for
the same channel. A mesh-visible handler is never invoked both directly and through that adapter. With
no external connector, InProcess implements both sides of the same contract. Adding a connector changes
physical reach without changing the handler or source-side business code.

The adapter owns physical fan-out/topology: Events deliver one occurrence to every subscription group;
Transport delivers one snapshot to every receiver group; replicas within a group compete. Publisher
acceptance reports publication to the channel, never inferred remote-group acceptance. Receiver-group
delivery is settlement truth.

The router elects each lane/channel with this executable order:

1. apply the host/deployment binding for the logical channel, when present;
2. collect direct build-manifest intents that claim that lane/channel; when none claim it, use only the
   built-in in-process floor for automatic election;
3. reject candidates that cannot preserve contract identity, copy isolation, context/provenance,
   retry identity, and the lane's fan-out or receiver-group topology;
4. prefer the highest documented `DeliveryAssurance` rank—durably acknowledged, acknowledged, then
   best-effort—among otherwise eligible candidates;
5. prefer the highest Core-owned provider priority; and
6. use stable connector identity as the deterministic tie-breaker.

Capability requirements are hard filters. Durability may vary; contract safety, isolation, and
cardinality may not. A direct connector claim removes the built-in floor from that lane/channel's
automatic candidate set; temporary unavailability cannot silently shrink external reach back to the
process. The fixed assurance/priority/identity vector always elects among eligible default candidates
without operator input. If no intended candidate satisfies a declared requirement, startup fails with
the candidates, missing capabilities, and one safe correction. A connector that supports Transport
but does not claim Events can transparently supersede only Transport while Events remain local.
Mirroring, automatic failover, and multi-path delivery are deferred until a real use case proves
semantics that do not expand this foundation.

Capabilities are facts, not marketing tiers. A UDP connector may honestly declare best-effort liveness
and limited ordering, but that does not excuse incorrect event fan-out, receiver groups, copy, context,
or provenance. Raw datagram or multicast behavior is not eligible for standard Entity Transport unless
the connector proves stable membership/rendezvous and retry dedupe. It may later expose an explicitly
different datagram/broadcast lane; it cannot silently weaken `Transport.Send()`. RabbitMQ earns only
the acknowledgement, topology, inbox, and recovery guarantees its conformance cells prove.

If an explicitly selected or default external channel becomes unavailable, Koan does not silently
fall back to InProcess. That changes reachability. Startup or the operation fails correctively and
reports the affected channel. Runtime connector health participates in readiness.

Zero-configuration replicas are safe only with stable names and one application communication manifest.
V1 derives:

- mesh scope from stable application identity plus environment;
- contract alias and schema version deterministically from the Entity/event contract, with an explicit
  stable alias for rename/version evolution;
- a unique node-session identity at host start; and
- receiver-group identity from its stable typed handler or explicit business alias.

V1 Entity mesh is limited to replicas sharing that application manifest and trust boundary.
Heterogeneous or cross-application communication requires a future explicit integration manifest; a
shared scope string is insufficient and does not turn Entity snapshots into integration contracts.

Wire envelopes carry mesh/application identity, logical channel, versioned contract, operation/item
identity, correlation/causation, and sealed context/provenance. Contract/schema compatibility governs
acceptance. The host computes a plan hash for boot/operator correlation only; ordinary compatible
configuration or filter changes do not become a second wire-compatibility protocol. Application binary
version is not part of mesh scope, so compatible rolling replicas can coexist.

The V1 foundation bundle includes the in-process Communication ring after its conformance journey
passes. External connector references then change reach and guarantees without changing business
code.

### 11. Sophisticated flows start with three small choices

V1 has one inferred default logical channel, an optional business-named channel at the terminal, and one
host/deployment binding from logical channel to connector. It does not ship competing attribute/fluent
precedence rules, provider-specific `Via<T>()`, automatic branching routes, mirroring, failover, or a
topology DSL. Exact channel and typed-filter spellings are compile-probed; the policy boundary is fixed.

Flows compose source selection, optional channel choice, and receiver-local selection:

```csharp
// Standard Data/LINQ selection occurs before the terminal.
await Order.QueryStream(order => order.Ready)
    .Transport.Send(ct);

await orders
    .Where(order => order.Classification == Classification.Restricted)
    .Transport.Send(channel: "restricted", ct);

// Illustrative typed receiver; exact base/interface spelling is compile-probed.
public sealed class ReadyOrders : EntityReceiver<Order>
{
    public override bool Where(Order order) => order.Ready;
    public override Task Receive(Order order, CancellationToken ct) => ...;
}
```

Standard LINQ or a truthful Data query filters the source before egress. Receiver `Where` runs after
authenticated, typed deserialization and belongs to the stable named handler; `false` records a terminal
`filtered` settlement and never retries or dead-letters. It is convenience, never a confidentiality
boundary. V1 does not normalize a predicate AST, push filters into adapters, or lower them into
providers. Boot facts name the handler-owned filter and `ingress` evaluation, but the predicate and its
implementation hash are not wire contract.

An automatic sender-side `When` policy is deferred until a dogfed flow proves it removes real business
clutter. If admitted, mesh-visible policies require a stable named type or business alias/version;
anonymous structural lambda hashing is not introduced merely to make a fluent sample compile.

Events use the same subscription-selection discipline. This does not recreate `PipelineBuilder`:
there are no mutable feature bags, arbitrary stages, or provider objects in the business flow. Source
selection, channel choice, receiver selection, and the terminal intent each have one owner.

### 12. Adapter negotiation preserves semantics

Adapters declare candidacy and capabilities; the router alone records the effective election. A static
boot description cannot assert selection.

Per lane, startup and runtime facts name:

- selected adapter and local/process/network reach;
- copy mechanism and maximum payload posture;
- fan-out or receiver-group behavior;
- acceptance durability and acknowledgement;
- ordering scope;
- retry, dedupe, inbox, and dead-letter guarantees;
- active durable context axes by safe identifier, never value;
- degradation or rejection reason; and
- a safe correction.

They also name application-manifest identity, mesh scope, versioned contract, each outbound
channel/adapter election, each local group/inbound binding, diagnostic plan hash, filter placement, and
whether each connector candidate came from direct intent, explicit binding, transitive availability,
election, or rejection.

Normal boot output is concise: one Communication summary plus warnings for rejected, unavailable, or
degraded plans. The complete per-contract/channel/group matrix remains available as structured runtime
facts and an opt-in expanded startup view. Errors, boot output, operator inspection, and authorized
agent projections reuse the same stable reason codes and corrections; they never reconstruct different
stories from logs.

The built-in in-process adapter must obey the same logical contract as a broker: serialized copies,
logical groups, context isolation, identities, receipts, cancellation, and host isolation. It is
ephemeral and reports that fact.

A configured or referenced distributed connector that cannot start does not silently fall back to
in-process delivery. That would change reachability while leaving business code apparently successful.
Its runtime health contributes to readiness.

### 13. Agent and projection behavior follow the same contract

Adding Communication makes its Entity facets discoverable to coding agents through types and XML
documentation. It does not automatically expose them to runtime agents, HTTP, or MCP.

Explicit projections may reuse the same capability and receipt facts after authorization. Agent-facing
metadata states mutation, idempotency scope, deferred acceptance, fan-out/group behavior, limits,
provider election, context handling, and safe retry guidance. A coding or runtime agent should never
need to infer these rules from RabbitMQ names or scrape startup prose.

### 14. Pillars earn lifting verb by verb

Pointwise semantics, not visual symmetry, decide which capabilities grow over sets and streams.

| Capability | Disposition |
|---|---|
| Events `Raise` | flagship scalar/set/stream lift |
| Transport `Send` | flagship scalar/set/stream lift |
| Relationships | intrinsic Data lift; rebuild key inference and preserve bounded-query facts |
| Jobs submission | lift only for `IKoanJob` Entities; ledger remains the work truth |
| AI | lift pointwise `Embed`/`Index`; keep type search and nested-result similarity distinct |
| Cache | lift Entity-entry eviction only; policy, topology, and flush remain type/control plane |
| Media | consider derivative/prewarm operations on media-capable Entities after a real contract exists |
| Canon | retain direct constrained `Canonize`; delete instance-shaped administration |
| Storage | retain its specialized model; do not add a redundant generic facet |
| Web and MCP | governed projections, never generic Entity capabilities |
| Backup and Admin | operator control plane, never scalar/set/stream Entity verbs |

### 15. Greenfield disposition of the current architecture

| Disposition | Current pieces |
|---|---|
| Keep | Entity/Data foundation; C# 14 module facets; Core capabilities/facts; ambient capture/restore/suppress behavior and tests; Jobs ledger/context-aware coalescing; bounded relationship negotiation; explicit MCP projection |
| Move | ambient typed context and durable carriers from Data.Core to Core; tenant carriage out of the Data-axis DSL |
| Absorb | InMemory messaging into Communication; only cardinality normalization from Pipeline into a minimal Data.Core Entity source adapter; Jobs wake and Cache coherence transport into an internal framework-signal lane after conformance |
| Rebuild | Lifecycle ownership/invocation; Data streaming; Messaging as Communication; subscriptions/receiver groups; InMemory and RabbitMQ adapters; selection/facts/receipts |
| Rename | persistence `Entity.Events` → `Entity.Lifecycle`; `Koan.Messaging.Core` → `Koan.Communication`; Messaging connectors → Communication connectors |
| Delete | broad `Send<T> where class`; `services.On<T>`; `IMessageProxy` and startup buffer; static interceptors; current `HandlerRegistry`; unused mutable `TransportEnvelope<T>`; public Pipeline DSL and pillar pipeline extensions; separate InMemory connector; Messaging bridge packages after their signals migrate; false provider-selected boot claims; compatibility aliases |

The current Messaging README and technical guide are retired with the implementation. Their absent
attributes, routing APIs, inbox/outbox, batch, retry, and topology claims are not a migration contract.

## Consequences

### Positive

- Business code says what happened or what should move, not which infrastructure carries it.
- The same grammar works for a single model, a set, or a real stream.
- Local development and distributed deployment share identities, copies, context, receipts, and
  failure laws.
- Tenant isolation is owned by Tenancy and enforced at one ingress chokepoint rather than repeated in
  every adapter.
- Coding agents get a small regular grammar; operators and runtime agents get facts from the same
  decisions.
- Koan deletes more public mechanisms than it introduces.

### Costs and boundaries

- Existing Messaging, Pipeline, Lifecycle naming, bridge packages, adapters, samples, and tests require
  intentional replacement or deletion.
- Provider-query syntax depends on DATA-0107 qualification; unsupported adapters reject rather than
  weakening the Communication terminal's meaning.
- The first in-process implementation proves process-local ephemeral acceptance, not production
  broker durability.
- Exactly-once effects, automatic Data transaction coupling, outbox/inbox atomicity, global ordering,
  replay, cross-bounded-context schemas, and hostile-network trust are not implied.
- Stable distributed handler identity may require a typed handler or explicit business identity where
  a local lambda was previously sufficient.

## Dependency-ordered implementation

1. **Core context foundation** — move typed ambient state and durable carriers beneath Data; preserve
   fail-closed Jobs/Tenant proofs and split Data-specific state into its own facade.
2. **Data semantic truth** — rename Lifecycle, make its execution canonical and host-owned, and replace
   fake streaming with a real provider contract before enabling query terminals.
3. **Shrink the substrate** — introduce the minimal Data.Core Entity-cardinality adapter, let each
   pillar own execution/results, replace the two real Pipeline sample uses with business-named
   operations, and delete the public Pipeline DSL.
4. **Transport flagship** — rebuild Communication with one faithful in-process adapter; prove copies,
   logical receiver groups, retry idempotence, acceptance receipts, settlement observation,
   operation-scoped settlement wait, cancellation, context isolation, typed receiver filters, and repeated hosts for
   scalar/set/stream sources.
5. **Events policy** — add occurrence identity and fan-out over the same kernel, including payload-less
   `Raise<E>()` and explicit-details behavior.
6. **Mesh and broker parity** — generate one PackageReference/ProjectReference-compatible application
   communication manifest; prove zero-routing-code one/many-connector election per channel, inbound
   group bindings, and honest weaker guarantees; rebuild RabbitMQ only against the same adapter
   conformance kit and do not promote it while any local/network semantic cell differs.
7. **Internal convergence** — migrate Jobs wake and Cache coherence as framework signals, then delete
   their Messaging bridge packages and the old Messaging generation.
8. **Secondary capability lifts** — Relationships, constrained Jobs streams, AI Embed/Index, Cache
   eviction, and later Media, each as its own business proof.
9. **Golden proof and bundle admission** — add the final Communication pillar to the foundation bundle
   only after one cumulative source/package journey proves the grammar, startup explanation, tenant
   isolation, failure/recovery, and agent-readable facts.

Each step leaves one useful state and deletes or supersedes the mechanism it replaces. Network breadth
does not precede the in-process semantic contract.

## Required conformance

The shared kit covers:

- scalar, finite, sync-yield, async-yield, and—after Data repair—provider-stream compilation;
- invalid non-Entity receivers and module-removal behavior;
- Event occurrence identity, retry identity, and logical subscription fan-out;
- explicit zero-subscription Event acceptance and zero-receiver Transport rejection;
- Transport send identity, retry dedupe, receiver groups, and tenant-scoped identity;
- serialization-copy isolation, distinct handler copies, and post-send/post-raise mutation;
- payload-free event tokens plus rejection of details-required events without details;
- lazy single enumeration, bounded memory, cancellation, ordering claims, partial acceptance receipts,
  typed partial-receipt exceptions, no unbounded per-item receipt state, operation-scoped settlement
  wait, and separately correlated settlement facts;
- parallel Tenant A/B restoration plus absent-context suppression;
- unknown-axis, malformed/version-invalid, and unauthenticated-sensitive-context refusal before handler
  code, without treating opaque syntax validation as integrity;
- handler retry/dead-letter behavior without cross-subscription failure;
- repeated-host registry/service/drain isolation and disposal, plus deliberate logical-flow context
  behavior when an outer scope spans hosts;
- schema/version rejection rather than loose CLR-name deserialization;
- InProcess/RabbitMQ logical parity;
- zero-connector, one-direct-connector, transitive-only, multi-connector, explicit-channel,
  unavailable-default, PackageReference/ProjectReference intent-manifest parity, outbound channel and
  inbound group binding, diagnostic plan-hash, and rolling-compatible contract elections;
- source-side LINQ/query selection, explicit logical-channel terminals, named ingress filters, terminal
  filtered settlement, and proof that receiver filters are not a confidentiality boundary;
- one-application-manifest mesh scope and rejection of heterogeneous/cross-application Entity meshes;
- ineligible raw-datagram topology and unauthenticated cross-process context refusal; and
- concise startup plus structured facts for every elected guarantee/correction, connector readiness,
  and refactor-sensitive default handler identities.

Public maturity does not advance until the relevant source and staged-package cells pass.
