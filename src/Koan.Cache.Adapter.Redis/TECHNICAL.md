# Sylin.Koan.Cache.Adapter.Redis technical notes

`RedisCacheStore` is pure Remote Cache storage. `RedisCacheCommunicationAdapter` is a layered Communication
candidate that remains dormant unless Redis owns the compiled Remote Cache route. Cache owns invalidation meaning;
Communication owns carriage, election, lifecycle, ingress, health, and facts.

The package currently consumes `IConnectionMultiplexer` from `Sylin.Koan.Data.Connector.Redis`. Consequently,
referencing this adapter also references the functional Data Redis connector. This is a known cross-functional
backend boundary, not a model for new adapters. Graduation of this package requires a joint Redis backend/Data
decision so one shared connection contract can exist without activating an unrelated capability.

The provider declares priority 100 for both Remote Cache storage and its layered broadcast candidate. Direct
Communication intent may still win normal lane election. Redis pub/sub does not claim replay or delivery
settlement; receiver behavior is L1-only and origin-filtered.
