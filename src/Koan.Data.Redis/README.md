# Sylin.Koan.Data.Redis

Redis provider for Koan data - key-value storage with options binding, health checks, and simple scanning.

- Target framework: net9.0
- License: Apache-2.0

## Capabilities

- Connection multiplexer management via options
- Basic scan/query over keys for simple filters
- Health checks and minimal metrics hooks

## Install

```powershell
dotnet add package Sylin.Koan.Data.Redis
```

## Minimal setup

- Configure `ConnectionStrings:Redis` or `Koan:Data:Redis:ConnectionString`.
- Avoid embedding credentials; use environment or secret stores.

## Usage - safe snippets

- Prefer first-class statics on entities; fall back to repository only when needed.

```csharp
// Save and get entity
await Session.Save(new Cart { Id = id, Items = [] }, ct);
var cart = await Session.Get(id, ct);

// Stream keys if the set is large
await foreach (var c in Session.AllStream(batchSize: 1000, ct))
{
	// process
}
```

See TECHNICAL.md for options and operational considerations.

## References

- Redis adapter notes: `~/20-redis-adapter.md`
- Data access reference: `~/reference/data-access.md`
