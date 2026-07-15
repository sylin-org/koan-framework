---
type: GUIDE
domain: framework
title: "Koan V1 Reorganization Current Handoff"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: reviewed
  scope: R07 semantic capability architecture accepted; Core context foundation next
---

# Koan V1 reorganization current handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Current state

- R00 through R06 remain passed. R07 is the only active initiative work item.
- [ARCH-0113](../../decisions/ARCH-0113-entity-capability-communication.md) accepts the greenfield
  semantic-capability rebuild and supersedes the old lifecycle/event/messaging split.
- The canonical [Entity Semantics Contract](../../architecture/entity-semantics-contract.md) now
  reflects that decision.
- No production implementation has started under R07. The first executable child is
  [R07-01](work-items/r07/R07-01-core-context-foundation.md).
- Public Messaging guidance is reduced to the truthful v0.17 legacy surface. The former long reference
  described absent attributes, routes, batches, inbox/outbox, retries, and topology guarantees.
- No package was published and no branch was pushed, tagged, or released.

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

- The foundation package plus `AddKoan()` ultimately provides the complete process-local ring: local
  `Raise` reaches local Event subscriptions and local `Send` targets local Transport receiver groups.
- A build-generated communication manifest records direct PackageReference/ProjectReference connector
  intent; transitive references cannot hijack selection.
- Connector references add no application routing code or Koan registration; endpoint, credentials,
  trust material, and production availability remain deployment configuration/discovery.
- Election is per logical channel: explicit binding, hard semantic eligibility, direct-reference
  intent, fixed delivery-assurance rank, then stable connector identity. Publishers submit once; every
  local stable group binds once to the same channel.
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

Move ambient typed state/carriers from Data to Core. Rename persistence `Events` to `Lifecycle`.
Rebuild Messaging as Communication, Data streaming, lifecycle invocation, InMemory, and RabbitMQ.
Absorb only cardinality normalization from Pipeline into Data.Core, plus Jobs wake and Cache coherence
transport into their correct internal seams. Delete broad arbitrary-class
`Send`, `services.On`, proxy/buffer/interceptors, the unused envelope, the public Pipeline DSL, the
separate InMemory connector, and obsolete bridge packages as their replacements pass.

## Implementation order

1. R07-01: Core-owned typed ambient context and durable carrier, preserving current Jobs/Tenant proofs.
2. Data truth: canonical host-owned Lifecycle plus genuine bounded streaming.
3. Minimal Data.Core Entity-cardinality adapter, pillar-owned execution, and deletion of the two real
   public Pipeline uses.
4. Faithful local Transport under `AddKoan()`.
5. Events occurrence policy on the same kernel.
6. Multi-connector mesh, RabbitMQ parity, Jobs wake, and Cache coherence migration.
7. Secondary pointwise lifts: Relationships, constrained Jobs streams, AI Embed/Index, Cache eviction,
   then Media if a real derivative operation earns it.

Only the next slice has a detailed child card. Do not open broker breadth before local semantics pass.

## Verified

- `pwsh -NoProfile -File scripts/build-docs.ps1 -Strict` passes after the architecture and public-truth
  changes.
- `git diff --check` passes.
- The changed architecture artifacts contain no private downstream identity, path, persona, or
  workflow.
- R07 code evidence was independently inventoried across Data Lifecycle/streams, ambient carriers,
  Jobs, Cache coherence, Pipelines, Messaging Core, InMemory, and RabbitMQ.

## Next safe action

Claim R07-01, run the mandatory exploration workflow, then add a failing Core-owned context/carrier
proof before moving implementation. Preserve the existing Data context, Tenancy, Jobs durable-hop,
parallel Tenant A/B, absent-context suppression, unknown-axis refusal, and repeated-host behavior.

Do not add Events, Transport, a router, a unit-of-work coordinator, or Messaging compatibility aliases
inside R07-01.

## Repository boundary

The branch is `dev`. Preserve ignored/untracked evaluator and scratch material under `tmp/`; never
stage it. Do not inspect or name private downstream applications. Do not publish, push, tag, or release
without a separate operator request.
