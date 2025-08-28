<!-- Auto-generated from docs/reference/_data/adapters.yml. Do not edit manually. -->

| Adapter | Storage | Tx | Batching | Paging | Filter | Schema | Instruction | Vector | Guardrails | Notes |
|---|---|---:|---:|---|---|---|---|---|---|---|
| Sqlite | Relational | Yes | Yes | true | partial | governance-ddl | direct | none |  | Schema governance and DDL policy (DATA-0046); LINQ pushdown with fallback (DATA-0007, DATA-0032). |
| Postgres | Relational | Yes | Yes | true | full | governance-ddl | direct | optional-pgvector |  | JSONB projection; ON CONFLICT upsert; DDL policy. pgvector optional (DATA-0060). |
| SqlServer | Relational | Yes | Yes | true | full | governance-ddl | direct | none |  | Computed projection columns via JSON_VALUE; bulk upsert/delete; DDL policy. |
| Mongo | Document | No | partial | partial | full | n/a | direct | optional |  | Filter pushdown; paging may fallback (DATA-0032). |
| Redis | KeyValue | No | partial | none | none | n/a | limited | none |  | In-memory filtering and client-side paging; ensure/clear instructions. |
| Weaviate | Vector | No | partial | n/a | true | native | direct | native |  | GraphQL nearVector with where filter; cursor continuation. See DATA-0054; primary store hydration recommended. |
| Json | Filesystem | No | partial | none | none | n/a | limited | none |  | Dev/local adapter; LINQ in-memory; best-effort batch; not for large datasets. |
| Vault | Secrets | n/a | n/a | n/a | n/a | n/a | n/a | none |  | HashiCorp Vault KV v2 provider. Uses secret+vault:// URIs; integrates with health checks. |
