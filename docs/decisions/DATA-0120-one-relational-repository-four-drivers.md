---
id: DATA-0120
slug: one-relational-repository-four-drivers
domain: DATA
status: Proposed
date: 2026-08-21
title: One relational repository, four drivers
related:
  - ARCH-0094
  - DATA-0119
---

# DATA-0120: One relational repository, four drivers

## Precedence

This record extends DATA-0119 from schema work to the read and write path. DATA-0119 established where a
relational *decision* lives and what a result reports; it left four repositories each holding a private copy of
the same execution. This record is authoritative for **what a relational adapter still owns** once that copy is
gone.

## Application contract

Unchanged. Nothing here is visible to an application:

```csharp
var open = await Todo.Query(item => !item.Done);
await todo.Save();
```

That is the point. This is a change to who holds the code behind those two lines, not to what they do.

## Context

Four relational repositories — SQLite (983 lines), MySQL (701), PostgreSQL/CockroachDB (659), SQL Server
(653) — expose **the same 27 members under the same names** and implement most of them the same way.

Measured on 2026-08-21 against the current tree. Three members are byte-identical across all four once
whitespace is stripped: `InstructionSql`, the public `Delete` overload, and `CreateBatch`. `IdentityPredicate`
differs in exactly one token — which dialect's `Quote` it calls — and every dialect already implements
`IRelationalMappingDialect.QuoteIdent`. `Describe` differs only in the provider name and capability constants
it reports.

**A textual similarity ratio was computed across all 27 members and then rejected as evidence**, in both
directions, and the reason is worth recording. `GetMany` scores 34% between SQL Server and Npgsql, yet the two
bodies are the same code: the ratio is consumed by `plan.` versus `_plan.`, by the four different connection
type names, and by one statement assigned to a local in one copy and inlined in the other. A ratio understates
duplication wherever a mechanical difference repeats, and would overstate it wherever two stores happen to
spell different logic similarly. The only evidence that settles a member is reading it.

So the honest statement of the problem is not a percentage. It is that the member set is identical, the shapes
rhyme throughout, and most differences sampled so far have been mechanical rather than semantic. `Query`,
`StableOrder`, `ExecuteSql` and the `Batch` nested class are where divergence looks real and has not yet been
explained. One sampled difference was neither mechanical nor a store decision: MySQL's `Order` carries a
tiebreaker the other three lack, which is a framework gap the collapse would otherwise erase (PMC-046).

This matters beyond tidiness for the reason DATA-0119 gave: grammar is generable and verifiable, decisions are
not, and every decision left in adapter code is one a generated adapter can silently get wrong (ARCH-0094). It
also matters for defect economics. Three defects this cycle were found by a suite downstream of the code that
broke, because the same logic exists four times and any given test exercises one copy. A fix applied to one
copy is not a fix.

## Decision

*(Drafted; not yet accepted. The measurement above is re-derived and current; the shape below is the proposal
this record exists to review.)*

**One relational repository core executes; each adapter supplies a driver and a dialect.**

- The core owns the member set, the command sequence, parameter binding, materialization, batch semantics, and
  the receipt every read returns.
- The dialect owns spelling. It already does — `QuoteIdent`, `Parameter`, `Read`, `EscapeLike`,
  `JsonArrayContains`, `JsonArrayLength`, `JsonArrayOrderTerm` — and the members that differ by one token
  become calls into it rather than copies.
- The driver owns the connection type, the command type, and how a result set is read.
- The adapter keeps its capability declaration and the decisions that are genuinely its own.

**A decision stays with the adapter only when it is a decision** — and which members those are is not yet
established. `Order` was carried into this draft as a known divergence on the strength of an earlier
measurement, and reading it disproved that: SQLite, PostgreSQL and SQL Server implement it identically, comment
included. MySQL alone differs, and its difference is not a store decision at all — it appends the identity
columns to every ORDER BY as a tiebreaker, which is the framework's job and is now half-done there
(`FilterPushdownCoordinator.EnsureOrderForPage` supplies an order only when the caller named none). That is a
correctness gap, carried as PMC-046, and it has to be settled before `Order` is collapsed: collapsing onto the
three-store majority would delete the only implementation that is currently right.

## Consequences

- Roughly two thousand lines stop existing in four copies. A fix lands once.
- A test that exercises one adapter's shared path now exercises every adapter's, which is the coverage gap
  behind this cycle's downstream-discovery pattern.
- Adapters shrink toward a driver and a dialect, which is the precondition ARCH-0094 needs.
- The risk is real and is the reason this is Proposed rather than Accepted: a collapse that flattens a genuine
  per-store difference converts four correct implementations into one that is subtly wrong everywhere. The
  mitigation is order — collapse the byte-identical members first, prove the suites stay green, and treat every
  member that is *nearly* identical as a decision until its difference is explained.

## Evidence

Gathered so far:

- All four repositories share 27 identically-named members; `InstructionSql`, `Delete` and `CreateBatch` are
  byte-identical across all four, and `IdentityPredicate` differs by one dialect call.
- A textual similarity ratio was computed and rejected as a metric, with `GetMany` as the worked example of why
  (34% similar, same code).

Still required before this record is accepted:

- A member-by-member reading of all 27, classifying each difference as mechanical, a store constraint, or a
  defect. Reading is the only thing that tells those apart, and the reading has already earned its keep:
  `Order` was assumed divergent and is not, while MySQL's version of it turned out to hold a correctness fix
  the other three are missing. A collapse driven by majority rule would have deleted it.
- Confirmation that SQLite's extra 300 lines are its own concerns rather than a fifth copy of something.
- A named collapse order, smallest and most certain first, with the suites green at each step.
