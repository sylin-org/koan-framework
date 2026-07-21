---
type: SPEC
domain: framework
title: "R07-12 - Cache Coherence Convergence"
audience: [architects, maintainers, developers, operators, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.19.0
validation:
  date_last_tested: 2026-07-15
  status: passed
  scope: Cache peer invalidation over process-local, Redis, and RabbitMQ every-node Communication
---

# R07-12 — Cache coherence convergence

- Tranche: `T6 — semantic capability ring`
- Status: `passed`
- Depends on: R07-11
- Unlocks: secondary Entity capability lifts without retaining legacy Messaging bridges
- Owner: Cache invalidation meaning and TTL safety; Communication carriage/election/lifecycle

## Meaningful outcome

A Cache-enabled application needs no transport package or configuration for the complete local ring.
When Redis becomes the active L2, its layered Redis broadcast capability activates automatically.
When a directly intended Communication mesh such as RabbitMQ carries the route, Cache code does not change.

The developer promise is narrow and strong: a successful peer invalidation is delivered once to every
active node within provider reach; the writer filters its echo; peers evict L1 only; and L1 TTL bounds
staleness when a best-effort signal is lost.

## Architecture

- `CommunicationLane.FrameworkBroadcasts` is distinct from Jobs' competing-group `FrameworkSignals`.
- Node-scoped bindings have unique host-lifetime identities; providers must declare `NodeFanOut`.
- The process-local provider is the minimum floor. RabbitMQ uses non-durable auto-delete node queues.
- Active layered candidates may replace the floor without a second direct reference. Dormant layered
  candidates declare zero lanes and are neither elected nor started.
- `Koan.Cache.Adapter.Redis` activates its `redis-cache` candidate only when Redis is the elected remote
  store and Cache coherence is enabled.
- Cache emits one internal key signal through Communication's bounded non-blocking egress.
- `CoherenceCoordinator` owns mode, origin filtering, L1-only application, logs, health, and Cache facts.

## Principal deletion

- public `ICoherenceChannel<T>` and `ICacheCoherenceChannel`;
- speculative `CoherenceCapabilities`, no-op catch-up methods, and `CursorStore`;
- multi-channel publication and the mismatch between claimed winner election and actual publish-to-all behavior;
- timer/fire-and-forget `CoherenceCoalescingBuffer` and its options;
- unused `EvictByTag` / `EvictAll` wire kinds; tag flush emits proven key removals;
- `Koan.Cache.Coherence.InMemory` and its process-static bus;
- `Koan.Cache.Coherence.Messaging`, `IMessageProxy`, and service-location bridge;
- the old reflective `ICacheAdapterRegistrar`, resolver, descriptor, and obsolete registration shim.

## Delight contract

- Application developers write `[Cacheable]` and normal Entity business operations, not bus/channel code.
- An active adapter contributes adjacent capability without requiring a second registration decision.
- Invalid explicit provider pins fail with candidates rather than silently selecting another provider.
- Coding agents see one Cache policy model and one internal Communication boundary, not competing channel APIs.
- Operators see topology, mode, elected carrier, assurance, L1-only receipt, and TTL safety in canonical facts.
- Reviewers can trace the complete key-invalidation path without crossing a generic message bus or service locator.

## Acceptance

- The process-local floor delivers a typed broadcast and rejects providers lacking `NodeFanOut`.
- Two RabbitMQ hosts in one mesh each receive the same framework broadcast through distinct ephemeral queues.
- Two Redis Cache hosts elect the layered `redis-cache` provider; a write on A invalidates B's L1 and B reads
  the new value from shared L2.
- Cache topology resolution fails loud on invalid explicit pins.
- Cache health and facts report the real provider/posture without `AppHost.Current` report-time lookup.
- Deleted package identities are absent from the solution and package inventory.

## Explicit non-claims

- durable replay or catch-up;
- retry, deduplication, exactly-once invalidation, or remote settlement;
- multiple simultaneous coherence carriers;
- public application-authored framework signals;
- global/region-wide wire flush without a provider-neutral enumeration contract; or
- production certification beyond the named Redis/RabbitMQ proofs.

## Evidence

- `Koan.Communication.Tests`: 33/33, including local every-node delivery and hard rejection when a
  candidate lacks `NodeFanOut`.
- `Koan.Tests.Cache.Topology`: 49/49; Cache Abstractions: 51/51; Cache Analyzer: 6/6.
- `Koan.Communication.Connector.RabbitMq.Tests`: 7/7 against RabbitMQ 3.13, including two hosts in
  one mesh each receiving the same broadcast through distinct ephemeral node queues.
- `Koan.Cache.Adapter.Redis.Tests`: 5/5 against real Redis, including layered activation, peer L1
  eviction, shared-L2 reread, facts, and lifecycle cleanup.
- Communication, Cache, the Redis adapter, and the RabbitMQ connector build warning-as-error. Seven
  touched package owners pack successfully after a focused restore; nuspec inspection proves exact
  Cache-to-Communication and connector dependency floors.
- The packaging inventory contains 112 independently versioned owners and neither deleted Cache
  coherence package identity. Automatic lineage records deleted owners as permanent retirements,
  including during the first projection, without planning replacement artifacts or operator input;
  its focused compiler/Git matrix passes 28/28. Documentation, stale-source, diff, and privacy checks pass.
- No release-certification suite runs in this child.

## Acceptance result

- Outcome: PASS
- Date: 2026-07-15
- Follow-up: admit secondary Entity capability lifts one business proof at a time; do not generalize
  the internal routes into an application bus.
- Reviewer: Codex implementation and executable evidence under maintainer approval.
