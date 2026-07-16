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
  the former generic Pipeline DSL, Entity queries/streams, Jobs and Cache facets.
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
  ring. Communication owns the minimum-priority in-process provider; no external adapter, separate
  InMemory reference, or configuration is required.
- A build-generated application communication manifest records direct PackageReference/ProjectReference
  connector intent. The host elects one eligible outbound adapter per logical channel; every local
  stable group binds once to that channel, and transitive references cannot hijack defaults.
- A model-owned logical channel and one host/deployment binding are the V1 application policy. Election
  is per lane/channel: explicit binding; direct connector claim or built-in floor; hard semantic
  capabilities; fixed delivery-assurance rank; Core-owned provider priority; then stable identity.
  Direct intent never silently falls back to local reach when unhealthy. Effective
  outbound/inbound/filter decisions and diagnostic plan hashes are boot-reported.
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
- provider `AllStream`/`QueryStream` materialized complete results at assessment time rather than
  providing the bounded lazy source required by the public grammar.

R07-01 repaired the first defect. R07-02 passes the second: qualified adapters page beneath the
existing `IAsyncEnumerable<TEntity>` surface, complete-scan resident adapters reject correctively,
and public/operator/agent guidance states the same tested boundary.

The assessment also found that the current InMemory and RabbitMQ adapters implement different
cardinality and copy semantics. Neither is a compatibility base for Events or Transport.

## Implementation slices

| Order | Slice | Meaningful result | Principal deletion/supersession |
|---|---|---|---|
| 1 | [R07-01 Core context foundation](r07/R07-01-core-context-foundation.md) | **Passed.** Tenant and other module context survive durable work through a Core-owned, fail-closed contract | Data-owned generic slice/carrier APIs |
| 2 | [Provider-bounded streaming](r07/R07-02-provider-bounded-streaming.md) | **Passed.** Data.Core 42/42 focused and 325/325 full, six qualified and three fail-closed adapter cells, SQLite 1/1, and Backup 5/5 acceptance plus 7/7 full establish the bounded contract | materializing stream implementation and unused batch hint |
| 3 | [Automatic breaking package lineage](r07/R07-03-automatic-package-lineage.md) | **Passed.** One version intent automatically mints and proves the complete reverse-dependent wave without an operator package list | manual closure tracking and source-SHA/package-SHA conflation |
| 4 | [Public-release ratchet rehabilitation](r07/R07-04-public-release-ratchet.md) | **Passed.** The exact automatic release floor passes with bounded project fan-out, every runnable suite retained, and no leaked host state | misclassified helper projects, unbounded certification concurrency, and a red solution-test baseline |
| 5 | [Canonical Lifecycle](r07/R07-05-canonical-lifecycle.md) | **Passed.** Lifecycle is host-owned, unavoidable, inspectable, and honestly named across Entity/Data/REST/MCP | lifecycle `Events` name, process-static registry, parallel repository construction, and bypassable hooks |
| 6 | [Typed capability substrate](r07/R07-06-typed-capability-substrate.md) | **Passed.** A minimal lazy Data.Core cardinality adapter and pillar-owned execution replace generic public flow machinery | `PipelineBuilder`, mutable envelopes, and pillar pipeline extensions |
| 7 | [In-process Transport flagship](r07/R07-07-local-transport.md) | **Passed.** Foundation `AddKoan()` provides bounded local scalar/set/stream snapshots, typed receiver groups/filters, isolated copies, opaque context carriage, acceptance, local settlement, host drain, and facts | old Messaging semantics are superseded for new application Transport; bridge-dependent packages remain until internal convergence |
| 8 | [Local Events policy](r07/R07-08-local-events.md) | **Passed.** Payloadless/explicit-details occurrences fan out as isolated copies over a shared host kernel with Event-owned identity and outcomes | event/messaging conflation and service-collection handlers |
| 9 | [Direct-reference intent](r07/R07-09-direct-reference-intent.md) | **Passed.** Core records direct package/project provenance separately from the transitive module closure, ready for truthful provider eligibility | assembly-presence inference and Communication-specific build machinery |
| 10 | [Provider election and RabbitMQ Transport](r07/R07-10-communication-provider-election.md) | **Passed.** Direct intent elects one semantically eligible provider per lane; rebuilt RabbitMQ carries Transport with confirmed publication, groups, authenticated context, and truthful facts | separate local/external runtimes and legacy RabbitMQ semantics for Entity Transport |
| 11 | [Jobs wake convergence](r07/R07-11-jobs-wake-convergence.md) | **Passed.** Jobs emits one internal bounded Communication signal; local and RabbitMQ providers preserve the ledger-backed latency contract | public `IJobTransport`, the Jobs Messaging bridge, service-location, and unmanaged fire-and-forget publication |
| 12 | [Cache coherence convergence](r07/R07-12-cache-coherence-convergence.md) | **Passed.** Cache owns one key invalidation meaning over a distinct every-node Communication route; Redis layered activation and local/Redis/RabbitMQ proofs replace the generic channel model | public generic coherence SPI, no-op catch-up/coalescing, Cache InMemory/Messaging packages, and legacy adapter resolver |
| 13 | [Pointwise Relationships](r07/R07-13-pointwise-relationships.md) | **Passed.** `Relatives` is one inferred scalar/set/stream Data operation with bounded execution facts | public batch loader, explicit key arguments, and duplicate graph orchestration |

Only the next slice receives a child card. Later rows remain outcomes rather than speculative API
backlogs until their prerequisites pass.

## Execution plan

1. Inventory current ownership, dependency direction, cardinality handling, context transfer, and
   adapter behavior. **Complete.**
2. Ratify semantic laws, DDD boundaries, chokepoints, and failure/inspection behavior. **Complete.**
3. Classify existing types, projects, and public surfaces as keep, absorb, rebuild, rename, or delete.
   **Complete.**
4. Amend the canonical contract and record the architecture decision. **Complete.**
5. Execute R07-01 without changing current durable context behavior. **Complete.** Core owns the typed
   logical-flow state and durable carrier registry; Data, Tenancy, Access, Jobs, and Data.AI are
   migrated, and the affected regression, compatibility, documentation, diff, and privacy gates pass.
6. Complete R07-02's public-document reconciliation and final regression/build/docs/compatibility/
   privacy gates. **Complete.**
7. Automate reverse-dependent package closure before changing Lifecycle's shipped public surface.
   **Complete.** R07-03 proves two synthetic waves and the complete 81-package Data.Core break in a
   registry-reconciled 100-artifact clean room.
8. Restore the exact public-release ratchet before changing Lifecycle's shipped surface. **Complete.**
   R07-04 passes all eight legs from clean commit `50002c262` in 24 minutes 33 seconds without
   publication or remote mutation.
9. Complete canonical Lifecycle as the clean 0.18 child. **Complete.** Host composition, one outer
   Data boundary, migrations, cross-surface proofs, affected regression, automatic lineage, docs, and
   privacy closure pass.
10. Replace generic Pipeline machinery with the typed capability substrate. **Complete.** R07-06
    leaves one lazy Entity-cardinality seam, makes embedding execution pillar-owned, migrates the two
    real consumers, and deletes the DSL without an alias.
11. Complete faithful process-local Entity Transport under foundation `AddKoan()`. **Complete.** The
    public grammar, serialized receiver-group copies, context ingress, bounded acceptance, local
    settlement, shutdown, facts, package admission, and focused closure pass.
12. Complete faithful process-local Entity Events on the coalesced Communication kernel.
    **Complete.** Event occurrence/details/fan-out policy remains lane-owned while host lifecycle,
    handler discovery, context ingress, bounded accounting, and aggregate observation are shared.
13. Complete provider-neutral election and real RabbitMQ Transport. **Complete.** The in-process floor
    and RabbitMQ share one host wire/ingress contract; direct intent changes Transport reach without
    changing Entity code, while Events remain local and unavailable intent fails without fallback.
14. Converge Jobs wake on the internal Communication signal lane. **Complete.** Jobs owns only the
    latency-hint meaning; Communication owns local/network carriage, provider election, health, wire,
    lifecycle, and facts. The old Jobs Messaging package and public transport seam are deleted after
    local and real RabbitMQ parity.
15. Converge Cache coherence on its distinct every-node Communication route. **Complete.** Origin
    filtering, L1-only receipt, layered Redis activation, and TTL-bounded staleness pass while
    speculative channels, catch-up/coalescing, and legacy bridges are deleted.
16. Open each later implementation slice only after its lower boundary passes.
17. Prove in-process semantics before any broker migration or public maturity change.

R07-02 was intentionally additive and preceded the Lifecycle source break. R07-03 removed the package-
lineage stop condition: once R07-04 restores the release floor, public 0.17's lifecycle `Events`
surface can be replaced in one explicit 0.18 wave, with every reverse dependent receiving a fresh
identity automatically.

## Verification

- R07-01 passed its focused, broader-regression, solution-build, strict-docs, compatibility, diff, and
  privacy gates.
- R07-02's Data.Core stream coordinator passes 42/42 focused and 325/325 full, including routed
  source, partition, registered carrier stability, exact caller-order admission, explicit Entity-id
  rejection, and selected/rejected runtime facts.
- R07-02's SQLite provider proof passes 1/1. The shared conformance cell passes for all six qualified
  adapters—SQLite, PostgreSQL, CockroachDB, SQL Server, MongoDB, and Couchbase—including boundary
  ordering for all six admitted scalar types, and fails closed as designed for InMemory, JSON, and
  Redis.
- The real Backup consumer passes 5/5 acceptance and 7/7 full: SQLite pages `2/2/1`, cancellation
  publishes nothing after the bounded stop, and InMemory/JSON reject before query or archive
  publication.
- The former Mongo ZenGarden endpoint-preference failure is resolved by ARCH-0114's uniform layered-
  capability activation. Core Unit passes 112/112 and Mongo 70/70; Couchbase's earlier 17/17 proof is
  corroborated by the final aggregate completing without its prior node-readiness failure.
- The Release solution build passes with 0 errors and 19 reviewed pre-existing warnings. Strict docs,
  skill lint, changed examples, structural claims, compatibility, diff, and privacy gates pass. R07-02
  is closed without a maturity promotion.
- R07-03 packaging passes 52/52, including all-owner bootstrap, canonical current version intent,
  durable exact identities, shared-input fan-out, two breaking waves, and same-source replay. A
  disposable real-repository Data.Core 0.18 break derives 81 closure members and 78 generated markers;
  registry reconciliation
  produces 100 exact artifacts, and package-only FirstUse plus GoldenJourney pass in 4.095s and
  10.591s. The implementation changes no runtime API and performs no publication or remote Git
  mutation.
- R07-04's exact public-release ratchet passes all eight legs from clean commit `50002c262` in 24
  minutes 33 seconds. The bounded two-project solution topology retains every runnable suite; the
  three prior aggregate-only Jobs failures do not recur, no hang timeout fires, and no package,
  release, tag, push, or remote mutation occurs.
- R07-05 passes the complete Lifecycle closure recorded by its child card.
- R07-06 passes cardinality 6/6, Entity language 13/13, Data.AI 86/86, Packaging 54/54,
  affected Release builds, docs lint with 0 errors, generated lockfiles, and stale-surface/diff/privacy
  gates without rerunning release certification.
- R07-07 passes Communication 14/14, Entity language 16/16, Packaging 54/54, a warning-as-error
  Communication build, direct/foundation packs, changed examples 2/2, docs lint with 0 errors, and
  generated FirstUse/GoldenJourney composition locks. No release-certification suite was run.
- R07-08 passes Communication 28/28 and Entity language 20/20, including all retained Transport
  proofs after the shared-runtime coalescence. Communication builds warning-as-error with zero
  warnings/errors; direct/foundation packs, changed examples 4/4, docs lint with 0 errors, and skills
  lint with 0 errors/warnings pass. No release-certification suite was run.
- R07-09 passes the direct-reference provenance closure recorded by its child card.
- R07-10 passes Communication 31/31 and a real RabbitMQ 5/5, plus warning-clean affected builds,
  focused Core provider-priority consumers, an independently versioned connector pack, docs lint with
  0 errors, diff, and privacy gates. No release-certification suite was run.
- R07-11 passes Jobs 77/77, Communication 31/31, and real RabbitMQ 6/6 after moving the ledger-backed
  wake hint onto Communication and deleting the Jobs Messaging seam.
- R07-12 passes Communication 33/33, Cache topology 49/49, Cache Abstractions 51/51, Analyzer 6/6,
  real RabbitMQ 7/7, and real Redis 5/5. Seven touched package owners pack with exact dependency
  floors; the 112-owner inventory excludes the two deleted coherence packages; automatic retirement
  passes 28/28 focused compiler/Git proofs. No release-certification suite was run.
- R07-13 passes the Data.Core Relationships matrix 10/10 and Entity Language 22/22. Data.Core builds
  warning-as-error; S1 compiles the inferred scalar/set/provider-stream grammar; the Data.Core 0.19
  owner packs and the inventory remains 112. Docs, stale-source, diff, and privacy gates pass without
  release certification. The complete Data.Core project is not re-claimed by this child: one aggregate
  attempt exceeded its four-minute slice bound and was terminated.

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
- The target has fewer public mechanisms than the former Entity + Messaging + Pipeline combination.
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
