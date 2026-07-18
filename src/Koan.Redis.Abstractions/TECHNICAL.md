# Sylin.Koan.Redis.Abstractions technical notes

`IRedisConnectionProvider` is the narrow cross-module seam for a host-owned Redis connection pool. It exposes the
resolved default endpoint, the default multiplexer, and an explicit-endpoint lookup. Implementations must reuse one
multiplexer per distinct connection string and dispose created connections with the owning host.

The default connection string can contain credentials. Consumers must not publish it without Koan redaction.

This contract package deliberately contains no options binding, discovery, orchestration, health policy, or
`KoanModule`. Those behaviors belong to the functional `Sylin.Koan.Redis` package. Cache/Data semantics remain in
their respective pillars.
