# Adapter matrix data (maintainers)

Source of truth
- This folder contains `adapters.yml`, the authoritative capability data for the Adapter Matrix.
- The matrix page is auto-generated during docs build into `../_generated/adapter-matrix.md`.

Fields
- name: Adapter display name (e.g., Sqlite, Postgres, Mongo, Redis, Weaviate, Json).
- storage: One of Relational | Document | KeyValue | Vector | Filesystem.
- transactions: true|false (or string for nuanced support like "partial").
- batching: true|false|partial.
- pagingPushdown: true|false|partial|n/a.
- filterPushdown: full|partial|none|n/a.
- schemaTools: governance-ddl|native|n/a.
- instructionApi: direct|limited|n/a.
- vector: native|optional|optional-pgvector|none.
- vectorCapabilities: (optional) search, filterPushdown, continuationTokens, rerank, accurateCount.
- guardrails: defaultPageSize, maxPageSize (or defaultTopK for vector stores).
- notes: Short human-readable clarifications; prefer linking ADR IDs.

Policy
- When adding or updating an adapter, update `adapters.yml` in the same PR and run a Strict docs build.
- Keep notes concise; link ADRs (e.g., DATA-0046) instead of restating policy.
- If a field doesn’t apply, use `n/a` instead of omitting it where possible.

Validation
- The build script generates a Markdown table from this YAML; ensure values are normalized to the sets above.
- If PowerShell’s `ConvertFrom-Yaml` is unavailable, a simple parser fallback is used—keep the YAML flat under `adapters:` with guardrails nested only.
