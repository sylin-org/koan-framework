---
type: GUIDE
domain: data
title: "Entity access and streaming"
audience: [developers]
status: current
last_updated: 2025-10-09
framework_version: v0.6.3
validation:
  date_last_tested: 2025-10-09
  status: verified
  scope: docs/guides/data/entity-access-and-streaming.md
---

# Entity access and streaming

## Contract

- Inputs: Entity models using `Entity<T>` statics; provider adapters installed.
- Outputs: Correct usage patterns for materialization vs streaming vs cursor/pager.
- Error modes: OOM on large `All()`, paging inconsistencies, unstable ordering.
- Success criteria: Clear guidance on when to materialize, how to stream/paginate, and stable Id ordering.

### Edge Cases

- Provider caps: Check capabilities before relying on pushdown/filters.
- Ordering: Cursor/pager/streaming must use stable Id ascending.
- Cancellation: Always pass `CancellationToken` to streams.

---

## Materialize everything (explicit)

```csharp
var all = await Product.All(ct); // full set; avoid for very large tables
```

## Stream in batches

```csharp
await foreach (var p in Product.AllStream(batchSize: 500, ct))
{
    // process
}
```

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

- Use streaming or pager for jobs/exports.
- Use page endpoints for APIs to control latency and memory.
- Reserve `All()` for small sets or one-off maintenance scripts.

## Related

- ADR: DATA-0061
- Reference: Data pillar index; Web pagination attribute
