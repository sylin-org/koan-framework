# Sylin.Sora.Data.SqlServer

SQL Server provider for Sora relational data with safe defaults, pushdowns, and schema helpers.

- Target framework: net9.0
- License: Apache-2.0

## Capabilities
- Connection + health integration with minimal options
- JSON projection and filter/paging pushdowns where supported
- Schema helpers via Sora.Data.Relational (add-only create/index)
- Paging and streaming semantics aligned with DATA-0061

## Install

```powershell
dotnet add package Sylin.Sora.Data.SqlServer
```

## Minimal setup
- Configure a connection using first-win resolution:
	- ConnectionStrings:Default (or a named source)
	- Sora:Data:Sources:Default:ConnectionString
	- Sora:Data:SqlServer:ConnectionString
- Bind options once at startup; keep credentials in secret stores.

## Usage — safe snippets
- Prefer first-class model statics from your entity models:
	- `Item.FirstPage(50, ct)` and then `Item.Page(cursor, ct)`
	- `Item.Query(x => x.Status == "Open", ct)`
	- `await foreach (var i in Item.QueryStream(x => x.Flag, ct)) { ... }`
- Avoid unbounded materialization; use paging or streaming for large sets.

```csharp
// Page through items
var page = await Item.FirstPage(50, ct);
foreach (var row in page.Items) { /* ... */ }
while (page.HasMore) {
	page = await Item.Page(page.Cursor, ct);
}
```

See TECHNICAL.md for contracts, options, and pushdown notes.

## References
- Paging/Streaming decision: `~/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- Data access reference: `~/reference/data-access.md`
- Engineering front door: `~/engineering/index.md`
