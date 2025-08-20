# Vector Search contracts (Sora.Data.Vector)

Status: Planned (see ADR DATA-0054)

This guide defines Sora’s vector datasource signature and accessory elements. It complements, but does not replace, classic query surfaces (LINQ/string). Vector search is a parallel capability focused on kNN/top‑K similarity.

See also:
- Support: Vector Adapter Acceptance Criteria (docs/support/09-vector-adapter-acceptance-criteria.md)

## Contracts (proposed)

- Capability flags (reported by adapters)
  - VectorSearch, FilterPushdown, ContinuationTokens, Rerank, AccurateCount (vector)

- Repository interface

```csharp
namespace Sora.Data.Vector.Abstractions;

public sealed record VectorQueryOptions(
  ReadOnlyMemory<float> Embedding,
  int TopK,
  string? VectorField = null,
  string? Metric = null,            // "cosine"|"dot"|"l2"
  string? Filter = null,            // adapter-specific or portable subset
  string? Continuation = null,      // adapter token if supported
  int? EfOrNProbe = null,
  TimeSpan? Timeout = null,
  bool IncludeScores = true);

public sealed record VectorHit<TKey>(TKey Id, double Score, string? Raw = null);
public sealed record VectorSearchResult<TKey>(IReadOnlyList<VectorHit<TKey>> Hits, string? Continuation = null);

public interface IVectorSearchRepository<TEntity, TKey>
  where TEntity : class, Sora.Data.Abstractions.IEntity<TKey>
  where TKey : notnull
{
  Task<VectorSearchResult<TKey>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default);
}

public interface IVectorIndexInfo
{
  int Dimensions { get; }
  string Metric { get; }             // normalized name
  Sora.Data.Vector.Abstractions.VectorCapabilities Capabilities { get; }
}

[Flags]
public enum VectorCapabilities { None = 0, VectorSearch = 1, FilterPushdown = 2, Rerank = 4, ContinuationTokens = 8, AccurateCount = 16 }
```

- Index instructions (via IInstructionExecutor)
  - vector.index.ensureCreated
  - vector.index.rebuild
  - vector.index.stats
  - plus existing data.clear

- Attribute for model mapping

```csharp
[AttributeUsage(AttributeTargets.Property)]
public sealed class VectorEmbeddingAttribute : Attribute
{
  public int Dimensions { get; init; }
  public string Metric { get; init; } = "cosine";
  public string? IndexName { get; init; }  // if different than storage name
  public string? Field { get; init; }      // adapter-specific field/column name
}
```

Notes:
- These definitions are specification-only here; implementation will live in Sora.Data.Vector.

## Usage patterns

### 1) Search → hydrate pattern (recommended)

```csharp
// Assume: services.AddSora(); vector adapter registered (e.g., Weaviate)
var data = sp.GetRequiredService<IDataService>();
var primary = data.GetRepository<Person, string>();         // source of truth
var vectors = (IVectorSearchRepository<Person, string>)primary; // or resolved separately if vector-only

var embedding = await embedder.CreateAsync("search text...");
// Prefer typed filter AST; JSON-DSL is available for interop/advanced scenarios.
var filter = Sora.Data.Abstractions.VectorFilter.And(
  Sora.Data.Abstractions.VectorFilter.Eq("country", "US"),
  Sora.Data.Abstractions.VectorFilter.Gte("price", 10)
);
var options = new VectorQueryOptions(embedding, TopK: 20, Filter: filter);

var result = await vectors.SearchAsync(options, ct);
var ids = result.Hits.Select(h => h.Id).ToArray();

var hydrated = new List<(Person Entity, double Score)>(ids.Length);
foreach (var hit in result.Hits)
{
  var entity = await primary.GetAsync(hit.Id, ct);
  if (entity is not null) hydrated.Add((entity, hit.Score));
}
// hydrated list preserves score order
```

### 2) Vector-only entity store

Some engines can store full entities (JSON payload) alongside vectors.

```csharp
var vectorRepo = sp.GetRequiredService<IVectorSearchRepository<Document, string>>();
var rsp = await vectorRepo.SearchAsync(new(embedding, TopK: 10, Filter: Sora.Data.Abstractions.VectorFilter.Eq("tag", "kb")));
// engine may also let you fetch documents directly by id from the same store
```

### 3) Index lifecycle with instructions

```csharp
var exec = (Sora.Data.Abstractions.Instructions.IInstructionExecutor<Person>)primary;
await exec.ExecuteAsync<bool>(Instruction.Create("vector.index.ensureCreated"));
var stats = await exec.ExecuteAsync<Dictionary<string, object?>>(Instruction.Create("vector.index.stats"));
```

## Capability negotiation

- Repositories expose classic query flags (LINQ/string) separately from vector flags.
- If VectorSearch is advertised but LINQ is not, call SearchAsync instead of LINQ.
- Avoid faking LINQ over vector search in the facade; prefer explicit capability checks.

## Acceptance criteria (adapters)

- Must
  - Implement SearchAsync(topK, embedding[, filter]) with scores ordered best-first.
  - Support vector.index.ensureCreated and data.clear.
  - Report VectorSearch capability.
- Should
  - FilterPushdown, ContinuationTokens when engine supports them.
- Nice
  - Rerank and AccurateCount where feasible; vector.index.stats.

## Entity<TEntity> surface to implement (sparse, meaningful)

For vector-enabled adapters, implement the minimal entity surface below. Keep semantics honest; don’t fake unsupported features.

- Must
  - IVectorSearchRepository<TEntity, TKey>.SearchAsync(options)
  - IDataRepository<TEntity, TKey> lifecycle: UpsertAsync, UpsertManyAsync, DeleteAsync, DeleteManyAsync, DeleteAllAsync
  - IBatchSet<TEntity, TKey>: best-effort only; throw on RequireAtomic = true
  - IInstructionExecutor<TEntity>: vector.index.ensureCreated, data.clear (optional: vector.index.stats, vector.index.rebuild)
  - IQueryCapabilities/IWriteCapabilities: advertise VectorSearch; omit LINQ/String if not supported; Writes = no native bulk unless truly supported

- Optional
  - GetAsync(id): return full entity if the vector store keeps payloads; otherwise support only when paired with a primary repo for hydration
  - QueryAsync(object?) / LINQ: only if the engine offers a viable pushdown; avoid in-memory fallbacks for large sets

- Operational
  - Health: lightweight readiness check (ping/info)
  - Options: Endpoint, ApiKey/Secret, DefaultTopK, DefaultMetric, Dimensions (if fixed), timeout; guardrails on TopK and timeouts
  - Naming: resolve set/class/index via StorageNameRegistry
  - Cancellation: observe CancellationToken on all async paths; throw on cancellation promptly

Notes
- Count across vector results is not guaranteed; don’t expose AccurateCount unless cheap and correct
- Prefer explicit capability checks in callers instead of bridging vector search to LINQ

## Naming and options

- Use StorageNameRegistry to derive collection/class names per entity set.
- Adapter options should include: Endpoint/Connection, ApiKey/Secret, DefaultTopK, DefaultMetric, DefaultDimensions (optional), timeouts, and engine tunables.

## First adapter target: Sora.Data.Weaviate

- Search: GraphQL/REST nearVector with where filter; topK; cursor for continuation.
- Entities: store JSON payload in properties or separate primary store with hydration.
- Health: readiness endpoint; instruction mapping to schema class ensure.

See ADR: DATA-0054 — Vector Search capability and contracts.
