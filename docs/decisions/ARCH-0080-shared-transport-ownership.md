# ARCH-0080: Shared Transport Ownership

**Status**: Accepted
**Date**: 2026-05-16
**Deciders**: Enterprise Architect
**Scope**: Adapters that register shared transport resources (`IConnectionMultiplexer`, future: `IConnection` for RabbitMQ, `IHttpClientFactory`, etc.)
**Related**: ARCH-0075 (cache pillar), ARCH-0079 (integration tests as canon), ARCH-0078 (SWR opt-in)

---

## Context

Phase 2 of the `feat/koan-cache-pillar` work surfaced a cross-pillar registration race: both `Koan.Data.Connector.Redis` and `Koan.Cache.Adapter.Redis` registered `IConnectionMultiplexer`. The data connector used `AddSingleton` (forced); the cache adapter used `TryAddSingleton` (skipped if present). The data connector's registration therefore won, and its connection-string resolution read from a different config key (`Koan:Data:Redis:ConnectionString`) than the cache adapter (`Cache:Redis:Configuration`). Apps that set only the cache adapter's key silently connected to the wrong Redis (the data connector's default discovery resolved to `localhost:6379`).

The current state of the branch documents this as a "cross-pillar workaround" in two places:
- The cache-pillar bootstrap smoke (`CachePillarBootstrapSpec`) sets both config keys to the same value.
- The Data.Core / Storage / Messaging pillar smokes (`DataCorePillarBootstrapSpec`, `StoragePillarBootstrapSpec`, `MessagingCorePillarBootstrapSpec`) supply `Koan:Data:Redis:ConnectionString = "localhost:0,abortConnect=false,..."` so the multiplexer construction doesn't fail when Redis isn't running.

Both workarounds expose the same root cause: **two packages racing to own a shared resource**.

This isn't unique to Redis. The same pattern would manifest for:
- `IConnection` (RabbitMQ) when both `Messaging.Connector.RabbitMq` and a future `Cache.Coherence.RabbitMq` need it.
- `IHttpClientFactory` (HTTP) — already centrally owned by `Microsoft.Extensions.Http`, but Koan AI adapters and web auth adapters all consume it.
- Future shared databases (Postgres `NpgsqlDataSource`, Mongo `IMongoClient`) where two pillars want one connection pool.

The framework needs a **canonical ownership pattern** for shared transports. Without it, every consumer-pair will reinvent the same race (and the same workarounds).

### Forces

1. **One transport, one pool.** `StackExchange.Redis` recommends one multiplexer per app for connection pooling. RabbitMQ's `IConnection` is similarly singleton-by-design. The framework's registrations should reflect this: each shared transport gets one owner, not N+1.
2. **One config key, one source of truth.** Multiple keys for the same resource is a bug waiting to happen. The owner reads one canonical key; consumers don't re-resolve it.
3. **Reference = Intent must compose cleanly.** Users should be able to reference any combination of adapters and have them cooperate. Adding `Koan.Cache.Adapter.Redis` shouldn't change the multiplexer chosen by `Koan.Data.Connector.Redis` — only add a consumer of it.
4. **Failure surfaces should be clear.** If a consumer adapter is referenced without its owner, the error should point at the missing package, not produce silent fallback or cryptic DI exceptions.

---

## Decision

Each **shared transport resource** has exactly one **canonical owner package**. Other packages that need the same resource **declare a dependency on the owner via project reference and inject the resource via DI** — they do not register it themselves.

### Ownership assignments

| Resource | Owner package | Canonical config key | Consumers (current) |
|---|---|---|---|
| `IConnectionMultiplexer` (Redis) | `Koan.Data.Connector.Redis` | `Koan:Data:Redis:ConnectionString` | `Koan.Cache.Adapter.Redis`, `Koan.Service.Inbox.Connector.Redis`, future Cache.Coherence.Redis-Streams |
| `IConnection` (RabbitMQ) | `Koan.Messaging.Connector.RabbitMq` | `Koan:Messaging:RabbitMq:ConnectionString` (TBD) | `Koan.Cache.Coherence.Messaging` (via `IMessageBus`, not `IConnection` directly — see notes) |
| `IHttpClientFactory` | `Koan.Core` (via `Microsoft.Extensions.Http`) | n/a (no connection string) | All AI adapters, Web.Auth adapters |
| Postgres `NpgsqlDataSource` (future) | `Koan.Data.Connector.Postgres` | `Koan:Data:Postgres:ConnectionString` | `Koan.Data.Connector.PGVector` |

### Owner responsibilities

The canonical owner:
1. Registers the shared resource via `services.AddSingleton<TResource>(factory)`. Plain `AddSingleton`, not `TryAddSingleton` — the owner asserts ownership.
2. Reads the connection string from the canonical config key (e.g., `Koan:Data:Redis:ConnectionString`).
3. Handles autonomous discovery / readiness / health probes for the transport.
4. Documents the resource as owned by this package (README + boot-report description).

### Consumer responsibilities

Consumer packages:
1. **Do not register** the shared resource. No `TryAddSingleton<TResource>` calls.
2. Add the owner as a `<ProjectReference>` in csproj (already true for `Koan.Cache.Adapter.Redis → Koan.Data.Connector.Redis`).
3. Inject the resource via constructor parameters in their internal services (e.g., `RedisCacheStore(IConnectionMultiplexer multiplexer, ...)`).
4. **Keep their own adapter-specific options** — for the cache adapter, that's `KeyPrefix`, `TagPrefix`, `ChannelName`. Connection-string options are removed.

### What happens if a consumer is referenced without its owner

The DI container's standard error suffices: when the consumer's auto-registrar (or first service resolve) requests the unregistered type, .NET throws `InvalidOperationException: No service for type 'IConnectionMultiplexer' has been registered.` The error points at the type directly. The consumer's README and boot-report description must call out the owner-package dependency.

**Owner packages SHOULD be project-referenced from their consumers** (already true for the Redis case) so the dependency is transitively guaranteed by NuGet/csproj. The consumer's README confirms this. A user who explicitly removes the transitive reference is opting out of the contract and accepts the diagnostic.

---

## What changes in this ADR's implementation

For the **Redis case** (the only one applied in this branch):

1. **`Koan.Cache.Adapter.Redis.KoanAutoRegistrar`**: remove the `TryAddSingleton<IConnectionMultiplexer>` factory registration and the `ResolveConnectionString` helper. Trust that `Koan.Data.Connector.Redis` (referenced transitively) registered the multiplexer.
2. **`RedisCacheAdapterOptions`**: remove the `Configuration` property (no longer needed — connection-string resolution is the data connector's responsibility). `KeyPrefix`, `TagPrefix`, `Database`, `InstanceName`, `TagIndexCapacity` stay. The cache adapter's `Cache:Redis:Configuration` config key becomes a no-op.
3. **`CacheConstants.Configuration.Redis.Configuration`**: removed (was the constant for the now-deleted config key).
4. **Bootstrap smokes**: remove the `Koan:Data:Redis:ConnectionString = "localhost:0,abortConnect=false,..."` workaround from `DataCorePillarBootstrapSpec`, `StoragePillarBootstrapSpec`, `MessagingCorePillarBootstrapSpec`. Without the cache adapter registering its own multiplexer, the data connector's factory is the only one — and it's already tolerant of misconfiguration in these tests because it doesn't run unless something requests `IConnectionMultiplexer`. The cache adapter's stores/channels DO request it (via DI), but the request only fires at host startup when the coherence coordinator's channels are constructed. With the CoherenceCoordinator-tolerance fix from earlier on this branch, that resolution failure no longer kills the host.

For **future transports** (RabbitMQ, Postgres, etc.): the pattern is documented but not yet applied. Follow-up branches handle each as the need surfaces.

---

## Consequences

### Positive

- **One owner per shared resource.** Eliminates the registration race class.
- **One config key per resource.** No more "the cache silently connected to the wrong Redis."
- **Clearer error surfaces.** Missing dependencies produce standard DI errors pointing at the missing type, not silent misbehavior.
- **Adapter test simplification.** Removes the `abortConnect=false` workaround from three integration specs. Tests document the canonical pattern, not workarounds for the race.
- **Forward-compat.** New transports adopt the pattern from day one. Future cross-pillar conflicts caught at design review, not runtime.

### Negative

- **Breaking change for `Cache:Redis:Configuration`.** Apps that set this config key will see the cache use whatever the data connector resolved instead. Mitigation: greenfield framework per ARCH-0075; no released users. The change is documented in cache.md and the upgrade-note section of the v0.7 release.
- **Tighter coupling between cache adapter and data connector.** The cache adapter can no longer be used standalone (without referencing `Koan.Data.Connector.Redis`). Mitigation: the csproj already requires it; this ADR formalizes what was already true in practice.

### Neutral

- The data connector's `IConnectionMultiplexer` lifecycle is unchanged. Its discovery, options binding, and connection-string resolution all stay. The cache adapter just becomes one of its consumers.

---

## Notes for reviewers

- This ADR is **narrow by design**. It codifies the pattern and applies it to one transport (Redis multiplexer ownership). The principle generalizes; the implementation work for each future transport is a separate, evidence-driven decision.
- The Phase 3.3 bootstrap smokes on this branch are the proof point: they previously documented the race with a "set both keys" workaround. After this ADR's implementation, the workaround collapses to "set the one canonical key" — the cache adapter no longer reads its own connection string.
- RabbitMQ and Postgres ownership assignments are listed in the table as **forward declarations** — they're consistent with the pattern but not implemented in this branch.
- The `IHttpClientFactory` row in the table records existing state (Microsoft.Extensions.Http owns it). This is informational; no Koan code changes required.

### Residual concern: the data connector's eager-connect

After this ADR's implementation, the pillar boot smokes STILL need an `abortConnect=false` setting on the canonical `Koan:Data:Redis:ConnectionString` key. The reason is upstream of ARCH-0080: `Koan.Data.Connector.Redis`'s `RegisterConnectionMultiplexer` factory calls `ConnectionMultiplexer.Connect()` eagerly, which throws when Redis is unreachable. ARCH-0080 makes the cache adapter consume rather than register — it doesn't change the data connector's factory.

A follow-up branch should make the data connector's factory tolerant by default (return a non-connected multiplexer that the rest of the host can hold safely; report the connection failure through health probes instead of throwing at construction). At that point the `abortConnect=false` workaround disappears from the test specs. Until then, the spec remarks document it.

This is a real concern but it lives in `Koan.Data.Connector.Redis`, not the cache pillar. Captured here so the next branch picks it up explicitly.
