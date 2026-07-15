# Sylin.Koan.Data.Connector.Sqlite

SQLite provider for Koan relational data—suited to local development, tests, and single-node applications.

- Target framework: net10.0
- License: Apache-2.0

## Capabilities

- Zero-config file database at `.koan/data/Koan.sqlite`
- Basic LINQ predicate pushdown via Koan.Data.Relational translator
- Schema helpers (create table/index) via Koan.Data.Relational
- Provider-bounded Entity streams through `DataCaps.Query.ProviderBoundedPaging`

## Install

> Current release status: the coherent package path is specified and exercised from staged artifacts, but the
> existing public 0.17 package set is not a supported clean-room install. Until the next coherent publication, use
> a source checkout/project reference. See the [repository installation status](../../../../README.md).

```powershell
dotnet add package Sylin.Koan.Data.Connector.Sqlite
```

## Minimal setup

Reference the package and keep the application's normal `services.AddKoan()` bootstrap. No provider registration,
schema scaffold, or configuration is required. Configure a source only when the default target is not appropriate:

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Default": {
          "Adapter": "sqlite",
          "ConnectionString": "Data Source=./data/app.db"
        }
      }
    }
  }
}
```

Use a `Data Source=...` connection string for a durable file. `Data Source=:memory:` and explicit
`Mode=Memory` targets create private, source-isolated databases that survive per-operation connections for one
Koan host and disappear when that host is disposed.

## Usage - safe snippets

- Prefer first-class model statics:
  - `Todo.FirstPage(100, ct)` then `Todo.Page(2, 100, ct)`
  - `await foreach (var t in Todo.QueryStream(x => x.Done == false, ct)) { ... }`

```csharp
// Materialize a small filtered set
var open = await Todo.Query(x => !x.Done, ct);

// Stream when the set may be large
await foreach (var t in Todo.QueryStream(x => !x.Done, ct))
{
    // process
}
```

## Streaming boundary

`AllStream` and `QueryStream` request one numbered SQLite page at a time. `batchSize` caps the
Koan-visible candidate page; it does not claim a bound for opaque provider-driver buffers. Streaming
accepts only DATA-0107's first proved user-sort floor: top-level, non-nullable `bool`, `byte`, `sbyte`,
`short`, `ushort`, or `int`. Other user sorts reject before provider I/O. Data.Core separately appends
the usual string Entity identifier as an opaque provider-stable tie-break, not a cross-provider
collation promise.

These streams do not provide snapshot consistency, mutation-safe traversal, resumability, or a public
cursor. Concurrent writes can therefore cause skips or duplicates during offset-based traversal.

See TECHNICAL.md for options and dialect notes.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

