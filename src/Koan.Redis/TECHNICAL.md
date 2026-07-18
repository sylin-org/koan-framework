# Sylin.Koan.Redis technical notes

`RedisModule` owns endpoint configuration, autonomous discovery, orchestration/Aspire contribution, connection
construction, and host-lifetime disposal. It registers StackExchange.Redis's `IConnectionMultiplexer` for ordinary
single-endpoint consumers and implements `IRedisConnectionProvider` for source-aware modules.

The provider creates connections lazily and reuses one multiplexer for each distinct connection string. The owning
host disposes every connection it created. The standard DI alias owns disposal of the default multiplexer when it is
resolved; the provider owns named-source connections and the default when no alias was resolved, avoiding duplicate
lifecycle ownership. Malformed and unavailable endpoints fail at this boundary with redacted, Redis-specific
corrections.

This package has no Data, Cache, or Communication semantics. A Data adapter decides source/database routing and Data
health; a Cache adapter decides tier placement, cache capabilities, and invalidation meaning. Consequently a
Cache-only application does not activate a Data provider merely because both can use Redis.

Configuration precedence is `ConnectionStrings:Redis`, `Koan:Redis:ConnectionString`, `REDIS_URL`, then
`REDIS_CONNECTION_STRING`; absent explicit configuration, Koan discovery resolves the endpoint or falls back to
`localhost:6379`/`redis:6379` according to the host environment.
