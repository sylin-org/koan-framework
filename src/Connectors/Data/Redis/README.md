# Sylin.Koan.Data.Connector.Redis

Redis provider for Koan data - key-value storage with options binding, health checks, and simple scanning.

- Target framework: net10.0
- License: Apache-2.0

## Capabilities

- Connection multiplexer management via options
- Basic scan/query over keys for simple filters
- Health checks and minimal metrics hooks
- Explicitly does not advertise `DataCaps.Query.ProviderBoundedPaging`

## Install

```powershell
dotnet add package Sylin.Koan.Data.Connector.Redis
```

## Minimal setup

- Configure `ConnectionStrings:Redis` or `Koan:Data:Redis:ConnectionString`.
- Avoid embedding credentials; use environment or secret stores.

## Usage - safe snippets

- Prefer first-class statics on entities; fall back to repository only when needed.

```csharp
// Save and get entity
await new Cart { Id = id, Items = [] }.Save(ct);
var cart = await Cart.Get(id, ct);

// Materialize deliberately small sets, or request a caller-visible numbered page
var knownSmall = await Cart.All(ct);
var secondPage = await Cart.Page(2, 100, ct);
```

## Streaming boundary

`AllStream` and `QueryStream` reject correctively with `QueryStreamRejectedException` before yielding
because the current Redis query path can scan the keyspace before slicing. Koan does not hide that scan
behind a streaming-shaped API.

Use `All`/`Query` only for deliberately small sets, or `FirstPage`/`Page` when a bounded result returned
to application code is sufficient. Numbered pages do not make Redis keyspace scanning provider-bounded.

See TECHNICAL.md for options and operational considerations.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

