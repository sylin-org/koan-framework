# Sylin.Koan.Cache.Adapter.Redis technical notes

`RedisCacheStore` is pure Remote Cache storage. `RedisCacheCommunicationAdapter` is a layered Communication
candidate that remains dormant unless Redis owns the compiled Remote Cache route. Cache owns invalidation meaning;
Communication owns carriage, election, lifecycle, ingress, health, and facts.

The package consumes the standard `IConnectionMultiplexer` registered by `Sylin.Koan.Redis`. Endpoint discovery,
connection pooling, orchestration, and disposal therefore have one backend owner. Cache-only applications do not
activate Data Redis; applications that reference both adapters reuse the same default connection.

The provider declares priority 100 for both Remote Cache storage and its layered broadcast candidate. Direct
Communication intent may still win normal lane election. Redis pub/sub does not claim replay or delivery
settlement; receiver behavior is L1-only and origin-filtered.
