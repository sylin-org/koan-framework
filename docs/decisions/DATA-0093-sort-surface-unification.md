---
id: DATA-0093
slug: DATA-0093-sort-surface-unification
domain: DATA
status: Accepted
date: 2026-05-17
---

# ADR 0093: Sort surface unification across Page, Stream, Transfer, and body-query

## Context

DATA-0092 introduces structured sort and adapter pushdown for the `Data<T,K>.QueryWithCount` path and the `GET /` collection endpoint. But sort is missing or silently dropped on every other entry point in the framework:

| Path | Sort threaded? | Today's behaviour |
|---|---|---|
| `Entity<T>.FirstPage(size)` | No | Returns page 1 of natural order |
| `Entity<T>.Page(page, size)` | No | Returns N-th page of natural order |
| `Entity<T>.AllStream()` | No | Yields in repository order |
| `Entity<T>.QueryStream(filter)` | No | New `DataQueryOptions(page, size)` is constructed inside, dropping caller's sort |
| Transfer builders (`Copy/Move/Mirror`) | Only via raw `QueryShaper` LINQ | Caller must hand-roll OrderBy in shaper |
| `POST /query` body | No | `sort` field is not extracted from JSON body |

Users who follow the framework's documented entity-first patterns hit silent unsorted results whenever they go off the `Data<T,K>.QueryWithCount` golden path. This is a DX cliff that contradicts the framework's promise of consistent behaviour across entry points.

## Decision

Add an optional sort surface to every read entry point. The same three input flavours (`string`, `ISortBuilder<T>` lambda, structured `SortSpec[]`) work everywhere. Default usage (no sort argument) is preserved — no breaking changes to call sites.

### 1. Entity<T> overloads

```csharp
// Existing — unchanged
Todo.All()
Todo.Page(2, 10)
Todo.FirstPage(10)
Todo.Query(x => x.Done)
Todo.AllStream()
Todo.QueryStream("filter")

// New — additive overloads with optional sort parameter
Todo.All(sort: q => q.OrderBy(x => x.Title))
Todo.All(sort: "-CreatedAt,Title")

Todo.Page(2, 10, sort: "-CreatedAt")
Todo.Page(2, 10, sort: q => q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Title))

Todo.FirstPage(10, sort: "-CreatedAt")

Todo.Query(x => x.Done, sort: q => q.OrderBy(x => x.Title))
Todo.Query(x => x.Done, sort: "-CreatedAt")

Todo.AllStream(sort: q => q.OrderBy(x => x.Id))
Todo.QueryStream("filter", sort: "-CreatedAt")
```

### 2. Streaming + sort: explicit buffer

`AllStream(sort: ...)` and `QueryStream(filter, sort: ...)` **materialize the full result set** before yielding the first item. This contradicts the "streaming" name but is the only way to honour sort semantics. The trade-off is documented in XML doc on each method:

> When `sort` is specified, all matching items are fetched and ordered before enumeration begins. For true incremental streaming, do not pass `sort`.

The orchestrator emits a debug-level log when streaming materializes for sort, so operators can detect the pattern in tracing.

### 3. Transfer builders

```csharp
// Existing
Order.Copy().Where(x => x.IsArchived).To<ArchiveOrder>(...)

// New — fluent OrderBy on the builder
Order.Copy()
     .Where(x => x.IsArchived)
     .OrderByDescending(x => x.CreatedAt)
     .ThenBy(x => x.Id)
     .To<ArchiveOrder>(...)
```

Transfers go through the standard query path with sort. This is meaningful for `Move` and `Copy` when batched: sorted source order is preserved in the target, useful for offset-style watermarking and reproducible archive snapshots.

### 4. POST /query body sort

The body request schema accepts a `sort` field with the same string-array form as `?sort=`:

```json
{
  "filter": { "status": "active" },
  "page": 2,
  "size": 10,
  "sort": ["-createdAt", "+title"]
}
```

When both query-string `?sort=` and body `sort` are present, body wins (body is the more specific request shape). Query-string `?sort=` continues to work for compatibility.

### 5. Hook helpers

```csharp
public static class SortSpecListExtensions
{
    public static void AddByField<T>(this IList<SortSpec> sorts, string field);
    public static void AddByField<T>(this IList<SortSpec> sorts, string field, bool desc);
    public static void OrderBy<T, TKey>(this IList<SortSpec> sorts, Expression<Func<T, TKey>> selector);
    public static void OrderByDescending<T, TKey>(this IList<SortSpec> sorts, Expression<Func<T, TKey>> selector);
}
```

Hook implementations append sorts without touching `MemberInfo` plumbing. Resolution happens at call time against the target entity type.

## Consequences

**Positive:**
- One coherent sort surface across the framework. No silent-drop entry points remain.
- Streaming + sort is explicit and documented, not hidden.
- POST /query and GET / are symmetric.
- Transfer builders inherit the standard sort grammar.

**Negative:**
- Streaming with sort breaks the streaming invariant (full materialization). Mitigated by documentation and operator log signal.
- Slight API surface expansion (one optional parameter per existing method). Argued worth it for the DX consistency win.

**DX preservation:**
- Every existing call site continues to work unchanged.
- `sort:` is a named optional parameter, never positional.

## Alternatives considered

- **Add `WithSort` to a fluent query builder, deprecate the current `Entity<T>.Page(args)` shape.** Rejected: breaks "static method returns Task" Koan idiom; would force every existing caller to rewrite.
- **Don't expose sort on streaming, force users to call `Query(sort: ...)` for sorted results.** Rejected: inconsistency is a worse DX failure than the materialize-for-sort trade-off, especially when the trade-off is documented.
- **Two body-query schemas: legacy (no sort) and v2 (with sort).** Rejected: greenfield framework, no breaking-change concern at v0.6.x.
