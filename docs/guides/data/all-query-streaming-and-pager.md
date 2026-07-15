---
type: GUIDE
domain: data
title: "All, Query, Streaming, and Paging"
audience: [developers]
status: current
last_updated: 2026-07-15
framework_version: v0.6.3
validation:
  date_last_tested: 2026-07-15
  status: verified
  scope: docs/guides/data/all-query-streaming-and-pager.md
---

# All, Query, Streaming, and Paging

## Contract

- Inputs: Entity models using `Entity<T>` statics; provider adapters installed.
- Outputs: Correct usage patterns for materialization, current async iteration, and explicit numbered paging.
- Error modes: OOM on large `All()`, paging inconsistencies, unstable ordering.
- Success criteria: Clear guidance on when to materialize, how to stream/paginate, and stable Id ordering.

### Edge Cases

- Provider caps: Check capabilities before relying on pushdown/filters.
- Ordering: Repeated numbered pages should use an explicit stable sort when data can change during traversal.
- Cancellation: Always pass `CancellationToken` to data operations.

---

## Materialize everything (explicit)

```csharp
var all = await Product.All(ct); // full set; avoid for very large tables
```

## Async iteration (currently materialized)

```csharp
await foreach (var p in Product.AllStream(batchSize: 500, ct))
{
    // process
}
```

`AllStream` and `QueryStream` currently materialize the complete query before the first yield and do
not apply `batchSize`. They provide an async-enumerable call shape, not bounded memory or provider
backpressure. Use explicit numbered pages to limit each returned result until the R07 Data streaming
repair lands; current adapters do not share a provider-agnostic bounded-stream guarantee.

## Explicit paging

```csharp
const int pageSize = 100;
for (var pageNumber = 1; ; pageNumber++)
{
    var items = await Product.Page(pageNumber, pageSize, "Id", ct);
    foreach (var product in items)
    {
        // process product
    }

    if (items.Count < pageSize)
    {
        break;
    }
}
```

This caps the page returned to application code. It does not prove every adapter avoids internal
materialization; no public cursor/resume-token API exists today.

## Guidance

- Use explicit numbered pages to limit application-visible batches today.
- Treat `AllStream`/`QueryStream` as materialized compatibility surfaces for now.
- Use page endpoints for APIs to control latency and memory.
- Reserve `All()` for small sets or one-off maintenance scripts.

## Related

- ADR: DATA-0061
- Reference: Data pillar index; Web pagination attribute
