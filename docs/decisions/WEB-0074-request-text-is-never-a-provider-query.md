---
id: WEB-0074
slug: request-text-is-never-a-provider-query
domain: Web
status: Accepted
date: 2026-10-19
title: Request text is never a provider query unless the endpoint says so
related:
  - DATA-0096
  - WEB-0068
  - ARCH-0092
---

# WEB-0074: Request text is never a provider query unless the endpoint says so

## Context

`EntityQueryParser` copied the request's `q` into `QueryOptions.Q`, and `EntityEndpointService` handed it to
`Data<T, K>.QueryRaw` whenever the read carried no filter. On SQLite and PostgreSQL that text is SQL. The SQLite adapter
wrapped it as `WHERE (<q>)`; the PostgreSQL adapter appended it to `WHERE`, or ran it verbatim when it started with
`select `. Both ran it on an ordinary read-write connection. Any caller allowed to read an `EntityController` collection
(anonymous by default) could read the whole database with a `UNION`, and both drivers executed stacked statements - a
measured `DROP TABLE` ran. The MCP edge forwarded the `q` tool argument into the same call and advertised it to agents
as "Full-text query term."

The documents disagreed with the code. The web reference called `q` "a separate free-text slot". DATA-0096 section 7.5
retained the raw string path "as an explicit escape hatch, gated behind a capability flag; never the default and never
a silent match-all". WEB-0068 had already named the supported way to give `q` a meaning: a request-options hook that
turns it into a predicate and clears it. Koan has no framework free-text search over an Entity: nothing declares which
fields are searchable (the `Searchable` flag belongs to vector schemas), and there is no ranking or operator over "all
text".

## Decision

**Request text is untrusted. The edge never runs it as a provider query unless the endpoint declared that it should.**

1. **`q` is carried, then refused.** The parser still fills `QueryOptions.Q`, so request-options hooks can see it.
   `EntityEndpointService` - the one core REST and MCP both run through - refuses what is left when the read executes:
   `400 { code: "web.read.freeTextUnsupported", message }`, naming `filter`, `POST /query`, and the hook. A blank `q` is
   ignored. Nothing is silently dropped: `q` beside a filter used to be discarded with an information log.
2. **The DATA-0096 escape hatch survives as a declaration.** `EntityController.AllowRawQuery` (protected virtual, default
   `false`) makes `?q=` a provider-native query for that controller, through the adapter's `IRawQueryRepository`, and
   only alone: beside a filter or a server-side constraint it answers `web.read.freeTextNotComposable`. It is REST-only by
   design. The MCP edge never grants it and the MCP collection schema no longer lists `q` - what a schema describes
   must be what the edge enforces (ARCH-0092) - and a terse `[RestEntity]` controller has no place to declare it.
3. **The hatch is given a floor, not a sanitizer.** SQLite and PostgreSQL now run `QueryRaw`/`CountRaw` as exactly one
   statement and read-only. `RawSql.RequireSingleStatement` refuses `;` and NUL anywhere in the text - lexer-free on
   purpose, because a lexer that disagrees with the engine is how a second statement gets through. PostgreSQL runs the
   read in a `SET TRANSACTION READ ONLY` transaction; SQLite runs it on an unpooled connection with
   `PRAGMA query_only = ON`, because a pooled connection returns to its pool still holding the pragma. CockroachDB
   shares the Npgsql repository, so it gets the same single-statement rule and the same transaction; its read-only mode
   refuses DML but, measured, not DDL, which the single-statement rule keeps out of reach. A mistaken opt-in costs a
   read, not the database. A predicate can still read: `UNION` is a read.

## Guarantee and correction

- A client cannot reach a provider query through `q` by default, on HTTP or MCP. It gets the correction instead.
- An application that wants search writes the filter (`AddPredicate`) in a hook. That is the same code on both surfaces.
- An application that wants a SQL predicate from trusted callers writes one visible override on the controller.
- The MySQL, SQL Server, Firebird, and DuckDB raw paths are not confined yet; under the override they run the text as
  written, and the override's documentation says so. SQL Server needs more than a separator rule - T-SQL batches need no `;`.

## Concepts

One: `AllowRawQuery`, the business decision "this endpoint serves provider-native queries to its callers". Everything
else is existing vocabulary: `QueryOptions.Q`, request-options hooks, `Filter`, `IRawQueryRepository`.

## Alternatives considered

- **Delete the HTTP hatch.** DATA-0096 retains it, trusted operator surfaces use it, and application code keeps
  `Entity<T>.QueryRaw`. Consent was the missing piece, not removal.
- **A global option.** Coarser than per endpoint; the decision belongs where the endpoint is declared.
- **An attribute on the entity.** It would reach MCP too, and agents are the most steerable callers.
- **A framework free-text search over every string property.** Nothing declares which fields may be searched, a match
  over all fields is an oracle for fields the caller may not read (`FieldAccess`), and ranking and tokenisation are
  product decisions. WEB-0068's hook is the seam; a later decision may promote a helper.
- **A capability token gating the override by adapter hardening.** The right shape for backend parity, but it adds a
  token, a claim profile, and a conformance cell; recorded as follow-up rather than folded into a security fix.

## Evidence

- `AdapterSurfaceSpecsBase` (SQLite and PostgreSQL run it against real stores): `q` payloads - `UNION`, stacked `DROP`,
  a leading `select ` - are refused on `GET` and `POST /query` and the table is untouched; blank `q` is ignored; the
  `AllowRawQuery` controller runs a predicate, still refuses a second statement, and refuses `q` beside a filter.
- `SqliteRawQueryBoundarySpec`, `PostgresRawQueryBoundarySpec`: a second statement is refused before the engine sees it,
  raw reads run `query_only` / read-only and cannot undo it from inside, and a side-effecting predicate is refused.
- `RawSqlSpec`: the text rule, including separators inside literals, identifiers, and comments.
- `RawQueryRefusalSpec`, `RequestTextHookSpec`: the MCP translator never grants the opt-in, `q` is not advertised, and a
  hook is the one way `q` reaches the data layer.
