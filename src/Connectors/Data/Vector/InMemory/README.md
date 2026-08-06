# Sylin.Koan.Data.Vector.Connector.InMemory

The bounded, zero-infrastructure exact Vector floor for Koan.

- Target framework: net10.0
- License: Apache-2.0

## Smallest meaningful result

```csharp
builder.Services.AddKoan(koan =>
{
    koan.Data.Source("Semantic").Vector<Article>(space => space
        .Name("articles")
        .Dimensions(3)
        .Metric(VectorMetric.Cosine)
        .Visibility(VectorVisibility.Session));
});

await Vector<Article>.Save("koan", [1f, 0f, 0f]);

var nearest = await Vector<Article>.Search(
    [0.9f, 0.1f, 0f],
    query => query.Top(5));
```

Reference the package and keep the ordinary `AddKoan(...)` bootstrap. There is no provider registration, client,
connection string, or index ceremony.

## What it proves

- exact cosine, Euclidean, or dot-product ranking with normalized finite similarity;
- stable identity tie ordering and truthful exact execution receipts;
- complete point save/get/delete and positional get-many;
- full Koan metadata filters before ranking;
- ordered bulk outcomes without an atomic-batch claim;
- Session visibility and source, partition, and row-scope isolation;
- bounded spaces, points, dimensions, metadata size, and host-owned caches.

It does not pretend to provide durability, Eventual visibility, approximate indexing, hybrid text search, continuation,
multiple vectors per point, streaming export, or atomic batches. Those clauses reject correctively.

Data belongs to one application host and disappears when that host is disposed. Use this adapter for local workflows,
tests, and bounded single-process datasets. Select another connector when persistence, sharing, or provider-native
capabilities are application requirements.

## References

- [Technical reference](./TECHNICAL.md)
- [Vector reference](../../../../../docs/reference/ai/vector.md)
- [Data adapter development primer](../../../../../docs/architecture/data-adapter-development-primer.md)
