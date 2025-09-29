<!-- Auto-generated from docs/reference/_data/adapters.yml. Do not edit manually. -->

| Adapter | Storage | Tx | Batching | Paging | Filter | Schema | Instruction | Vector | Guardrails | Notes |
|---|---|---:|---:|---|---|---|---|---|---|---|
| SQLite | relational/sqlite | Yes | No | native | linq | migrations | direct | none |  | Embedded development provider; ships as the default local store. |
| PostgreSQL | relational/postgresql | Yes | Yes | native | linq | migrations | direct | pgvector |  | Primary production relational provider; enables pgvector for semantic workloads. |
| SQL Server | relational/sqlserver | Yes | Yes | native | linq | migrations | direct | none |  | Enterprise SQL Server adapter with full transactional semantics. |
| MongoDB | document/mongodb | No | Yes | limited | bson | \"—\" | aggregation | atlas-search |  | Document-first adapter using native aggregation pipelines and Atlas Search when available. |
| Redis JSON | keyvalue/redis | No | Yes | limited | search | none | commands | redisearch |  | High-throughput cache and search scenarios backed by RedisJSON and RediSearch. |
| JSON Files | filesystem/json | No | No | none | in-memory | none | none | none |  | Simplest adapter for demos and local-first prototypes; all evaluation performed in-process. |
| Milvus | vector/milvus | No | Yes | top-k | hybrid | collections | commands | milvus |  | Specialized vector database integration used for high-recall semantic retrieval. |
| Weaviate | vector/weaviate | No | Yes | top-k | hybrid | classes | graphql | weaviate |  | Managed vector search platform with hybrid filters and GraphQL façade. |
