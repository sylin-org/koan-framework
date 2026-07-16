---
type: GUIDE
domain: framework
title: "Koan V1 Reorganization Current Handoff"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.18.0
validation:
  date_last_tested: 2026-07-15
  status: reviewed
  scope: R07-01 through R07-06 passed; faithful local Transport is next
---

# Koan V1 reorganization current handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Current state

- R00 through R06 remain passed. R07 is the only active initiative work item.
- [ARCH-0113](../../decisions/ARCH-0113-entity-capability-communication.md) accepts the greenfield
  semantic-capability rebuild and supersedes the old lifecycle/event/messaging split.
- The canonical [Entity Semantics Contract](../../architecture/entity-semantics-contract.md) now
  reflects that decision.
- [R07-01](work-items/r07/R07-01-core-context-foundation.md) passed. The working tree now has one
  Core-owned typed logical-flow context and durable carrier registry; Data, Tenancy, Access, Jobs, and
  Data.AI have moved to it. The old Data-owned generic slice/carrier APIs and Data-axis carriage hook
  are removed, and the affected regression/docs/privacy gates are green.
- [R07-02](work-items/r07/R07-02-provider-bounded-streaming.md) passed. It keeps
  `IAsyncEnumerable<TEntity>` and composes one honest provider-bounded-page capability beneath it.
  Data.Core passes 42/42 focused and 325/325 full; all six qualified and three fail-closed adapter
  cells pass; SQLite passes 1/1; and the real Backup consumer passes 5/5 acceptance plus 7/7 full.
  Unsupported adapters reject rather than falling back to full-result materialization.
- [R07-03](work-items/r07/R07-03-automatic-package-lineage.md) passed. Its two production concepts—an
  evaluated `PackageGraph` and Git-native `ReleaseLineageCompiler`—automatically mint and prove the
  complete breaking reverse closure.
- [R07-04](work-items/r07/R07-04-public-release-ratchet.md) passed. All five shared TestKits are
  fenced from solution test execution; packaging subprocesses terminate deterministically; Identity
  passes 114/114; and Canon passes unit 35/35 plus integration 6/6. The five Jobs SQLite failures were
  one ownership root: generic source placement, host selection, and SQLite connection lifetime had
  been blurred together. The shared repair passes Jobs core 77/77, Jobs SQLite 79/79 repeatedly and in
  simultaneous complete processes, SQLite 35/35, and Data.Core 349/349. Mongo's stale direct
  Zen Garden preference has now been replaced by the shared layered-capability contract in
  [ARCH-0114](../../decisions/ARCH-0114-layered-capability-activation.md): Core Unit passes 112/112 and
  Mongo passes 70/70. The certification runner limits solution testing to two concurrent projects and
  terminates an inactive test host after five minutes without producing a dump; a packaging contract
  pins both bounds. The final exact ratchet passed all eight legs from clean commit `50002c262` in 24
  minutes 33 seconds. The three prior aggregate-only Jobs failures did not recur, Couchbase completed
  without its earlier node-readiness failure, and no hang timeout fired.
- [R07-05](work-items/r07/R07-05-canonical-lifecycle.md) passed. Host-owned `Entity.Lifecycle`, one
  outer Data boundary, consumer migrations, runtime facts, generated REST/MCP parity, affected
  regression, automatic lineage, docs, and privacy gates are green. The old persistence `Events`
  implementation has no alias.
- [R07-06](work-items/r07/R07-06-typed-capability-substrate.md) passed. Data.Core now owns only lazy
  scalar/set/stream Entity-cardinality normalization. The generic Pipeline builder, mutable envelope,
  feature bags, and every pillar extension are deleted without an alias. Ordinary embedding is
  Lifecycle-owned; explicit subset and whole-collection rebuilds are Data.AI migration operations
  with aggregate outcomes. The two real sample consumers are smaller and business-named.
- Public Messaging guidance is reduced to the truthful v0.17 legacy surface. The former long reference
  described absent attributes, routes, batches, inbox/outbox, retries, and topology guarantees.
- No package was published and no branch was pushed, tagged, or released.

## Validation economy — persistent operating rule

- Ordinary implementation uses the smallest affected fact/project proof.
- Architectural claims add only a named, bounded capability/consumer matrix.
- Full-solution and public-release ratchets run only at tranche, merge, or release-certification
  boundaries, or when explicitly requested. They are not the normal development loop.
- One red certification result is recorded. Diagnose and repair its named owners with focused evidence;
  do not rerun certification until an owner or the certification topology has materially changed.

## Accepted model

There are three public intents and one hidden mechanism:

```csharp
Order.Lifecycle.BeforeUpsert(...);

public sealed class RecordApproval : IHandleEntityEvent<Order, OrderApproved> { /* business code */ }
await order.Events.Raise<OrderApproved>(ct);

public sealed class ImportOrder : IReceiveEntity<Order> { /* business code */ }
await order.Transport.Send(ct);
```

- Data owns `Lifecycle` and never knows Communication.
- One Communication pillar owns distinct Events occurrence semantics and Transport snapshot semantics.
- Scalar, finite-set, and lazy-stream forms share one pointwise meaning; no batch atomicity is implied.
- Data.Core owns only Entity cardinality normalization; each pillar owns its callbacks, execution, and
  outcomes.
- `Raise<E>()` is the primary zero-payload fact form because the source Entity already supplies identity
  and snapshot.
- Every deliberate `Raise` is a new occurrence. Every deliberate `Send` is a new logical send. Internal
  retries retain identity and deduplicate within the declared receiver scope.
- Cross-call content-hash coalescing is rejected because a valid A → B → A transition must not lose the
  second A.
- Local delivery serializes/deserializes too; application handlers never share the sender's mutable
  reference.
- Tenant and other context axes own capture/validation/restoration. Absence suppresses, while an unknown
  axis or malformed/version-invalid payload fails before handler code. Opaque syntax alone is not
  integrity; carriers state generic ingress-trust requirements and sensitive cross-process restoration
  requires authenticated adapter provenance.
- The sole V1 receiver path is an auto-discovered business-named typed handler. Bare static `On` and
  `Receive` registration is deferred because it cannot safely identify a host on its own.
- Await reports channel publication acceptance through a bounded operation summary. Per-item identity,
  receiver dedupe, handler outcome, retries, and dead-lettering are incremental correlated settlement
  facts; local tests get an operation-scoped wait and shutdown gets a separate host drain.

## Zero-configuration delight contract

- Communication must ship, and the foundation package plus `AddKoan()` must activate, the complete
  minimum-priority process-local ring: local
  `Raise` reaches local Event subscriptions and local `Send` targets local Transport receiver groups.
  Communication owns this in-process provider floor; it is not a separately referenced connector.
- A build-generated communication manifest records direct PackageReference/ProjectReference connector
  intent; transitive references cannot hijack selection.
- Connector references add no application routing code or Koan registration; endpoint, credentials,
  trust material, and production availability remain deployment configuration/discovery.
- Election is per lane and logical channel: explicit binding; directly referenced claim or, only when
  none exists, the built-in floor; hard semantic eligibility; fixed delivery-assurance rank; provider
  priority; then stable connector identity. A directly intended but unavailable provider never
  silently falls back to process-local reach. Publishers submit once; every local stable group binds
  once to the same channel.
- UDP may honestly weaken durability or liveness, never fan-out/groups, copy, context, provenance, or
  contract safety. Raw datagrams are ineligible for standard Entity Transport unless those invariants
  are proved. RabbitMQ earns only the guarantees its conformance tests prove.
- V1 has an inferred default channel, an optional business-named channel terminal, and one
  host/deployment binding. Manifest, mesh/contract/channel, outbound adapter, local inbound group,
  receiver filter, and diagnostic plan hash are reported at boot.
- Source `Where`, channel choice, receiver `Where`, and terminal intent form the complete V1 flow
  grammar. Receiver filters run at typed ingress, record terminal filtered settlement, and are never
  confidentiality boundaries. Automatic sender `When`, adapter predicate pushdown, provider lowering,
  mirroring, and failover are deferred.
- V1 mesh joins replicas sharing one application communication manifest and trust boundary.
  Heterogeneous/cross-application flows require a future explicit integration manifest.

## Greenfield disposition

Keep Core facts/capabilities, C# 14 Entity facets, Jobs' ledger/context-safe coalescing, bounded
relationship negotiation, and the carrier's fail-closed behavior.

Ambient typed state/carriers have moved from Data to Core. Persistence `Events` has been replaced by
host-owned `Lifecycle`.
Rebuild Messaging as Communication, Data streaming, lifecycle invocation, InMemory, and RabbitMQ.
Absorb only cardinality normalization from Pipeline into Data.Core, plus Jobs wake and Cache coherence
transport into their correct internal seams. Delete broad arbitrary-class
`Send`, `services.On`, proxy/buffer/interceptors, the unused envelope, the public Pipeline DSL, the
separate InMemory connector, and obsolete bridge packages as their replacements pass.

## Implementation order

1. R07-01: Core-owned typed ambient context and durable carrier, preserving current Jobs/Tenant proofs.
   **Passed.**
2. R07-02: genuine provider-bounded streaming beneath the existing Entity surface. **Passed.**
3. Automate the breaking package closure. **Passed as R07-03.**
4. Restore the exact public-release ratchet without exclusions. **Passed as R07-04.**
5. Rebuild canonical host-owned Lifecycle as a clean 0.18 wave. **Passed as R07-05.**
6. Minimal Data.Core Entity-cardinality adapter, pillar-owned execution, and deletion of the two real
   public Pipeline uses. **Passed as R07-06.**
7. Faithful local Transport under `AddKoan()`.
8. Events occurrence policy on the same kernel.
9. Multi-connector mesh, RabbitMQ parity, Jobs wake, and Cache coherence migration.
10. Secondary pointwise lifts: Relationships, constrained Jobs streams, AI Embed/Index, Cache eviction,
   then Media if a real derivative operation earns it.

Only the next slice has a detailed child card. Do not open broker breadth before local semantics pass.

## Verified

- R07-01's recorded closure remains green: strict docs, diff check, its recorded successful solution build, Core
  257/257, Data context/transactions 35/35, Tenancy 110/110, Access 22/22, Jobs core 77/77 plus durable
  tenancy 11/11, Data.AI 84/84, and Data axes 56/56 plus integration 18/18.
- R07-02 Data.Core streaming passes 42/42 focused and 325/325 full, including exact pages,
  first-yield laziness, no count,
  cancellation/disposal, residual continuation, total-order/overclaim rejection, stable routed
  source/partition/registered carrier context, natural cancellation overloads, and selected/rejected
  runtime facts.
- The focused SQLite provider proof passes 1/1.
- The shared provider-bounded cell passes once each for SQLite, PostgreSQL, CockroachDB, SQL Server,
  MongoDB, and Couchbase, including boundary ordering for every admitted caller-sort type: top-level
  non-nullable `bool`, `byte`, `sbyte`, `short`, `ushort`, and `int`. Only the usual string Entity id
  is admitted as an opaque provider-stable tie-breaker. The matching fail-closed cell passes once each for InMemory,
  JSON, and Redis; none yields or silently enters a complete-source stream fallback.
- The real Backup consumer passes 5/5 acceptance and 7/7 full: SQLite requests pages `2/2/1` and
  publishes the complete
  archive; cancellation during page 2 prevents its completion and archive publication; InMemory and
  JSON reject before query or archive publication.
- R07-02's current artifacts contain no private downstream identity, path, persona, or workflow.
- R07-03 packaging passes 52/52, including all-owner bootstrap, canonical current version intent,
  durable exact identities, shared-input fan-out, two breaking waves, and same-source replay. A
  disposable Data.Core 0.18 rehearsal derives the full 81-package breaking closure and 78 markers.
  Registry reconciliation
  yields 100 verified artifacts; package-only FirstUse passes in 4.095s and GoldenJourney in 10.591s.
  No package or remote Git object changed.
- The Release solution build passes with 0 errors and 19 reviewed pre-existing warnings; strict docs,
  skill lint, changed examples, structural claims, compatibility, diff, and privacy gates pass.
- The exact public-release ratchet passes all eight legs from clean commit `50002c262` in 24 minutes
  33 seconds. Docs lint reports 0 errors / 1567 historical warnings; changed examples pass 25/25,
  skills 20/20, and blueprint lint 1/1. No publication or remote mutation occurred.
- All five shared TestKit projects now evaluate `IsTestProject=false`; the previously aborting Jobs
  helper exits without a test host. The packaging runner now disables reusable MSBuild worker nodes
  for its redirected subprocesses, closing the pipe-lifetime hang across source probes and release
  commands. Executable contracts pass 3/3 and the complete packaging suite passes 53/53 in 1 minute
  20 seconds without an external environment override.
- Identity passes 114/114 after one test-local flow-scope base separated fixture lifetime from ambient
  host selection. The expected failed nested host still rejects correctly; no production fallback or
  lease behavior changed.
- Canon unit passes 35/35 and integration passes 6/6. `ICanonPersistence.GetCanonicalAsync<T>` now
  owns prior-state and rebuild reads, custom-store failures propagate, explicit provider overloads
  scope and restore correctly, and the intentional pre-1.0 breaking contract is recorded as the
  Domain package's 0.18 tier.
- SQLite now resolves generic source configuration only for the provider that owns that source, while
  provider-scoped overrides remain explicit. Repository and Direct use—not mere discovery—record
  adapter participation for readiness. One host-owned lifecycle supplies per-operation connections,
  source-isolated memory databases, lazy directory creation, and deterministic disposal. SQLite passes
  35/35; Data.Core 349/349; Core Unit 112/112; JSON 20/20; Data axes integration 18/18; Web SQLite 49/49;
  Tenancy 110/110; Jobs core 77/77; and Jobs SQLite 79/79 on repeated and simultaneous complete runs.
- R07-05 passes its complete closure: Data.Core 347/347; Data.AI 84/84; Web 111/111; MCP 75/75;
  Identity 114/114; OpenGraph 38/38; SoftDelete 9/9; Backup 7/7; Cache 14/14; Entity language
  11/11; Core Unit 112/112; Canon 35/35 + 6/6; Packaging 54/54. Release build has 0 errors,
  docs lint has 0 errors, and examples/skills/blueprint/surface/diff/privacy gates pass.
- R07-06 passes its bounded closure: cardinality 6/6; Entity language 13/13; Data.AI 86/86;
  Packaging 54/54; affected Release builds; docs lint 0 errors; generated source-application
  lockfiles; stale-surface, diff, and privacy inventories. No release-certification suite was rerun.

## Next safe action

Open faithful local Transport as the next bounded child. Start with fresh exploration of the current
Messaging contracts, in-process adapter behavior, host discovery, serialization/copy semantics,
logical-flow context carriage, and boot facts. Build only the process-local `AddKoan()` path first;
do not open Events, brokers, RabbitMQ compatibility, or multi-connector election until scalar/set/
stream Transport semantics pass locally.

## Repository boundary

The branch is `dev`. Preserve ignored/untracked evaluator and scratch material under `tmp/`; never
stage it. Do not inspect or name private downstream applications. Do not publish, push, tag, or release
without a separate operator request.
