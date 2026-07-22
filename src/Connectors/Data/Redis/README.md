# Sylin.Koan.Data.Connector.Redis

Supported Redis provider for Koan Entity data: keyed storage, native TTL, fast keyed removal, source/database routing,
participation-aware health, and deliberately limited scan-based queries.

- Target framework: net10.0
- License: Apache-2.0

## Capabilities

- Supported for keyed, ephemeral, and modest-cardinality Entity workloads
- Shared backend connection pooling without activating unrelated pillars
- Basic scan/query over keys for simple filters
- Native TTL and fast keyed removal
- Health checks and minimal metrics hooks
- Explicitly does not advertise `DataCaps.Query.ProviderBoundedPaging`

## Install

```powershell
dotnet add package Sylin.Koan.Data.Connector.Redis
```

## Minimal setup

- Configure `ConnectionStrings:Redis` when automatic discovery is not appropriate.
- Avoid embedding credentials; use environment or secret stores.

`Sylin.Koan.Redis` arrives transitively and owns endpoint discovery, orchestration, connection pooling, and disposal.
Data Redis owns only repository behavior, source/database routing, Data capabilities, and provider-aware health.
Referencing this adapter together with Cache Redis reuses one default connection.

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
to application code is sufficient. Numbered pages do not make Redis keyspace scanning provider-bounded,
snapshot-based, resumable, or mutation-safe. Prefer another supported Data provider for large or
query-intensive datasets.

See TECHNICAL.md for options and operational considerations.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

