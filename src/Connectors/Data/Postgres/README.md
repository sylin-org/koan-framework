# Sylin.Koan.Data.Connector.Postgres

PostgreSQL provider for Koan managed entities and existing relational data.

- Target framework: net10.0
- License: Apache-2.0

## Capabilities

- Connection + health checks with minimal options
- JSON projection, filter, and paging pushdowns where supported
- Schema helpers (create table/index) via Koan.Data.Relational
- Provider-bounded Entity streams through `DataCaps.Query.ProviderBoundedPaging`
- Compact flat, structured, nested-path, composite-key, and generated-key maps
- Read-only fail-fast safety and non-creating `StorageLifecycle.External`
- Provider-neutral container inspection and bounded record sampling
- Registered parameterized SQL reads and scalars through provider-enforced read lanes

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

Connect an existing table without introducing a parallel data API:

```csharp
builder.Services.AddKoan(koan => koan.Data
    .Source("Legacy")
    .Map<Customer>(map => map
        .Container("CUSTOMER")
        .Key(customer => customer.Id).Name("CUSTOMER_NO")
        .Property(customer => customer.DisplayName).Name("DISPLAY_NM")
        .Property(customer => customer.Profile).Object("PROFILE_JSON")));

using (EntityContext.Source("Legacy"))
{
    var customer = await Customer.Get(7);
}
```

Set `StorageLifecycle=External` when Koan must validate but never create or repair the table. Set `Access=ReadOnly`
when writes must reject before provider mutation. Nested `.Path("NAME_DATA", "full")` updates preserve unbound values
inside the same `jsonb` object.

Explore or name a useful read without adding a second data API:

```csharp
koan.Data.Source("Legacy").Query("customers.active", query => query
    .Lane("Reports")
    .Sql("select id as Id, name as Name from customers where active = @active")
    .Parameter<bool>("active"));

var legacy = Data.Source("Legacy");
var active = await legacy.Query("customers.active", new { active = true });
var containers = await legacy.Inspect().Containers(100, null, ct);
```

Opaque SQL requires a configured read lane. PostgreSQL executes it in a native read-only transaction.

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
orders by a portable scalar -- an enum, or an integral, decimal, floating, or temporal type -- with a
comparison that holds on any backend. Any other key still streams stably here, but PostgreSQL defines its
comparison rather than Koan, which records that as a runtime fact instead of refusing the query.
Data.Core separately appends the usual string Entity identifier as an opaque provider-stable
tie-break, not a cross-provider collation promise.

These streams do not provide snapshot consistency, mutation-safe traversal, resumability, or a public
cursor. Concurrent writes can therefore cause skips or duplicates during offset-based traversal.

See TECHNICAL.md for options and pushdown coverage.

## References

- [DATA-0107 provider-bounded Entity streams](../../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](../../../../docs/guides/data/entity-access-and-streaming.md)

