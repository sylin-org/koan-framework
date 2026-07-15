---
type: SPEC
domain: framework
title: "R07 - Rebuild the Semantic Capability Ring"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: in-progress
  scope: greenfield Entity capability, communication, context, and execution architecture
---

# R07 — Rebuild the semantic capability ring

- Tranche: `T6 — capability-ring graduation`
- Status: `in-progress`
- Depends on: R06
- Unlocks: coherent Entity capabilities, context-safe Events and Transport, and later capability rings
- Owner: Core context, Data lifecycle, Entity language, and communication boundaries

## Meaningful outcome

An application expresses a fact about one Entity, a finite set, or a lazy stream with the same small
business-readable grammar. Koan owns cardinality, context propagation, routing, backend mechanics,
idempotency, partial failure, and explanation without teaching the application a bus, pipeline,
envelope, provider, or service locator.

## Why now

The foundation ring is stable enough to expose a semantic fault above it. The current surface uses
`Events` for persistence lifecycle, offers broad transport through `Send<T>(this T) where T : class`,
and has a separate mutable-bag pipeline abstraction. Those mechanisms obscure rather than reinforce
the Entity language. Koan is pre-1.0 and intentionally greenfield: compatibility preservation is
subordinate to arriving at one sustainable architecture.

## Evidence to read first

- Code: Entity lifecycle builders, `EntityContext`, Messaging Core/InMemory/RabbitMQ, ambient carrier,
  semantic pipelines, Entity queries/streams, Jobs and Cache facets.
- Tests: Entity language consumer cells, Data lifecycle/context tests, Messaging tests, Jobs ambient
  context and collection submission tests.
- Documentation / decisions: Entity Semantics Contract, ARCH-0100, ARCH-0106, R03 ecosystem and Entity
  inventories, and the R04/R06 acceptance records.
- Relevant external primary sources: none required; this slice is grounded in current repository
  behavior and already-recorded ecosystem design mining.

## Decisions

### DECIDED

- Break-and-rebuild is required when current packages, names, or public APIs obstruct the coherent
  target. No compatibility layer is presumed.
- `Entity<T>` remains Koan's semantic spine and IntelliSense discovery surface.
- Persistence lifecycle, a business event, and transport of Entity state are different developer
  intents even when they share internal execution machinery.
- Data owns persistence and lifecycle; it does not know how Events or Transport move.
- Referenced modules contribute their own Entity language. Data.Core does not predict optional
  capability facets.
- Infrastructure choices remain internal but their selected guarantees and failures are inspectable.
- Capabilities whose meaning is per Entity lift from scalar to finite set to lazy async stream without
  changing their semantic promise.
- Standard `IAsyncEnumerable<TEntity>` remains the public stream substrate; Koan does not require a
  second public pipeline/container type merely to attach terminal capabilities.
- Events and Transport share one internal context-aware dispatch kernel while retaining separate
  public contracts, coordinators, identities, acceptance results, and delivery laws.
- One `Koan.Communication` pillar replaces current Messaging and owns both semantic lanes plus its
  built-in in-process adapter. Data.Core owns only the minimal scalar/set/stream Entity-cardinality
  adapter; each pillar owns execution and results. The public Pipeline surface is deleted, not moved
  into Communication.
- A foundation application with only `AddKoan()` receives a complete process-local Events/Transport
  ring. No external adapter or configuration is required.
- A build-generated application communication manifest records direct PackageReference/ProjectReference
  connector intent. The host elects one eligible outbound adapter per logical channel; every local
  stable group binds once to that channel, and transitive references cannot hijack defaults.
- A model-owned logical channel and one host/deployment binding are the V1 application policy. Hard
  semantic capabilities filter adapters; a fixed delivery-assurance rank and stable identity complete
  deterministic election. Effective outbound/inbound/filter decisions and diagnostic plan hashes are
  boot-reported.
- Sender selection, optional business-named channel choice, and named receiver filters remain typed and
  composable without reviving a general Pipeline DSL. Automatic sender `When` routing is deferred.
- Event and Transport awaits return only lane-named acceptance facts. Receiver dedupe, handler outcome,
  retry exhaustion, and dead-lettering are later correlated settlement facts with a host-owned
  observation seam, operation-scoped wait where supported, and separate shutdown drain. Receipts are
  bounded summaries and never accumulate per-item stream identity.
- Transport guarantees retry idempotence. Cross-call state convergence requires an explicit revision
  or idempotency contract and is not inferred from content hashes.
- Core owns logical-flow ambient typed context and durable carriers. Data deliberately retains the
  `EntityContext` facade for Entity/Data intent without owning other modules' axes. Absent registered
  axes suppress; unknown/malformed axes fail; sensitive cross-process axes require authenticated
  adapter provenance.
- The sole V1 receiver path is an auto-discovered, business-named typed handler. Bare static `On` and
  `Receive` calls are deferred because they cannot bind a host safely without a containing composition
  API or hidden process state.
- UDP or another best-effort adapter may weaken durability, never contract identity, copy, isolation,
  fan-out, or receiver-group semantics. An adapter that cannot prove those hard invariants is
  ineligible for standard Entity Transport.
- Adjacent pillars lift only pointwise verbs that preserve scalar meaning; pillar symmetry is not an
  admission reason.

### DEFAULT

- The foundation bundle admits Communication as soon as its in-process source/package journey passes;
  this is an evidence gate, not an optional final product posture.
- A shorter Koan-owned `Order.Where(...)` selection may follow a truthful `QueryStream`; arbitrary
  `IQueryable<T>` does not receive communication terminals.

### OPEN

- Exact callback overloads and lane-named receipt type spellings, to be settled by checked-in C# 14
  consumer probes without reopening their semantics.
- Exact typed-handler/alias spellings, optional logical-channel terminal, and named receiver `Where`,
  subject to host-ownership and stable-identity compile probes.

## Scope

### In

- Ratify the semantic laws for scalar, set, and stream capability use.
- Assign DDD/SoC ownership for Lifecycle, Events, Transport, context, routing, adapters, and receipts.
- Produce a keep, absorb, rebuild, rename, or delete disposition for the current architecture.
- Define the smallest target package topology and conformance matrix.
- Order implementation as independently useful, red/green vertical slices.
- Amend canonical Entity semantics where the prior lifecycle/event contract is superseded.

### Out

- Network connector work before the in-process semantic contract passes.
- Certifying RabbitMQ, distributed exactly-once delivery, cross-provider atomicity, or production
  multi-tenant isolation.
- Adding unrelated features or preserving APIs solely because they already exist.
- Publishing packages, pushing branches, tagging, or releasing.

## Business-code proof

The target grammar is intentionally small:

```csharp
await order.Events.Raise<OrderApproved>(ct);
await orders.Events.Raise<OrderApproved>(ct);
await Order.QueryStream(x => x.Ready).Events.Raise<OrderApproved>(ct);

await order.Transport.Send(ct);
await orders.Transport.Send(ct);
await Order.QueryStream(x => x.Ready).Transport.Send(ct);

public sealed class RecordApproval : IHandleEntityEvent<Order, OrderApproved> { /* business code */ }
public sealed class ImportOrder : IReceiveEntity<Order> { /* business code */ }
Order.Lifecycle.BeforeUpsert((order, ct) => ...);

await orders
    .Where(order => order.Priority == Priority.Critical)
    .Transport.Send(channel: "priority", cancellationToken: ct);
```

The final spellings may tighten during conformance-driven implementation. The semantic distinctions
and scalar/set/stream equivalence may not.

## Architecture assessment

[ARCH-0113](../../../decisions/ARCH-0113-entity-capability-communication.md) is the accepted decision
and deletion map. It records the semantic laws, package direction, adapter contract, conformance
matrix, adjacent-pillar admission, and dependency-ordered rebuild. The canonical
[Entity Semantics Contract](../../../architecture/entity-semantics-contract.md) is amended to match.

The assessment found two lower-layer defects that must precede visible Communication syntax:

- the correct cross-hop ambient carrier is owned by Data rather than Core; and
- provider `AllStream`/`QueryStream` currently materialize complete results rather than providing the
  bounded lazy source required by the public grammar.

It also found that the current InMemory and RabbitMQ adapters implement different cardinality and copy
semantics. Neither is a compatibility base for Events or Transport.

## Implementation slices

| Order | Slice | Meaningful result | Principal deletion/supersession |
|---|---|---|---|
| 1 | [R07-01 Core context foundation](r07/R07-01-core-context-foundation.md) | Tenant and other module context survive durable work through a Core-owned, fail-closed contract | Data-owned generic slice/carrier APIs |
| 2 | Data semantic truth | Lifecycle is canonical/host-owned and Data streams are genuinely incremental | lifecycle `Events` name, bypassable hooks, materializing stream implementation |
| 3 | Typed capability substrate | A minimal Data.Core cardinality adapter and pillar-owned execution replace generic public flow machinery | `PipelineBuilder` and pillar pipeline extensions |
| 4 | In-process Transport flagship | a foundation + `AddKoan()` app gets local scalar/set/stream snapshots, stable typed receivers, source/ingress filters, isolated copies, bounded acceptance, operation-scoped settlement wait, and tenant-safe retry identity | old Messaging Core, proxy/buffer, broad `Send`, separate InMemory connector |
| 5 | Events policy | payload-less occurrences fan out over the proved kernel | event/messaging conflation and service-collection handlers |
| 6 | Mesh, broker, and internal parity | a build manifest turns connector references into zero-routing-code channel election; RabbitMQ, Jobs wake, and Cache coherence obey the same groups/context/facts | current RabbitMQ implementation and two Messaging bridge packages |
| 7 | Secondary capability lifts | Relationships, Jobs, AI, Cache, then Media adopt only proven pointwise verbs | fragmented or misleading per-pillar surfaces |

Only the next slice receives a child card. Later rows remain outcomes rather than speculative API
backlogs until their prerequisites pass.

## Execution plan

1. Inventory current ownership, dependency direction, cardinality handling, context transfer, and
   adapter behavior. **Complete.**
2. Ratify semantic laws, DDD boundaries, chokepoints, and failure/inspection behavior. **Complete.**
3. Classify existing types, projects, and public surfaces as keep, absorb, rebuild, rename, or delete.
   **Complete.**
4. Amend the canonical contract and record the architecture decision. **Complete.**
5. Execute R07-01 without changing current durable context behavior.
6. Open each later implementation slice only after its lower boundary passes.
7. Prove in-process semantics before any broker migration or public maturity change.

## Verification

- Focused tests: the completed architecture assessment is documentation-only; implementation children
  define executable gates.
- Broader regression tests: none until a production slice changes.
- Documentation / sample checks: strict full-site docs build and initiative link validation.
- Manual or observable proof: current code citations for every disposition and a reviewable target
  dependency map.
- Privacy check: no private downstream identity, path, persona, or workflow enters tracked artifacts.

## Acceptance additions

- One capability-lifting law states when scalar, set, and stream forms are equivalent and when they
  are not.
- Events and Transport each define identity, cardinality, context, delivery, idempotency, ordering,
  failure, and inspection semantics.
- The no-external-adapter path exercises the complete local ring; PackageReference/ProjectReference
  intent, direct/transitive and one/many connector election, outbound channel/inbound group binding,
  mesh/contract/group identity, weaker durability, hard semantic invariants, source/ingress filters,
  bounded receipts, and settlement observation are explicit.
- Lifecycle has a Data-owned name and no longer shares the `Events` noun.
- The target has fewer public mechanisms than the current Entity + Messaging + Pipeline combination.
- Each implementation slice produces a meaningful application result and can delete or supersede the
  mechanism it replaces.

## Stop conditions

- Stop any design that makes Data depend on a communication implementation.
- Stop any collection abstraction that changes per-Entity meaning or silently claims atomicity.
- Stop any adapter contract that embeds tenant, actor, or another module-owned context axis by name.
- Stop any compatibility work that leaves two canonical paths for the same intent.
- Stop before publication, push, tag, release, or private downstream inspection.

## Session close

Update [`../PROGRESS.md`](../PROGRESS.md), replace [`../NOW.md`](../NOW.md), and attach the acceptance
record before marking the card `passed`, `blocked`, or `stopped`.
