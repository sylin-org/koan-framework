# Sylin.Koan.Cache.Abstractions technical notes

## Contract ownership

`ICacheStore` is the provider SPI. It carries identity, placement, capability description, and the minimum operation
set needed by the Cache runtime. `ICacheClient` and `ICacheEntryBuilder<T>` are application-facing contracts;
providers do not implement them.

`CacheEntryOptions` is the developer aggregate. It projects into the stricter `CacheReadOptions` and
`CacheWriteOptions` passed to stores. `CacheTier` is consumed by the runtime topology and therefore does not appear
on the store contract.

## Provider laws

- `Name` is stable and unique within one host, compared case-insensitively.
- `Placement` is either Local or Remote and must agree with the host-wide pin it satisfies.
- `Describe` is deterministic and side-effect free.
- `Fetch` returns fresh-or-miss unless `AllowStaleFor` explicitly permits a bounded stale result.
- `EnumerateByTag` uses exact, case-insensitive tag identity; substring matching is invalid.
- Sliding expiration must renew a fresh entry on access if `CacheCaps.SlidingExpiration` is declared.
- Cancellation and provider errors propagate; stores do not silently weaken an explicit operation.

Cache owns provider election, layering, physical segmentation, serialization, coherence meaning, health, and facts.
Adapters own backend mechanics and truthful capability declaration.
