# Sylin.Koan.Data.Mongo

MongoDB provider for Koan document data with options binding and pushdown-friendly queries.

- Target framework: net9.0
- License: Apache-2.0

## Capabilities
- Connection and database binding via options
- Filter/paging pushdowns for supported predicates
- Streaming and pager semantics via Koan.Data.Core

## Install

```powershell
dotnet add package Sylin.Koan.Data.Mongo
```

## Minimal setup
- Configure a MongoDB URI and database name via options.
- Keep credentials in secret stores; don’t inline URIs.

## Usage — safe snippets
- Prefer first-class model statics:
	- `Book.Query(b => b.Tags.Contains("db"), ct)`
	- `await foreach (var b in Book.QueryStream(b => b.Score >= 80, ct)) { ... }`

```csharp
// Page through results for UI
var page = await Book.FirstPage(20, ct);
// ... render ...
if (page.HasMore)
{
		page = await Book.Page(page.Cursor, ct);
}
```

See TECHNICAL.md for options and pushdown details.

## References
- Data access reference: `~/reference/data-access.md`
- Decision DATA-0061: `~/decisions/DATA-0061-data-access-pagination-and-streaming.md`
