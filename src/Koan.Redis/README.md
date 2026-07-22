# Sylin.Koan.Redis

Supported shared Redis backend lifecycle for Koan adapters.

## Install

Application developers normally install a semantic adapter instead of this package directly:

```powershell
dotnet add package Sylin.Koan.Data.Connector.Redis
# and/or
dotnet add package Sylin.Koan.Cache.Adapter.Redis
```

Module authors building a Redis-backed capability can reference the backend directly:

```powershell
dotnet add package Sylin.Koan.Redis
```

## Meaningful result

Keep `builder.Services.AddKoan()` and configure the common endpoint once when discovery is not appropriate:

```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}
```

Referencing either adapter brings this backend transitively. Referencing both still creates one backend module and one
default `IConnectionMultiplexer` for the host. Cache and Data retain their own semantic options and health decisions.

Automatic discovery uses local/container/Aspire conventions. `Koan:Redis:DisableAutoDetection` disables probing and
uses the environment-appropriate default endpoint.

## Guarantees and limits

- This is the functional supported backend; `AddKoan()` activates it when a functional Redis adapter brings it
  into the application.
- One host-owned connection is shared for each exact normalized Redis connection string.
- `ConnectionStrings:Redis` is the canonical default endpoint; adapter-specific settings do not duplicate backend
  ownership.
- Referencing this functional package activates Redis discovery and lifecycle through `AddKoan()`; referencing only
  `Sylin.Koan.Redis.Abstractions` does not.
- The backend owns connectivity, not Cache or Data semantics. Each consuming adapter still owns its database,
  routing, readiness, and capability decisions.
- Automatic discovery selects an endpoint; it does not provision Redis. Use Aspire, Compose, Docker, a managed
  service, or another standard topology owner to run it.
- A malformed or unavailable selected endpoint fails correctively at connection use; the backend does not silently
  substitute another service.

See [TECHNICAL.md](TECHNICAL.md) for resolution order, ownership, and failure behavior.
