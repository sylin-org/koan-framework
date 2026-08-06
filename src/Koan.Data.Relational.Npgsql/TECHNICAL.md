# Technical notes

Concrete providers own discovery, configuration, identity, and startup reporting. This package owns only their common
Npgsql data paths and receives every provider decision through immutable options.

- `NpgsqlRepository<TEntity,TKey>` is the only Entity execution path.
- `NpgsqlEntityPlan<TEntity,TKey>` compiles both managed Id + `jsonb` storage and explicit physical maps.
- `NpgsqlSchema` validates or creates only Managed storage; External storage is validated without DDL.
- `NpgsqlFeatures` is the single family capability declaration used by both repository diagnostics and concrete
  provider claim publication.
- `NpgsqlStableOrder` is an immutable provider policy, not an arbitrary SQL callback. It selects PostgreSQL physical
  tuple order or the compiled identity roots and leaves all CRUD/query mechanics on the single repository path.
- Native connection pooling remains Npgsql-owned. Koan's bounded plan cache stores no connection.
- Managed and explicit maps share native CRUD, query, count, batch, conditional-write, and transaction mechanics.
