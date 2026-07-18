# Sylin.Koan.Redis.Abstractions

Inert connection-lifecycle vocabulary for Koan modules that share a Redis backend.

## Install

Application developers do not normally reference this package. Reference a functional Redis adapter such as
`Sylin.Koan.Data.Connector.Redis` or `Sylin.Koan.Cache.Adapter.Redis`; it brings the backend implementation
transitively and keeps `AddKoan()` as the only bootstrap call.

Module authors that only need the cross-module contract can reference it without activating Redis:

```powershell
dotnet add package Sylin.Koan.Redis.Abstractions
```

## Meaningful result

Module authors use `IRedisConnectionProvider` only when they need source-aware access to more than the default
endpoint. Simple Redis consumers should inject StackExchange.Redis's standard `IConnectionMultiplexer`.

This lets a capability compile against Redis connection ownership while remaining inert until a functional Redis
backend is present.

## Guarantees and limits

- This assembly contains no `KoanModule`, discovery adapter, or backend implementation.
- Referencing it cannot activate Redis, open a connection, or add `IConnectionMultiplexer` to dependency injection.
- `IRedisConnectionProvider` is a module integration contract, not an application bootstrap requirement.
- Runtime consumers require the functional `Sylin.Koan.Redis` package, normally supplied transitively by a Redis
  adapter.

See [TECHNICAL.md](TECHNICAL.md) for the contract boundary.
