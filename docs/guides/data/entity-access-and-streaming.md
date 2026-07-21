---
type: GUIDE
domain: data
title: "Entity access and streaming"
audience: [developers]
status: current
last_updated: 2026-07-15
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-15
  status: verified
  scope: docs/guides/data/entity-access-and-streaming.md
---

# Entity access and streaming

## Contract

- Inputs: Entity models using `Entity<T>` statics; provider adapters installed.
- Outputs: Explicit materialization, capability-qualified async iteration, and numbered pages.
- Error modes: OOM from large materialization; corrective stream rejection; offset-paging drift under writes.
- Success criteria: The code shape communicates its memory and consistency intent.

### Edge Cases

- Provider caps: Entity streaming is available only when the selected adapter earns provider-bounded paging.
- Ordering: Koan validates the caller's portable order, then appends the exact Entity identifier as a
  separate provider-stable page tie-breaker. Explicit Entity-identifier ordering is not portable.
- Cancellation: Always pass `CancellationToken` to data operations.

---

## Materialize everything (explicit)

```csharp
var all = await Product.All(ct); // full set; avoid for very large tables
```

## Async iteration (provider-bounded)

```csharp
await foreach (var product in Product.AllStream(batchSize: 500, ct: ct))
{
    await Process(product, ct);
}

await foreach (var ready in Product.QueryStream(
    product => product.Ready,
    batchSize: 250,
    ct: ct))
{
    await Process(ready, ct);
}
```

Koan requests one numbered candidate page, validates the adapter's execution report, then yields it
before requesting another. Consumer pace controls later requests; cancellation and early disposal
prevent later pages. Streams do not request totals.

`batchSize` bounds the candidate list visible to Koan. It does not describe opaque driver buffers or
make the complete operation snapshot-consistent. A residual predicate can run pointwise over each
bounded candidate page; the provider must still own the page bound and complete total order.

### Qualified adapters

| Stream behavior | Adapters |
|---|---|
| Provider-bounded numbered pages | SQLite, PostgreSQL, SQL Server, CockroachDB, MongoDB, Couchbase |
| Reject before query/yield | InMemory, JSON, Redis |

Unsupported execution throws `QueryStreamRejectedException` with the Entity, provider, stable reason,
and corrective action. Koan does not silently materialize a full source as a fallback.

Every user stream sort component must be a single-member, top-level, non-nullable `bool`, `byte`,
`sbyte`, `short`, `ushort`, or `int`. Nullable, enum, string/char, wide numeric, floating/decimal,
temporal, `Guid`, binary, nested, complex, and collection sorts reject before provider I/O. This is an
intentionally narrow first semantic floor; types return only after the shared six-adapter matrix proves
their complete value and null-order contract.

Koan separately adds the exact Entity identifier after validating caller ordering. The usual string
key is an opaque provider-stable tie-break, not a CLR or cross-provider collation promise, so an
explicit Entity-identifier sort rejects; a differently
cased business member does not replace it. Avoid CLR persistence models whose members differ only by
case, such as `Id` and `id`; their storage names are not portable across the qualified adapters.

## Explicit numbered paging

```csharp
const int pageSize = 100;
for (var pageNumber = 1; ; pageNumber++)
{
    var items = await Product.Page(pageNumber, pageSize, "Id", ct);
    foreach (var product in items) await Process(product, ct);
    if (items.Count < pageSize) break;
}
```

`Page` returns one materialized list and is useful for UI/request boundaries. Adapter fallback rules
still apply to ordinary paging; do not infer the stronger stream capability from this API. Koan has no
public cursor, Pager, continuation token, or resume API.

## Consistency boundary

Current streams use numbered offset pages. Concurrent inserts, deletes, or order-key changes can cause
skips or duplicates. They are not mutation-safe and do not create a snapshot. Use an application-owned
watermark/snapshot design when the business operation requires those guarantees.

The current provider contract represents `Skip`/`OFFSET` as `Int32`. Koan rejects a requested page
before provider I/O when `(pageNumber - 1) * pageSize` exceeds `Int32.MaxValue`.

The selected or rejected execution appears as `koan.data.stream.execution` in the shared facts
envelope (`/.well-known/Koan/facts` and `koan://facts`) after the first enumeration attempt.

## Guidance

- Use `AllStream`/`QueryStream` for consumer-paced processing on a qualified adapter.
- Treat a rejection as a capability mismatch; choose a qualified adapter or materialize explicitly.
- Use page endpoints for APIs to control latency and memory.
- Reserve `All()` for small sets or one-off maintenance scripts.

## Related

- Decision: [DATA-0107](../../decisions/DATA-0107-provider-bounded-entity-streams.md)
- Reference: Data pillar index; Web pagination attribute
