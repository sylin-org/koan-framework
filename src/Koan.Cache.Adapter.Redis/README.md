# Sylin.Koan.Cache.Adapter.Redis

Redis Remote Cache provider plus a layered every-node Communication capability for peer L1 invalidation.

## Install

```powershell
dotnet add package Sylin.Koan.Cache.Adapter.Redis
```

Keep `builder.Services.AddKoan()`. Referencing the package contributes Redis as the Remote Cache candidate; when it
is elected, its broadcast capability becomes eligible automatically.

## Meaningful result

With a reachable Redis backend, two application nodes share the Remote entry and peer L1 invalidations without any
Cache-specific registration or transport code.

Connection ownership comes from the transitive `Sylin.Koan.Redis` backend. Configure `ConnectionStrings:Redis` once;
referencing this package does not activate a Redis Data provider. When Data Redis is also referenced, both adapters
share the same host-owned default `IConnectionMultiplexer`.

Cache-specific settings remain under `Cache:Redis`: key/tag prefixes, database, instance name, channel name, and
bounded ingress/publish behavior.

## Guarantees and limits

- Redis is the Remote tier and supports tags, sliding expiration, bounded stale serving, and binary payloads.
- Backend durability depends on the operator's Redis persistence and replication configuration; the adapter does
  not declare `CacheCaps.Persistent` merely because the backend is remote.
- Peer invalidation activates only when Redis is the elected Remote Cache route and Cache coherence permits it.
- Redis pub/sub is best effort. It provides no replay, remote settlement, or missed-invalidation catch-up.
- L1 TTL bounds stale residence when a broadcast is lost.

See [TECHNICAL.md](TECHNICAL.md) for the backend and capability boundaries.
