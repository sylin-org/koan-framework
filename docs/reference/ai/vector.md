---
type: REFERENCE
domain: ai
title: "Store and search Entity vectors"
audience: [developers, operators, architects, ai-agents]
status: current
last_updated: 2026-08-29
framework_version: v1.0.0
validation:
  date_last_tested: 2026-07-28
  status: verified
  scope: source-owned vector spaces, complete points, bounded search, policy, and capability correction
---

# Store and search Entity vectors

Declare the vector decision once, then use ordinary Entity-centered terminals.

```csharp
builder.Services.AddKoan(koan =>
{
    koan.Data.Source("Semantic").Vector<Media>(space => space
        .Name("media")
        .Dimensions(1536)
        .Metric(VectorMetric.Cosine)
        .Visibility(VectorVisibility.Session));
});

await Vector<Media>.Save(media.Id, embedding, new { media.Genre }, ct);

VectorSearchResult<string> related = await Vector<Media>.Search(
    embedding,
    query => query
        .Top(20)
        .Where(Filter.Eq("Genre", "science-fiction"))
        .AtLeast(.80),
    ct);
```

`Vector<T>.Search` is the primitive — it returns matches with similarities and leaves loading to you.
When the outcome is simply "entities like this phrase", the `Entity.Ai` gateway composes embed, search,
and load in one call: `Media.Ai.Search("science fiction", s => s.Top(20), ct)` — see
[Semantic search](../../capabilities/ai/semantic-search.md).

`Name`, `Dimensions`, `Metric`, and `Visibility` are immutable source-owned decisions. Koan binds them before adapter
creation, applies source access and lifecycle policy at the first execution boundary, and gives the selected adapter one
complete `VectorSpacePlan`.

## The compact surface

- `Save`, `Get`, `Delete`, `Clear`, and `Sync` operate on complete vector points.
- Batch `Save`, `Get`, and `Delete` preserve input order; get-many preserves missing positions.
- `Search` accepts `Top`, `Where`, `Space`, `AtLeast`, `Text`, `SemanticWeight`, and `After`.
- `VectorMatch.Similarity` is finite `[0,1]`, with higher values always closer.
- `VectorSearchResult.Execution` says which metric ran, whether work was exact or approximate, and how many candidates
  were considered when the provider can know.
- Metadata is `DataObject`/`DataArray`, not a provider dictionary or JSON DOM.

A clause is available only when the selected adapter announces and implements it. Unsupported hybrid search,
continuation, filtering, named spaces, visibility, export, or atomic batch behavior fails with a correction; Koan does
not simulate a stronger provider.

## Routing and policy

`EntityContext.Source(...)` selects among declared source spaces. `Vector<T>.WithPartition(...)` applies the same
partition and isolation plan as Entity data. Read-only sources reject saves, deletes, clears, and lifecycle mutation
before adapter creation or I/O. External lifecycle rejects schema creation or repair but does not imply read-only data.

Use `Vector<T>.GetCapabilities()` to inspect the selected provider. A configured provider failure or unsupported
operation never falls back silently to another store.

## InMemory as the exact floor

`Sylin.Koan.Data.Vector.Connector.InMemory` supplies an ephemeral, bounded, exact brute-force implementation. It is the
zero-infrastructure semantic oracle for Session visibility, full metadata filters, bulk operations, normalized scores,
and isolation. It deliberately declines durability, Eventual visibility, hybrid search, continuation, multi-vector
points, export, and atomic batches.

Use a durable or networked connector when the application needs persistence, multi-process sharing, approximate
indexes, or provider-native capabilities. The [data adapter development primer](../../architecture/data-adapter-development-primer.md)
is the normative adapter contract.
