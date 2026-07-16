# Koan Cache architecture

## Boundary

Cache is a semantic pillar, not a storage adapter and not a messaging system.

| Owner | Responsibility |
|---|---|
| Entity/Data | Business-shaped persistence verbs and decorator invocation |
| Cache | policy, key/scope projection, store topology, L1/L2 behavior, singleflight, peer-invalidation meaning |
| Communication | provider election, bounded signal carriage, wire envelope, lifecycle, ingress, health, facts |
| Cache adapters | cache store implementation and, when real, a layered Communication capability |

The data layer never decides how an invalidation travels. A Communication adapter never decides what Cache
does with it.

## Entity entry and control planes

`EntityCachePlan` is the one host-owned decision for whether and how an Entity cache entry exists. It
selects the active Entity policy, rejects unsafe transformed/non-equality-scoped types, parses the key
template, supplies partition/source tokens, and folds managed equality axes. Both the repository
decorator and explicit `entity.Cache.Evict()` consume that resolved plan; neither reconstructs cache
identity independently.

Scalar, finite-set, and async-stream eviction normalize through Data.Core's cardinality helper and run
sequentially in constant memory. The coordinator captures Data routing context and registered Core
carriers once, restores them around deferred enumeration/removal, and returns aggregate counters only.
Typed failures and cancellation retain the confirmed prefix. Selected-tier removal and peer carriage
are not atomic, so no collection or per-item transaction is claimed.

Static `EntityType.Cache.Explain/Flush/Count/Any` remains policy/tag control plane. It is deliberately
separate from pointwise entry eviction and from the governed MCP operational surface.

## Runtime shape

```text
Entity operation / Cache client
          |
          v
   Cache policy + key
          |
          v
   LayeredCache (L1, L2)
          |
          +---- successful write/remove ----+
                                             v
                                  CoherenceCoordinator
                                  (Cache meaning/policy)
                                             |
                                             v
                          Communication FrameworkBroadcasts
                          (election/wire/lifecycle/ingress)
                                             |
                    +------------------------+------------------------+
                    |                         |                        |
              in-process floor        Redis layered adapter      RabbitMQ connector
                    |                         |                        |
                    +------------------------+------------------------+
                                             v
                                  every reachable active node
                                             |
                                             v
                               origin filter -> peer L1 evict
```

`LayeredCache` is composition, not an `ICacheStore`. It reads L1 then L2, backfills L1 after an L2 hit,
writes the selected tiers, and exposes a narrow `EvictLocal` receiver operation. Remote invalidation never
touches L2 and never republishes.

## Store election

The built-in memory store is the local floor. Adapter packages append typed `ICacheStore` candidates.
`CacheTopologyResolver` resolves local and remote tiers independently:

1. explicit `LocalProvider` / `RemoteProvider` pin;
2. highest `[ProviderPriority]` for that placement;
3. stable provider-name tie-break; or
4. no tier.

An invalid explicit pin fails boot with the registered choices. It does not fall back.

## Framework broadcast topology

Cache does not reuse Jobs' competing-group route. They are different semantic contracts:

- Jobs wake: one stable group across replicas; one replica receiving is sufficient because the ledger and poll
  own correctness.
- Cache invalidation: every active node in provider reach must receive because each owns a distinct L1.

Communication therefore exposes two internal lanes. A `FrameworkBroadcasts` binding is node-scoped and gets a
unique host-lifetime identity. Providers must declare `NodeFanOut` to be eligible.

The process-local provider is always present. RabbitMQ maps node bindings to non-durable auto-delete queues while
keeping ordinary receiver groups durable and competing. Redis pub/sub naturally fans out once per subscriber.

## Layered capability activation

`Koan.Cache.Adapter.Redis` describes two capabilities:

- `RedisCacheStore`, a remote Cache store; and
- `RedisCacheCommunicationAdapter`, an every-node Communication carrier.

The Communication carrier declares no active lanes unless Redis is the elected remote Cache store and coherence is
enabled. An active layered candidate may replace the process-local floor without a second direct connector reference.
If a directly intended Communication provider also claims the lane, normal lane election applies. This is the Koan
layered-capability standard: packages may describe adjacent capability, but the capability activates only with its
owning engine.

## Correctness and failure

The only current wire meaning is “evict this key from peer L1.” The signal contains key, optional region, origin
node identity, and occurrence time. Cache applies it only when:

- coherence is active for a layered topology;
- the key is valid; and
- the origin differs from the receiving process.

Publication is deliberately non-blocking at the Cache caller boundary. Communication owns a bounded egress queue.
Queue pressure, provider failure, disconnect, or missed pub/sub delivery can lose a hint; the L1 TTL derived from the
L2 TTL is the staleness bound.

`CoherenceMode.Required` rejects a layered topology whose elected route is only process-local. Direct/explicit
external provider startup failure also fails boot; Koan does not pretend local reach is distributed reach.

## Removed mechanisms

The previous design exposed `ICoherenceChannel<T>`, `ICacheCoherenceChannel`, channel priorities, multiple-channel
publication, no-op catch-up cursors, a timer-based coalescer, a process-static InMemory package, and a legacy
Messaging bridge. None had a second valid consumer or a provider-backed durable replay contract. The multi-channel
coordinator also contradicted its own “winner” documentation.

These surfaces are deleted. Future replay, coalescing, or alternate topology must enter through a concrete use case,
declared Communication capability, owned lifecycle, and executable provider proof.

## Inspection

Communication facts report the elected `FrameworkBroadcasts` carrier, election reason, priority, assurance, and
node-scoped bindings. Cache facts report L1/L2 topology, coherence mode, active state, L1-only receiver semantics,
TTL safety bound, policy count, and each resolved Entity entry plan or cache-safety exclusion. The policy lifecycle
owner initializes once before this composition snapshot, so startup never reports a pre-bootstrap empty registry.
Health includes tier probes plus the active carrier and node identity.

The module boot report contains configuration-derived settings only. It does not service-locate live state or swallow
reporting errors; live decisions come from the shared composition model.

## Executable proof

- Communication unit specs prove process-local delivery and reject providers without `NodeFanOut`.
- RabbitMQ integration proves one broadcast reaches two active hosts in the same mesh.
- Redis integration proves two Cache nodes share L2, select the layered `redis-cache` carrier, evict only the peer L1,
  and observe the new value on the next read.
- Cache topology specs prove L1-only receipt, deterministic store election, fail-loud pins, facts, and health posture.
- Entity cache eviction specs prove shared custom-template identity, captured context, no read-ahead,
  idempotent absence/default-key skips, and typed partial failures/cancellation; the real tenancy suite
  proves same-tenant removal and cross-tenant isolation through a composed SQLite host.

See [ARCH-0075](../decisions/ARCH-0075-koan-cache-pillar.md) and
[ARCH-0113](../decisions/ARCH-0113-entity-capability-communication.md).
