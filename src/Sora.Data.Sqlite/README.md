# Sylin.Sora.Data.Sqlite

SQLite provider for Sora relational data — great for local dev, tests, and simple single-node apps.

- Target framework: net9.0
- License: Apache-2.0

## Capabilities
- Zero-config file DB (connection string can be a file path)
- Basic LINQ predicate pushdown via Sora.Data.Relational translator
- Schema helpers (create table/index) via Sora.Data.Relational

## Install

```powershell
dotnet add package Sylin.Sora.Data.Sqlite
```

## Minimal setup
- Use an on-disk file for persistence or :memory: for tests.
- Bind options at startup; don’t hardcode paths.

## Usage — safe snippets
- Prefer first-class model statics:
	- `Todo.FirstPage(100, ct)` then `Todo.Page(cursor, ct)`
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

See TECHNICAL.md for options and dialect notes.

## References
- Data access reference: `~/reference/data-access.md`
- Decision DATA-0061: `~/decisions/DATA-0061-data-access-pagination-and-streaming.md`
