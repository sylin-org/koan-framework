---
type: GUIDE
domain: data
title: "Entity access and streaming"
audience: [developers]
status: current
last_updated: 2026-09-12
framework_version: v1.0.0
validation:
  date_last_tested: 2026-07-15
  status: verified
  scope: docs/guides/data/entity-access-and-streaming.md
---

# Entity access and streaming

Koan gives one Entity three ways to leave the store, and they are not interchangeable: materialize the
set, iterate it, or ask for a numbered page. Choosing among them is choosing how much memory the call
may take and how consistent the result has to be, so the shape you write is the shape you meant.

Three properties decide most of it.

- **Streaming is earned, not assumed.** Only an adapter that advertises provider-bounded paging can
  serve an Entity stream; the rest refuse before yielding rather than quietly buffering the set.
- **Order is completed for you.** Koan validates the portable order you asked for and appends the
  Entity identifier as a provider-stable tie-breaker, so pages do not shuffle between reads. Ordering
  by that identifier yourself is not portable.
- **A stream in identity order holds still under concurrent writes.** Each page continues after the
  last identity read, so a row present for the whole read is returned exactly once. A stream ordered
  by your own key, and an explicit numbered page, still drift when rows are inserted or deleted.

Pass a `CancellationToken` to every one of them.

## Materialize everything (explicit)

```csharp
var all = await Product.All(ct); // full set; avoid for very large tables

var ready = await Product.Query(
    product => product.Ready,
    QueryDefinition.All
        .WithSort<Product>(sort => sort.OrderBy(product => product.Name))
        .WithPagination(page: 1, pageSize: 100),
    ct);
```

List-returning `All` and `Query` preserve a null count strategy, including when the definition carries
sort or pagination. The adapter receives the requested rows without an implicit total-count request.
Use `QueryWithCount` when the caller actually needs a total, and select `Exact`, `Fast`, or `Optimized`
explicitly when the distinction matters:

```csharp
var page = await Product.QueryWithCount(
    product => product.Ready,
    QueryDefinition.All
        .WithPagination(page: 1, pageSize: 100)
        .WithCountStrategy(CountStrategy.Exact),
    ct);
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
| Provider-bounded pages (identity seek, or numbered under a caller's order) | SQLite, PostgreSQL, SQL Server, CockroachDB, MongoDB, Couchbase |
| Reject before query/yield | InMemory, JSON, Redis |

Unsupported execution throws `QueryStreamRejectedException` with the Entity, provider, stable reason,
and corrective action. Koan does not silently materialize a full source as a fallback.

A sort key whose type is a portable scalar -- an enum, or `bool`, `byte`, `sbyte`, `short`, `ushort`,
`int`, `long`, `decimal`, `double`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan` -- compares
the same way on every backend, because the shared adapter matrix proves that value and null-order
contract.

Any other key still streams, stably and completely, on the store it runs against. What it does not
carry is agreement *across* backends: a string orders by collation, a nullable column by the
provider's null placement. Koan records that as a runtime fact naming the keys involved rather than
refusing the query -- an application that needs cross-backend agreement can see which keys to avoid,
and one that does not gets the order it actually asked for.

Koan separately adds the exact Entity identifier after validating caller ordering. A `string`, `Guid`,
`int` or `long` key is an opaque provider-stable tie-break in the store's own order of that key (text by
collation, a native column or BSON value by its type), not a CLR or cross-provider collation promise.
An Entity keyed by any other type rejects before I/O; a differently cased business member does not
replace the identifier. Avoid CLR persistence models whose members differ only by
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

A stream whose order leads with the Entity identifier (`AllStream()`, `QueryStream(predicate)`, or an
explicit `Id`/`-Id` order) continues each page after the last identity it read, compared by the store
(DATA-0151). A row present for the whole read is returned exactly once, whatever another writer inserts or
deletes; a row inserted behind the read is not returned, and one deleted ahead of it is not. Removing each
row as it is streamed visits every row. Each page costs the same however deep the read is, and no page
number bounds it. It is still not a snapshot: a row updated during the read is returned as it was when its
page was read.

A stream ordered by a caller's own key still uses numbered offset pages, and concurrent inserts, deletes,
or order-key changes can cause skips or duplicates there. The provider contract represents
`Skip`/`OFFSET` as `Int32`, so Koan rejects such a page before provider I/O when
`(pageNumber - 1) * pageSize` exceeds `Int32.MaxValue`. Use an application-owned watermark/snapshot
design when the business operation requires more.

The selected or rejected execution appears as `koan.data.stream.execution` in the shared facts
envelope (`/.well-known/Koan/facts` and `koan://facts`) after the first enumeration attempt.

## Guidance

- Use `AllStream`/`QueryStream` for consumer-paced processing on a qualified adapter.
- Treat a rejection as a capability mismatch; choose a qualified adapter or materialize explicitly.
- Use page endpoints for APIs to control latency and memory.
- Reserve `All()` for small sets or one-off maintenance scripts.
- Use `QueryWithCount` only when the consumer needs a total; ordinary list queries do not ask for one.

## Related

- Decision: [DATA-0107](../../decisions/DATA-0107-provider-bounded-entity-streams.md)
- Reference: Data pillar index; Web pagination attribute
