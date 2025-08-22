# Adapter Capability Matrix

Authoritative capabilities by adapter (Relational, Document, Vector). See adapters.yml for the raw data source.

Columns
- Storage type, Transactions, Batching, Paging pushdown, Filter pushdown
- Schema tools, Instruction API, Vector support/filters/cursor
- Guardrails and known limits

Adapters
- Sqlite, SqlServer, Postgres, Mongo, Redis, Weaviate (Vector), Json

Generated table

> The matrix below is generated from `reference/_data/adapters.yml` during docs build.

[!include[](../reference/_generated/adapter-matrix.md)]

References
- Support/08, 09 acceptance criteria, adapter ADRs 0046–0054+, guides/adapters
