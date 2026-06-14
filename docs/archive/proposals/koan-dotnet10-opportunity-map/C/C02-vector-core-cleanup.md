# C2 — Vector/Core cleanup (stretch)

**Intent**: Tighten ingestion hooks/shared transforms in `Koan.Data.Vector` adapters (Weaviate-first), unify capability flags and bulk APIs.  
**Why**: Better ergonomics and consistent performance across vector providers.

## Plan (high level)
- Normalize `SaveWithVector`/`SemanticSearch` surfaces across adapters.  
- Profile bulk operations and paging; add provider capability detection similar to Data.QueryCaps. fileciteturn0file14

## Acceptance Criteria
- The same RAG sample runs unmodified on Weaviate and Postgres vector backends.
