# Sylin.Koan.Data.Connector.SqlServer

SQL Server provider for Koan relational data with safe defaults, pushdowns, and schema helpers.

- Target framework: net10.0
- License: Apache-2.0

## Capabilities

- Connection + health integration with minimal options
- JSON projection and filter/paging pushdowns where supported
- Schema helpers via Koan.Data.Relational (add-only create/index)
- Provider-bounded Entity streams through `DataCaps.Query.ProviderBoundedPaging`

## Install

```powershell
dotnet add package Sylin.Koan.Data.Connector.SqlServer
```

## Minimal setup

- Configure a connection using first-win resolution:
  - ConnectionStrings:Default (or a named source)
  - Koan:Data:Sources:Default:ConnectionString
  - Koan:Data:SqlServer:ConnectionString
- Bind options once at startup; keep credentials in secret stores.

## Usage - safe snippets

- Prefer first-class model statics from your entity models:
  - `Item.FirstPage(50, ct)` and then `Item.Page(2, 50, ct)`
  - `Item.Query(x => x.Status == "Open", ct)`
  - `await foreach (var i in Item.QueryStream(x => x.Flag, ct)) { ... }`
- Avoid unbounded materialization; use paging or streaming for large sets.

```csharp
// Page through items explicitly
const int pageSize = 50;
for (var pageNumber = 1; ; pageNumber++)
{
    var items = await Item.Page(pageNumber, pageSize, ct);
    foreach (var item in items) { /* ... */ }
    if (items.Count < pageSize) break;
}
```

## Streaming boundary

`AllStream` and `QueryStream` request one numbered SQL Server page at a time. `batchSize` caps the
Koan-visible candidate page; it does not claim a bound for opaque provider-driver buffers. Streaming
accepts only DATA-0107's first proved user-sort floor: top-level, non-nullable `bool`, `byte`, `sbyte`,
`short`, `ushort`, or `int`. Other user sorts reject before provider I/O. Data.Core separately appends
the usual string Entity identifier as an opaque provider-stable tie-break, not a cross-provider
collation promise.

These streams do not provide snapshot consistency, mutation-safe traversal, resumability, or a public
cursor. Concurrent writes can therefore cause skips or duplicates during offset-based traversal.

See TECHNICAL.md for contracts, options, and pushdown notes.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)
- Engineering front door: `~/engineering/index.md`

