---
id: DATA-0119
slug: adapters-express-grammar-the-framework-owns-guarantees
domain: DATA
status: Accepted
date: 2026-08-20
title: Adapters express grammar; the framework owns guarantees
related:
  - ARCH-0084
  - DATA-0032
  - DATA-0107
  - DATA-0113
---

# DATA-0119: Adapters express grammar; the framework owns guarantees

## Precedence

This record is authoritative for **where a data decision lives** and for **what a query result reports**.

It amends DATA-0032 in one direction only: the in-memory fallback remains the correctness floor that
record established, and its requirement that the fallback be *visible* generalizes — from one response
header covering pagination on the web surface, to every axis on every surface, carried as a runtime
fact. It builds on ARCH-0084, which made capability declaration uniform, by adding the half ARCH-0084
did not need: what an application may *require*. DATA-0107 continues to govern stream rejection, and
DATA-0113 continues to govern bulk-read strategy selection.

## Application contract

**Business sentence:** ask for the data; rely on what the framework says you got.

```csharp
var page = await Todo.Page(1, 20);
await foreach (var todo in Todo.AllStream("-CreatedAt")) { … }
```

The application states intent and reads results. It never learns which layer applied the sort, and it
is never handed a page whose order was not applied to the whole set. Where a backend cannot meet a
guarantee the entity requires, composition refuses at `AddKoan()` with the capability named — not at
the first request that happens to expose it.

## Outcome

Three rules, in the order they constrain each other:

1. **An adapter expresses grammar, not decisions.** The words for `MAX`, `OFFSET`, quoting and JSON
   traversal belong to the adapter. *Whether* to paginate, *what* order makes a page meaningful,
   *whether* auto-DDL may run — these are framework decisions with one owner each.

2. **A result reports guarantees, not labor.** `Complete`, `Bounded` and `Stable` are what an
   application can act on. Who performed the work is diagnostic detail, not the headline.

3. **A guarantee that can be declared can be required.** An entity or source may demand one, and
   composition negotiates or rejects correctively.

## Context

A structural pass over twelve data connectors and four relational runtimes on 2026-08-20 found seven
instances of one pathology: a decision that belongs to the framework, made instead by each adapter.

- **SQL Server pages without a stable order.** Its `StableOrder` is `ORDER BY (SELECT NULL)`, which
  exists to satisfy the parser for `OFFSET/FETCH` and guarantees nothing. `sortComplete` short-circuits
  when no sort was requested, so paging proceeds. Two successive pages may repeat and skip rows.
  `[Pagination(DefaultSort)]` is opt-in, so the default `EntityController<T>` is exposed. SQLite and
  PostgreSQL are unaffected because their answer to the same question is the identity.
- **Three relational stores create schema in production without consent.** `RelationalDdlGate` exists
  and says in its own documentation that it was written because adapters spelled this gate three ways
  and none honored `Koan:AllowMagicInProduction`. `SqlServerSchema` and `NpgsqlSchema` — PostgreSQL,
  CockroachDB, SQL Server — contain zero references to it.
- **The relational schema-orchestration subsystem is dead.** 746 lines, registered in DI by
  `RelationalModule`, resolved by nothing outside its own assembly. Four adapters hand-roll schema
  creation, which is *why* the gate was adopted twice: it lives inside an orchestrator nobody calls.
- **"Only paginate a fully ordered set" is spelled four ways across six adapters**, and
  `QueryReceiptValidator` — the chokepoint every receipt passes through — never asserts it. Couchbase
  paginated without it until this cycle and no test noticed, because a wrong page is a plausible page.
- **`Handled` means two things.** A relational adapter sets `SortHandled` when SQL did the work;
  `KeyValueStore` sets it after materializing every record and calling the framework's own sorter. The
  type cannot tell those apart, so the in-memory fallback fact added this cycle is structurally blind
  to exactly the adapters that materialize everything.
- **Sparse Entity projection** was removed the same day for the same reason: `TEntity` had no
  vocabulary for *absent*, so the feature blanked fields, produced entities indistinguishable from real
  data, and destroyed rows when saved.

None of these was visible in output. A query answered in memory returns the right rows; a schema
created without consent creates the right table; a page taken against no order is still a plausible
page. Comparing results cannot locate a seam, because results are what stays the same across a good
seam and a bad one.

### Why the vocabulary is the root cause

Three of the four query-result axes answer *who did the work*. Only the count axis answers what an
application can rely on — `IsEstimate` says whether the number is trustworthy. That asymmetry explains
the defects rather than merely accompanying them: a receipt shaped around labor cannot express
"this page is not stable", so nothing asked, and `ORDER BY (SELECT NULL)` passed every check the
framework had.

## Decision

**Decisions move to one owner each.**

- The **total order** a page is taken against is the framework's. When a query has pagination and no
  sort, the coordinator appends the entity identity before any adapter sees it — the same rule
  `QueryStreamCoordinator.EnsureTotalOrder` already applies to streams. Adapter `StableOrder`
  implementations stop being consulted for paged reads.
- The **pagination invariant** is asserted by `QueryReceiptValidator`: a receipt claiming
  `PaginationHandled` without a complete sort is rejected. Adapters keep their own spelling of *when*
  to page; forgetting the rule becomes loud.
- **Auto-DDL consent** is evaluated on the path every relational schema owner takes, so consent is
  structural rather than remembered.

**Results report guarantees.** The query receipt gains the three the application acts on — complete,
bounded, stable — and keeps who-performed-it as diagnostic detail beneath them. A receipt that cannot
distinguish "the store did it" from "the adapter materialized everything and did it here" is a type
that cannot express its own subject.

**Refusals use the shape that already works.** `KoanMagic` — capability, risk, remedy, consent — is
the framework's semantic refusal record. New refusals adopt it rather than inventing a second
vocabulary of ad-hoc strings.

**Facts name the loss, not the mechanism.** An in-memory fallback is reported as what the application
lost — the read was unbounded, the whole collection was materialized — not as which layer did the work.

## Consequences

- A relational adapter that omits a framework decision no longer produces a quiet wrong answer; it
  fails a receipt assertion or does not compile.
- `StableOrder` becomes vestigial for paged reads, and the four implementations — one of which was
  wrong — become candidates for deletion rather than repair.
- Unsorted paged queries acquire an `ORDER BY` on the identity. This is a deliberate cost: paging
  without an order is meaningless, and the alternative is the defect above. Unpaged reads are
  unaffected.
- Adapters shrink toward a dialect and a driver. That is a precondition for ARCH-0094's generated
  adapters, not merely tidiness: grammar is generable and verifiable, decisions are not. Every decision
  left in adapter code is one a generated adapter can silently get wrong.
- Guarantee-shaped capabilities can be required, which moves a class of failure from the first
  exposing request to composition.

## Evidence

- Two successive unsorted pages over the same corpus partition it exactly — no repeats, no gaps — on
  every adapter, including SQL Server. This test does not exist today.
- A receipt claiming provider pagination with an incomplete sort is rejected by the validator.
- `SqlServerSchema` and `NpgsqlSchema` refuse automatic DDL in Production without consent, matching
  SQLite and MySQL, and the refusal names the table.
- The cross-adapter no-fallback sweep stays green, and the fallback fact reports what was lost.
- The full data suite and the web adapter surfaces stay green throughout.
