# Sylin.Koan.Data.Connector.Postgres

PostgreSQL provider for Koan relational data with safe defaults and pushdowns.

- Target framework: net9.0
- License: Apache-2.0

## Capabilities

- Connection + health checks with minimal options
- JSON projection, filter, and paging pushdowns where supported
- Schema helpers (create table/index) via Koan.Data.Relational

## Install

```powershell
dotnet add package Sylin.Koan.Data.Connector.Postgres
```

## Minimal setup

- Configure connection via ConnectionStrings or Koan:Data first-win keys.
- Bind options at startup; keep secrets out of source.

## Usage - safe snippets

- Use first-class model statics from your entities:
  - `Order.FirstPage(50, ct)` / `Order.Page(cursor, ct)`
  - `Order.Query(o => o.CustomerId == id, ct)`
  - `await foreach (var o in Order.QueryStream(o => o.Total > 100, ct)) { ... }`

```csharp
// Stream a filtered set (backpressure friendly)
await foreach (var o in Order.QueryStream(o => o.IsActive, ct))
{
		// process
}
```

See TECHNICAL.md for options and pushdown coverage.

## References

- Paging/Streaming decision: `~/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- Data access reference: `~/reference/data-access.md`

