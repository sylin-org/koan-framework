# Sylin.Koan.Cache.Adapter.Sqlite technical notes

## Activation and election

`SqliteCacheModule` appends `SqliteCacheStore` through standard `IEnumerable<ICacheStore>` DI. Priority 50 wins
automatic Local election over Memory priority 10. `Cache:LocalProvider=sqlite` is an optional fail-closed host pin,
not a required setup step.

## Storage model

`cache_entries` owns payload, content kind, runtime type, absolute/stale expirations, serialized tag compatibility
data, and sliding TTL. `cache_entry_tags` owns exact normalized tag membership with a cascading foreign key.
Enumeration joins on tag equality with `NOCASE`; it never uses `LIKE`.

Initialization is idempotent. It creates missing schema, adds `sliding_ttl_ms` to an older entry table, migrates
legacy tag values into the normalized index, enables foreign keys, and selects WAL mode. Writes update the entry and
its tag index in one SQLite transaction.

A fresh read renews `absolute_expiration_utc` from `sliding_ttl_ms` and preserves the configured stale window.
Expired rows beyond their stale ceiling are removed on direct access. Tag enumeration returns expiration metadata
so the Cache runtime can remove expired matches during count/flush. The adapter has no background sweeper.

## Limits

SQLite persistence is process-local infrastructure, not distributed durability or coherence. File placement,
backup, encryption, and filesystem durability remain operator responsibilities.
