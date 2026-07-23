# Sylin.Koan.Data.Connector.Mongo

MongoDB persistence for Koan Entities. Reference the package, keep the application's normal
`AddKoan()` bootstrap, and use Entity verbs; the connector owns driver setup, discovery, naming,
schema/index ensure, source routing, and readiness participation.

- Target framework: net10.0
- License: Apache-2.0

## Install

The generated [product surface](../../../../docs/reference/product-surface.md) owns support maturity.
MongoDB's transitive Zen Garden contract dependency is module-free and activates nothing;
applications still reference only this connector for MongoDB.

```powershell
dotnet add package Sylin.Koan.Data.Connector.Mongo
```

## Meaningful result

With MongoDB reachable, no provider registration or repository scaffold is required:

```csharp
builder.Services.AddKoan();

public sealed class Book : Entity<Book>
{
    public string Title { get; set; } = "";
    public bool Published { get; set; }
}

var saved = await new Book { Title = "Meaningful steps", Published = true }.Save();
var published = await Book.Query(book => book.Published);
```

When Mongo is the only server-backed Data connector, it wins automatic provider election over the
local JSON floor. Pin business-critical placement with `[DataAdapter("mongo")]` or the Default-source
adapter setting when adding another connector must not move existing data.

## Configuration

`auto` is the default and uses Koan's health-checked discovery pipeline, then the local MongoDB
fallback. Use exact provider configuration only when placement must be explicit:

```json
{
  "ConnectionStrings": {
    "Mongo": "mongodb://localhost:27017"
  },
  "Koan": {
    "Data": {
      "Mongo": {
        "Database": "Books"
      }
    }
  }
}
```

Named sources use `Koan:Data:Sources:{name}:{Adapter,ConnectionString}` plus the provider-specific
`mongo` settings beneath that source. Credentials belong in the platform's secret store.

## Capabilities

- LINQ/filter translation to native MongoDB filters for the supported expression floor
- bulk upsert/delete, atomic batches, conditional replace, TTL indexes, and fast remove
- Row-, container-, and database-scoped Data isolation composition
- provider-bounded Entity streams through `DataCaps.Query.ProviderBoundedPaging`
- source-aware client pooling and selection-aware readiness

`Book.All()` requests the complete visible set. Mongo does not invent pagination defaults or caps;
use `Book.Page(page, size)` or `Book.AllStream(batchSize: ...)` when the set can grow.

## Streaming boundary

`AllStream` and `QueryStream` request one numbered MongoDB page at a time. `batchSize` caps the
Koan-visible candidate page; it does not claim a bound for opaque MongoDB driver buffers. Streaming
accepts only DATA-0107's proved user-sort floor. Unsupported sorts reject before provider I/O.

These streams use offset pages. They do not provide snapshot consistency, mutation-safe traversal,
resumability, or a public cursor; concurrent writes can cause skips or duplicates.

## Operations boundary

A referenced but unelected Mongo connector is available, not a readiness dependency. It becomes
critical when it wins default election or a runtime Entity/source operation selects it. Startup,
health, and facts report the selected/participating sources with credentials redacted.

## References

- [Technical reference](https://github.com/sylin-org/Koan-framework/blob/main/src/Connectors/Data/Mongo/TECHNICAL.md)
- [DATA-0107 — provider-bounded Entity streams](https://github.com/sylin-org/Koan-framework/blob/main/docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](https://github.com/sylin-org/Koan-framework/blob/main/docs/guides/data/entity-access-and-streaming.md)
