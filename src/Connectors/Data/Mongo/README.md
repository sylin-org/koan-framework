# Sylin.Koan.Data.Connector.Mongo

MongoDB provider for Koan document data with options binding and pushdown-friendly queries.

- Target framework: net10.0
- License: Apache-2.0

## Capabilities

- Connection and database binding via options
- Filter/paging pushdowns for supported predicates
- Provider-bounded Entity streams through `DataCaps.Query.ProviderBoundedPaging`

## Install

```powershell
dotnet add package Sylin.Koan.Data.Connector.Mongo
```

## Minimal setup

- Configure a MongoDB URI and database name via options.
- Keep credentials in secret stores; don’t inline URIs.

## Usage - safe snippets

- Prefer first-class model statics:
  - `Book.Query(b => b.Tags.Contains("db"), ct)`
  - `await foreach (var b in Book.QueryStream(b => b.Score >= 80, ct)) { ... }`

```csharp
// Page through results for UI
const int pageSize = 20;
for (var pageNumber = 1; ; pageNumber++)
{
    var books = await Book.Page(pageNumber, pageSize, ct);
    foreach (var book in books) { /* render */ }
    if (books.Count < pageSize) break;
}
```

## Streaming boundary

`AllStream` and `QueryStream` request one numbered MongoDB page at a time. `batchSize` caps the
Koan-visible candidate page; it does not claim a bound for opaque MongoDB driver buffers. Streaming
accepts only DATA-0107's first proved user-sort floor: top-level, non-nullable `bool`, `byte`, `sbyte`,
`short`, `ushort`, or `int`. Other user sorts reject before provider I/O. Data.Core separately appends
the usual string Entity identifier as an opaque provider-stable tie-break, not a cross-provider
collation promise.

These streams do not provide snapshot consistency, mutation-safe traversal, resumability, or a public
cursor. Concurrent writes can therefore cause skips or duplicates during offset-based traversal.

See TECHNICAL.md for options and pushdown details.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

