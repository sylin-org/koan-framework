# Vector module and facade (Sora.Data.Vector)

This guide explains the new vector module split and how to use vector features via Sora.Data.Vector.

## Package split

- Contracts live in Sora.Data.Vector.Abstractions (IVectorSearchRepository, IVectorAdapterFactory, VectorCapabilities, VectorQueryOptions/Result/Match, VectorEmbeddingAttribute, VectorAdapterAttribute).
- Implementation and helpers move to Sora.Data.Vector:
  - AddSoraDataVector(IServiceCollection)
  - IVectorService (resolution/caching)
  - VectorDefaultsOptions (Sora:Data:VectorDefaults:DefaultProvider)
  - Facades: VectorData<TEntity, TKey> and VectorData<TEntity>
  - Orchestration: SaveWithVector and SaveManyWithVector with VectorEntity<TEntity>

## Configuration

Bind default vector provider:

- Sora:Data:VectorDefaults:DefaultProvider = "weaviate" (example)

## Usage

- Resolve the facade:
  - VectorData<MyDoc>.UpsertManyAsync(items)
  - VectorData<MyDoc>.SearchAsync(options)
- Or orchestrate document + vector save:
  - var ve = new VectorEntity<MyDoc>(doc, vector);
  - await VectorData<MyDoc>.SaveManyWithVector(new[]{ ve });

## Adapter resolution precedence

- [VectorAdapter] on entity → config DefaultProvider → entity source provider → first registered IVectorAdapterFactory → fail if none.

## Samples

- S5.Recs sample updated to call Sora.Data.Vector facade for seeding and querying.
