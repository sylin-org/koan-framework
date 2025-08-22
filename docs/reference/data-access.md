# Data Access Reference

Canonical APIs and semantics for Sora data access. Newer ADRs supersede older ones (see references).

## Contract
- First-class static model methods only for top-level access:
  - All(ct), Query(..., ct)
  - AllStream(batchSize, ct), QueryStream(predicate|filter, batchSize, ct)
  - FirstPage(size, ct), Page(page, size, ct)
- All/Query without paging must fully materialize the result set.
- Prefer streaming (AllStream/QueryStream) or Pager/EntityCursor for large sets.
- Stable iteration order is by Id ascending across adapters.

## When to use what
- All/Query: small to medium sets; when the full set is desired.
- AllStream/QueryStream: jobs/pipelines; memory-safe iteration; easy batching.
- Pager/EntityCursor: operational jobs; Next/End; resume tokens; optional counts.
- Page/FirstPage: UI and public APIs with classic pagination.

## Examples

```csharp
// Full collection
var all = await MyModel.All(ct);

// Filtered subset
var drama = await MyModel.Query(m => m.Genres.Contains("Drama"), ct);

// Streaming in batches
await foreach (var item in MyModel.QueryStream(m => m.Score >= 80, batchSize: 500, ct))
{
    await ProcessAsync(item, ct);
}

// Pager usage
var cursor = await EntityCursor.ForAll<MyModel, string>(ct);
var pager = await Pager.From(cursor, pageSize: 500, includeTotal: true, ct);
while (!pager.End)
{
    foreach (var item in pager.Items) { /* ... */ }
    await pager.NextAsync(ct);
}
```

## Filters and pushdown
- JSON filter language and endpoints (DATA-0029); $options.ignoreCase (DATA-0031).
- Paging pushdown with in-memory fallback (DATA-0032); guardrails (DATA-0044).

## References
- guides/data/all-query-streaming-and-pager.md
- decisions/DATA-0061-data-access-pagination-and-streaming.md
- decisions/DATA-0029-json-filter-language-and-endpoint.md
- decisions/DATA-0031-filter-ignore-case-option.md
- decisions/DATA-0032-paging-pushdown-and-in-memory-fallback.md
- decisions/DATA-0044-paging-guardrails-and-tracing-must.md
