Koan.Data.Vector.Abstractions - Technical reference

Contract

- Interfaces and records for vector stores: schema, upsert, delete, query, and similarity search.
- Inputs: vectors (float[]), metadata, ids; Outputs: matches with scores and metadata.

Design

- Provider-agnostic abstractions consumed by Data.Vector providers (e.g., Weaviate).
- Keep shapes minimal and explicit; avoid magic constants; use typed options where tunable.

Options

- Distance metric (cosine/euclidean/dot), default topK, namespace/collection, timeouts.

Error modes

- Invalid dimension, unsupported metric, provider unavailable, timeouts, rate limiting.

Edge cases

- Empty vectors; large batch upserts; eventual consistency on index build; schema migrations.

References

- ./README.md
- ../Koan.Data.Vector/TECHNICAL.md
