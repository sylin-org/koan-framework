---
id: DATA-0126
slug: a-declared-source-owns-its-connection
domain: Data
status: Accepted
date: 2026-10-21
title: A declared source owns its connection or says it shares one
related:
  - DATA-0077
  - DATA-0125
  - ARCH-0103
---

# DATA-0126: A declared source owns its connection or says it shares one

## Context

`AdapterConnectionResolver` resolves the connection a provider opens for a source, by priority: the source's own
generic connection, `Koan:Data:Sources:{source}:{provider}:ConnectionString`, `ConnectionStrings:{source}`, then the
provider-wide `Koan:Data:{provider}:ConnectionString`. When none of them answered for a named source:

- `ResolveRoutedConnection`, which every adapter factory calls, returned the adapter's discovered default (for
  SQLite `.koan/data/Koan.sqlite`) whether the source's connection was absent, blank or the literal `auto`. Its own
  comment claimed an absent placement kept the fail-loud path; the early return made that unreachable.
- `ResolveConnectionString` recursed into the `Default` source.

So `Koan:Data:Sources:Analytics:{Adapter: sqlite}` with the connection string missing or misspelled made
`EntityContext.Source("Analytics")` read and write the Default database under the same table names: Analytics rows
appeared in Default's tables and the reverse, with no error and no fact. A channel source whose connection was
forgotten served drafts to the channel's audience, and a Database-mode axis source missing its connection merged its
tenant into the shared database (ledger NK-010, NK-036). DATA-0125's correction text describes this sharing as the
current behaviour.

## Decision

**A source declared under `Koan:Data:Sources` resolves to a connection of its own, to the provider-wide connection,
or to the provider's default only when it says `auto`. Otherwise its first operation is refused.**

1. For a declared non-Default source with no connection at any priority, both resolver entry points throw
   `InvalidOperationException`. The message names the source and provider, lists
   `Koan:Data:Sources:{source}:ConnectionString`, `Koan:Data:Sources:{source}:{provider}:ConnectionString`,
   `ConnectionStrings:{source}` and `Koan:Data:{provider}:ConnectionString`, and names `auto` as the way to share.
2. The literal `auto` stays the declared intent to share the provider's discovered default connection.
3. `Koan:Data:{provider}:ConnectionString` stays a valid answer for a named source. It is configuration the
   application wrote for that provider, and server providers that separate sources by a per-source `Database` or
   search-path setting rely on it.
4. A name that is not declared (a factory-internal route, a `Direct` connection lookup) keeps the previous
   behaviour: the discovered default, or the Default source.
5. The Default source is unchanged.

This amends DATA-0125's correction text: a declared source without its own connection no longer shares its
adapter's default store implicitly.

## Compatibility

- A declared named source with an adapter and no connection, and no provider-wide connection, now fails on its first
  operation with the message above, instead of reading and writing the Default database. Applications that relied on
  the sharing add `ConnectionString: auto` to the source, or configure its own connection.
- Rows already written to the Default database through such a source stay there; Koan cannot tell them apart from
  Default's own rows.
- No public signature or stored format changes.

## Guarantee and correction

- A declared source's rows land in a database the configuration names for that source (its own connection, the
  provider-wide one) or the default the source explicitly asked to share.
- The correction is in the message: configure the source's connection, or set it to `auto`.

## Evidence

- `AdapterConnectionResolverRoutingSpec` (Koan.Tests.Data.Core): a declared `Archive` source with a null, empty or
  blank connection is refused by `ResolveRoutedConnection` and by `ResolveConnectionString` (which used to recurse into
  Default); `auto` still shares the discovered default; the provider-wide key still answers. The three refusal cells
  failed before this change. The former `""` cell of the "uses the resolved default" theory, which pinned the defect,
  is now a refusal cell.
- `SqliteSourceConnectionSpec` (Koan.Data.Connector.Sqlite.Tests): a `Preview` source with an adapter and no
  connection refuses a save and the Default database stays empty; before this change the save succeeded into
  Default. `ConnectionString: auto` still shares the Default connection.
- `SqliteVecVectorAodbConformanceSpec` (VectorAdapterSurface.SqliteVec, run with `DOTNET_ENVIRONMENT=Development`):
  a vector route for an undeclared source keeps resolving, which is why the refusal is limited to declared sources.
