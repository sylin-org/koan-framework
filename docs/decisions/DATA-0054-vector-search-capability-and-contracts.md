# DATA-0054 — Vector Search capability and contracts

Status: Accepted
Date: 2025-08-20

## Context

Sora’s data model centers on aggregates (`IEntity<TKey>`) and adapter-specific query capabilities (LINQ, string queries, server paging). Vector databases expose a different access pattern: similarity search (top‑K) over embeddings with optional metadata filtering. LINQ/string semantics do not generalize well to vector engines.

## Decision

- Introduce Vector Search as a first‑class, parallel capability.
- Keep existing query surfaces unchanged for classic adapters; add vector‑specific contracts instead of forcing LINQ/string onto vector stores.
- Adapters advertise support via capability flags.

## Consequences

- Non‑breaking: current adapters continue to report their LINQ/string capabilities.
- Vector‑enabled adapters implement a dedicated search interface and set capability flags accordingly.
- Callers branch by capability rather than assuming LINQ/string everywhere.

## Specification (high‑level)

- Capability flags (extend query capability reporting):
  - VectorSearch, FilterPushdown, ContinuationTokens, AccurateCount (vector), Rerank.
- Vector contracts:
  - `IVectorSearchRepository<TEntity, TKey>` with `SearchAsync(VectorQueryOptions)` → `VectorSearchResult<TKey>`.
  - `VectorQueryOptions`: Embedding, TopK, Metric (cosine|dot|l2), optional Filter, Continuation, engine‑tunables (e.g., ef/nprobe), Timeout, IncludeScores.
  - `VectorSearchResult<TKey>`: Hits (Id, Score) + Continuation token.
  - Index ops via instructions: `vector.index.ensureCreated`, `vector.index.rebuild`, `vector.index.stats` (plus existing `data.clear`).
- Utilities:
  - Hydration helper: perform vector search → hydrate full entities using a primary repository while preserving score order.

## Rationale

- Uses domain‑correct language (Search) and avoids leaking LINQ/SQL semantics into vector adapters.
- Preserves Sora’s simplicity: identity and lifecycle via existing repos; vector search via a focused interface.
- Capability flags make behavior explicit and discoverable.

## Adoption plan

1. Add `Sora.Data.Vector.Abstractions` package with interfaces, options, and instruction constants.
2. Extend the capabilities reporting to include Vector flags (without breaking existing enum values).
3. Document a capability matrix and dual‑store pattern (vector index + primary repo).
4. Implement a pgvector adapter prototype, then Qdrant/Milvus.

## Alternatives considered

- Overloading LINQ to express vector similarity: rejected due to ambiguity and poor pushdown story across engines.
- String query surface for vectors: rejected; no portable DSL exists across engines, leading to leakage of engine‑specific syntax.
