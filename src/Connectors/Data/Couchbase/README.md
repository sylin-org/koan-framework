# Sylin.Koan.Data.Connector.Couchbase

Couchbase persistence for Koan Entities. Reference the package, keep the application's normal
`AddKoan()` bootstrap, and use Entity verbs; the connector owns discovery, cluster/bucket access,
scope and collection routing, schema ensure, and readiness participation.

- Target framework: net10.0
- License: Apache-2.0

## Install

> **Supported 0.20 extension:** Koan supports this connector within the capability and operational
> boundaries below. Couchbase availability is not eager activation; the selected or explicitly routed
> provider owns connection and readiness.

```powershell
dotnet add package Sylin.Koan.Data.Connector.Couchbase
```

## Meaningful result

With Couchbase reachable, no provider registration or repository scaffold is required:

```csharp
builder.Services.AddKoan();

public sealed class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

var saved = await new Product { Name = "Garden sensor", Price = 24.50m }.Save();
var affordable = await Product.Query(product => product.Price < 50m);
```

When Couchbase is the only server-backed Data connector, it wins automatic provider election over
the local JSON floor. Pin business-critical placement with `[DataAdapter("couchbase")]` or the
Default-source adapter setting when adding another connector must not move existing data.

## Configuration

`auto` is the default and uses Koan's discovery path, then the local Couchbase fallback.
Use exact provider configuration only when placement or native guarantees require it:

```json
{
  "ConnectionStrings": {
    "Couchbase": "couchbase://localhost"
  },
  "Koan": {
    "Data": {
      "Couchbase": {
        "Bucket": "Products",
        "Durability": "Majority"
      }
    }
  }
}
```

Supply `Username` and `Password` through the platform's secret provider; do not commit credentials.
`Scope`, `Collection`, `QueryTimeoutSeconds`, `ManagementUrl`, and named-source placement are
optional, explicit native choices—not common-path requirements.

## Capabilities

- supported LINQ/filter translation to parameterized N1QL
- bulk upsert/delete, transactional batches, conditional replace, and raw N1QL string queries
- Row-, container-, and database-scoped Data isolation composition using bucket/scope/collection
- provider-bounded Entity streams through `DataCaps.Query.ProviderBoundedPaging`
- factory-owned connection pooling, lazy selected use, and selection-aware readiness

`Product.All()` requests the complete visible set. Couchbase does not invent pagination defaults or
caps; use `Product.Page(page, size)` or `Product.AllStream(batchSize: ...)` when the set can grow.

## Streaming boundary

`AllStream` and `QueryStream` request one numbered N1QL page at a time. `batchSize` caps the
Koan-visible candidate page; it does not claim a bound for opaque Couchbase SDK buffers. Unsupported
sorts reject before provider I/O.

These streams use offset pages. They do not provide snapshot consistency, mutation-safe traversal,
resumability, or a public cursor; concurrent writes can cause skips or duplicates.

## Operations boundary

A referenced but unelected Couchbase connector is available, not a readiness dependency and not an
eager host initializer. Its shared cluster provider connects lazily when Couchbase wins election or a
runtime Entity/source operation selects it. First selected use against a fresh local cluster may be
expensive while Couchbase initializes services, bucket, and indexes.

## References

- [Technical reference](https://github.com/sylin-org/Koan-framework/blob/main/src/Connectors/Data/Couchbase/TECHNICAL.md)
- [DATA-0107 — provider-bounded Entity streams](https://github.com/sylin-org/Koan-framework/blob/main/docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Entity access and streaming](https://github.com/sylin-org/Koan-framework/blob/main/docs/guides/data/entity-access-and-streaming.md)
