---
uid: reference.modules.Koan.data.vector
title: Koan.Data.Vector - Technical Reference
description: Facade and abstractions for vector storage and search across providers.
since: 0.2.x
packages: [Sylin.Koan.Data.Vector]
source: src/Koan.Data.Vector/
---

## Contract

- Provider-agnostic facade to save/search embeddings for entities
- Stable inputs: entity id, embedding vector, optional metadata; outputs: topK results with scores

## Embedding contract

- Dimensionality: determined by your model; keep consistent per entity type
- Metric: cosine similarity (default), provider-dependent alternatives (e.g., L2)
- Normalization: ensure vectors are normalized when the metric expects it

## Search parameters

- `topK` - number of nearest neighbors to return (default 10)
- Optional score threshold; provider-specific ef/accuracy knobs when available

## Operations

- Health: provider-specific readiness
- Metrics: index latency for save/search; vector count; cache hit rates (if used)

## References

- Data access patterns: `/docs/guides/data/all-query-streaming-and-pager.md`
