# Data access: All/Query, streaming, and cursor/pager usage

This guide shows how to use Sora’s data-access surface with clear semantics and safe iteration for large datasets. It complements ADR-0050.

## Semantics at a glance

- All and Query (no paging options) fully materialize the complete result set.
- Paging is explicit via Page/FirstPage or DataQueryOptions.
- Streaming APIs (AllStream/QueryStream) return IAsyncEnumerable for memory-safe iteration in stable Id order.
- EntityCursor is the low-level primitive for resumable iteration; Pager wraps it for ergonomic Next/End loops.

## Materialize everything (All/QueryAll)

```csharp
using Sora.Data.Core;
using Sora.Domain;

// Full collection
var all = await MyModel.All(ct);

// Full query (string or predicate variants)
var matched = await MyModel.Query("Genre = 'Drama'", ct);
var matched2 = await MyModel.Query(m => m.Genres.Contains("Drama"), ct);
```

When you call All/Query without paging, the entire set is returned. Prefer streaming or paging below for very large sets.

## Stream results (IAsyncEnumerable)

Use streaming for jobs/pipelines to avoid loading everything in memory.

```csharp
// Stream the entire set in batches (stable Id ascending)
await foreach (var item in MyModel.AllStream(batchSize: 500, ct))
{
    // process item
}

// Stream a filtered subset
await foreach (var item in MyModel.QueryStream(m => m.Popularity > 0.8, batchSize: 500, ct))
{
    // process item
}

// Process in batches (bulk-friendly)
await foreach (var batch in MyModel.AllStream(500, ct).ToBatches(500))
{
    await DoBulkWorkAsync(batch, ct);
}
```

Helpers you can compose with streams:

- ToBatches(int batchSize): IAsyncEnumerable<IReadOnlyList<T>>
- ForEachAsync(Func<T,Task> action, int degreeOfParallelism = 1)
- WithProgress(Action<ProgressInfo> onProgress)
- UpTo(int maxItems)
- WithRetry/OnErrorContinue

## Imperative paging (Pager + EntityCursor)

Prefer this when you want explicit Next/End semantics, total counts, or resumability via tokens.

```csharp
// Create a cursor for the full set (or use ForQuery for subsets)
var cursor = await EntityCursor.ForAll<MyModel, string>(ct);

// Wrap in a pager
var pager = await Pager.From(cursor, pageSize: 500, includeTotal: true, ct);

// Inspect metadata
var total = pager.TotalCount; // may be null if counting is disabled/expensive
var pageSize = pager.PageSize;

// Iterate pages
while (!pager.End)
{
    foreach (var item in pager.Items)
    {
        // process item
    }
    await pager.NextAsync(ct);
}

// Resume later using cursor token (opaque)
var token = pager.Cursor; // persist token
// ...later
var resumed = await Pager.From(cursorToken: token, pageSize: 500, includeTotal: false, ct);
```

Notes:

- Token is opaque and adapter-specific; treat it as a bookmark only.
- Stable order by Id ascending is guaranteed.
- TotalCount is optional; enable via includeTotal when you need it.

## Explicit page access (Page/FirstPage)

```csharp
// First page explicitly (UI lists, etc.)
var first = await MyModel.FirstPage(size: 50, ct);

// Arbitrary page
var page3 = await MyModel.Page(page: 3, size: 50, ct);
```

These APIs apply server-side limits consistently across adapters. Prefer these for user-facing pagination.

## Choosing the right modality

- All/Query: small to medium sets; fast scripts; when you explicitly want the full list.
- AllStream/QueryStream: long-running jobs; memory-safe pipelines; easy batching.
- Pager/EntityCursor: operational jobs that need Next/End, resume tokens, or optional counts.
- Page/FirstPage: UI and APIs with classic pagination.

## Example: vector job (AdminController)

Replace full materialization with streaming to avoid loading the entire dataset:

```csharp
// Before (materializes all)
var all = await Models.AnimeDoc.All(ct);
await seeder.StartVectorUpsertAsync(all, ct);

// After (streaming batches)
await foreach (var batch in Models.AnimeDoc.AllStream(batchSize: 500, ct).ToBatches(500))
{
    await seeder.StartVectorUpsertAsync(batch, ct);
}
```

## Troubleshooting

- “I only got 50 items from All/Query”: ensure you’re using no-options overloads. If you need one page only, use FirstPage/Page explicitly.
- “Stream order looks odd”: order is by Id ascending; verify your Ids are stable and comparable.
- “TotalCount is null”: enable includeTotal when constructing the pager; some adapters may still omit it when expensive.
