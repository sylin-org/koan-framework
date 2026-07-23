# Sylin.Koan.Data.Connector.Postgres

PostgreSQL provider for Koan relational data with safe defaults and pushdowns.

- Target framework: net10.0
- License: Apache-2.0

## Capabilities

- Connection + health checks with minimal options
- JSON projection, filter, and paging pushdowns where supported
- Schema helpers (create table/index) via Koan.Data.Relational
- Provider-bounded Entity streams through `DataCaps.Query.ProviderBoundedPaging`

## Install

The generated [product surface](../../../../docs/reference/product-surface.md) owns support maturity.
This page owns PostgreSQL behavior and limits; they do not imply parity with every relational backend.

```powershell
dotnet add package Sylin.Koan.Data.Connector.Postgres
```

## Minimal setup

- Keep the application's ordinary `services.AddKoan()` bootstrap.
- For autonomous local discovery, run a reachable PostgreSQL service and omit explicit configuration.
- Otherwise set `ConnectionStrings:Postgres` or `Koan:Data:Postgres:ConnectionString`; keep secrets
  outside source control.

```csharp
builder.Services.AddKoan();

public sealed class Order : Entity<Order>;

var saved = await new Order().Save();
var same = await Order.Get(saved.Id);
```

## Usage - safe snippets

- Use first-class model statics from your entities:
  - `Order.FirstPage(50, ct)` / `Order.Page(2, 50, ct)`
  - `Order.Query(o => o.CustomerId == id, ct)`
  - `await foreach (var o in Order.QueryStream(o => o.Total > 100, ct)) { ... }`

```csharp
// Stream a filtered set through consumer-paced provider pages
await foreach (var o in Order.QueryStream(o => o.IsActive, ct))
{
    // process
}
```

## Streaming boundary

`AllStream` and `QueryStream` request one numbered PostgreSQL page at a time. `batchSize` caps the
Koan-visible candidate page; it does not claim a bound for opaque provider-driver buffers. Streaming
accepts only DATA-0107's first proved user-sort floor: top-level, non-nullable `bool`, `byte`, `sbyte`,
`short`, `ushort`, or `int`. Other user sorts reject before provider I/O. Data.Core separately appends
the usual string Entity identifier as an opaque provider-stable tie-break, not a cross-provider
collation promise.

These streams do not provide snapshot consistency, mutation-safe traversal, resumability, or a public
cursor. Concurrent writes can therefore cause skips or duplicates during offset-based traversal.

See TECHNICAL.md for options and pushdown coverage.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

