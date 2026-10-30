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
- Native connection pooling remains Npgsql-owned. Koan's plan cache stores no connection.
- A repository keeps one compiled plan per physical table (a partition's table included) in a `RelationalPlanCache`,
  an LRU bounded by `Koan:Data:Mapping:PlanEntries`. A full cache drops the least recently used plan and compiles it
  again on that table's next use, so a partition per tenant or per day never stops working (DATA-0162).
- A set of ids, and a filter's `In`/`Nin`, binds as one typed PostgreSQL array compared with `= ANY` (`<> ALL`), so it
  meets no parameter limit and the store declares no `MaximumSetMembers`. Arrays carry text, integers, uuids,
  numerics, doubles and reals, booleans, intervals, dates, times, and timestamps whose members share one
  `DateTimeKind`. A filter's set of any
  other or mixed type binds one parameter per member, and a filter whose statement then passes 65,535 parameters, less
  those the statement binds itself, is refused, naming its sets; `GetMany`
  and `DeleteMany` over a composite identity, or over keys no array carries, run in slices of 30,000 parameters
  (DATA-0162).
- Managed and explicit maps share native CRUD, query, count, batch, conditional-write, and transaction mechanics.

## Enum storage contract

Default Entity storage preserves enum names as strings, including nullable values, nested values, collections,
and declared EnumMember aliases. Unnamed numeric values fail instead of silently changing the storage format.
Queries use the same spelling. Ordinary enum ordering uses declared ordinal ranks in native expressions while
the stored value stays a string; native ordering of arbitrary Flags combinations rejects correctively.
An explicit external mapping codec remains responsible for its declared physical representation.
Existing numeric rows or columns require a separate, explicit migration; upgrades do not rewrite them automatically.
