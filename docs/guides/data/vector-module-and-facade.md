# Vector module and facade (Sora.Data.Vector)

This guide explains the new vector module split and how to use vector features via Sora.Data.Vector.

## Package split

- Contracts live in Sora.Data.Vector.Abstractions (IVectorSearchRepository, IVectorAdapterFactory, VectorCapabilities, VectorQueryOptions/Result/Match, VectorEmbeddingAttribute, VectorAdapterAttribute).
- Implementation and helpers move to Sora.Data.Vector:
  - AddSoraDataVector(IServiceCollection)
  - IVectorService (resolution/caching)
  - VectorDefaultsOptions (Sora:Data:VectorDefaults:DefaultProvider)
  - Facades: Vector<TEntity> (preferred) and VectorData<TEntity, TKey>/VectorData<TEntity>
  - Orchestration: SaveWithVector and SaveManyWithVector with VectorEntity<TEntity>

## Configuration

Bind default vector provider:

- Sora:Data:VectorDefaults:DefaultProvider = "weaviate" (example)

## Usage

- Preferred, terse facade:
  - await Vector<MyDoc>.Save((id, vec, meta), ct);
  - await Vector<MyDoc>.Save(items, ct);
  - var res = await Vector<MyDoc>.Search(new VectorQueryOptions(query, TopK: 10), ct);
  - if (Vector<MyDoc>.IsAvailable) { var caps = Vector<MyDoc>.GetCapabilities(); }
  - Optional maintenance (provider-dependent): await Vector<MyDoc>.EnsureCreated(); await Vector<MyDoc>.Clear(); await Vector<MyDoc>.Rebuild(); var n = await Vector<MyDoc>.Stats();

- Orchestrate document + vector save:
  - var ve = new VectorEntity<MyDoc>(doc, vector);
  - await VectorData<MyDoc>.SaveManyWithVector(new[]{ ve });

## Adapter resolution precedence

- [VectorAdapter] on entity → config DefaultProvider → entity source provider → first registered IVectorAdapterFactory → fail if none.

## Samples

- S5.Recs sample updated to call Sora.Data.Vector facade for seeding and querying.
