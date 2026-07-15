---
type: GUIDE
domain: data
title: "Entity access and streaming"
audience: [developers]
status: current
last_updated: 2026-07-15
framework_version: v0.6.3
validation:
  date_last_tested: 2026-07-15
  status: verified
  scope: docs/guides/data/entity-access-and-streaming.md
---

# Entity access and streaming

## Contract

- Inputs: Entity models using `Entity<T>` statics; provider adapters installed.
- Outputs: Correct usage patterns for materialization, current async iteration, and bounded cursor/pager work.
- Error modes: OOM on large `All()`, paging inconsistencies, unstable ordering.
- Success criteria: Clear guidance on when to materialize, how to stream/paginate, and stable Id ordering.

### Edge Cases

- Provider caps: Check capabilities before relying on pushdown/filters.
- Ordering: Cursor/pager traversal must use a stable order.
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
backpressure. Use the pager below for large data until the R07 Data streaming repair lands.

## Explicit paging

```csharp
var first = await Product.FirstPage(pageSize: 50, ct);
var next = await first.NextPage(ct);
```

## Cursor + Pager (imperative)

```csharp
var cursor = EntityCursor.ForAll<Product>();
var pager = Pager.From(cursor, pageSize: 100, includeTotal: false);
while (!pager.End)
{
    var page = await pager.NextAsync(ct);
    // process page.Items
}
```

## Guidance

- Use `EntityCursor` + `Pager` for bounded jobs and exports today.
- Treat `AllStream`/`QueryStream` as materialized compatibility surfaces for now.
- Use page endpoints for APIs to control latency and memory.
- Reserve `All()` for small sets or one-off maintenance scripts.

## Related

- ADR: DATA-0061
- Reference: Data pillar index; Web pagination attribute
