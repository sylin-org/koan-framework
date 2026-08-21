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

*(Drafted; not yet accepted. The measurement is re-derived and current, and the reading below reshaped the
proposal once already — the four-way collapse this record opened with is not available.)*

**The collapse is over all four adapters. The constraint that would have excluded SQLite has been removed.**

This record opened proposing four, narrowed to three on reading, and is back to four — and the round trip is
the useful part. Dapper's `GetTypeDeserializerImpl` emits IL at runtime, which NativeAOT forbids, so SQLite was
Dapper-free and a Dapper-based core could not have included it. Then reading the call sites showed the three
server adapters never used the feature that constraint protects: every call was untyped or scalar, and each
immediately cast the row to `IDictionary<string, object>` to hand a dictionary to `plan.Hydrate`. Dapper was a
dictionary reader and a parameter bag, both already provided AOT-clean by `Koan.Data.Relational/Ado`.

Removing it (PMC-047) dissolved the split rather than working around it. So:

- **A core over all four adapters.** It owns the member set, the command sequence, parameter binding,
  materialization, batch semantics, and the receipt every read returns.
- **The dialect owns spelling**, as it already does, and the members that differ by one token become calls into
  it rather than copies.
- **Execution is `AdoCommands` and `SqlParameters`** — one surface, no runtime IL emit, so the core does not
  reintroduce the constraint it just removed.

**Not everything that looks like grammar is grammar.** `Count` reads as boilerplate and is not: SQL Server
issues `COUNT_BIG(1)` where the others issue `COUNT(1)`, because `COUNT` returns an `int` and overflows past
two billion rows. Flattened to the majority spelling it would be silently wrong on the largest tables anyone
runs. It belongs in the dialect.

**`Describe` is not collapsible either**, and an earlier draft of this record said it was 94% identical. It is a
one-line delegation to each store's own capability declaration — exactly the thing an adapter should keep. The
94% was an artifact of a range-matching script that ran past the member's end.

## Consequences

- The four adapters stop holding four copies of the same execution. A fix lands once across all of them.
- A test that exercises one adapter's shared path now exercises every adapter's, which is the coverage gap
  behind this cycle's pattern of defects being found by a suite downstream of the code that broke.
- Adapters shrink toward a driver and a dialect, which is the precondition ARCH-0094 needs.
- **NativeAOT reaches the servers — measured 2026-08-21, PMC-049.** Removing Dapper removed the blocker
  ARCH-0093 *named*; whether the provider libraries would then publish was a separate question, and it is now
  answered by publishing rather than by argument. All four drivers produce a working native binary that
  writes and reads through `Entity<T>` against a real server: `Npgsql` 10.0.3 on PostgreSQL 17 and on
  CockroachDB v24.3, `MySqlConnector` 2.6.1 on MySQL 8.4, and `Microsoft.Data.SqlClient` 7.0.2 on SQL Server
  2022. `samples/fundamentals/AotRelational` is the reproduction. One provider constraint attaches, and it is
  the driver's rather than AOT's: SqlClient refuses globalization-invariant mode, so a SQL Server build carries
  culture data. Three framework defects on the AOT path had to be repaired first — see ARCH-0093 — one of which
  had silently broken SQLite's certified proof three weeks earlier.
- The risk is real and is why this is Proposed rather than Accepted: a collapse that flattens a genuine
  per-store difference converts correct implementations into one that is subtly wrong everywhere. `Count` is
  the worked example — `COUNT_BIG` reads as a spelling preference and is an overflow bound. The mitigation is
  order: collapse the members already read and classified, prove the suites stay green at each step, and treat
  every unread member as a decision until its difference is explained rather than assumed.

## Evidence

The member-by-member reading is under way. What it has established:

| Member | Verdict |
|---|---|
| `InstructionSql`, `Delete`, `CreateBatch` | Byte-identical across all four. Collapsible as-is. |
| `Order` | Logically identical across all four after PMC-046. Collapsible, and the first member ready. |
| `IdentityPredicate` | Differs by one token — which dialect's `Quote` is called. Collapsible through `QuoteIdent`. |
| `GetMany` | Same code; differs by `plan` versus `_plan`, connection type names, and one statement inlined rather than assigned. Collapsible. |
| `StableOrder` | Differs only because SQLite aliases the row as `koan_row` and the others do not. Collapsible within the three. |
| `Query` | Same logic in all three. The only difference is the page clause — `OFFSET n ROWS FETCH NEXT m ROWS ONLY` against `LIMIT m OFFSET n`. Collapsible behind one dialect member. |
| `Count` / `CountExact` | Collapsible **only** behind a dialect member. SQL Server's `COUNT_BIG(1)` is an overflow bound, not a spelling preference: `COUNT` returns an `int`. |
| `Insert` | SQL Server and MySQL are identical apart from the quote. PostgreSQL is not: `jsonb` will not take a plain text parameter, so structured values bind as `CAST(@p AS jsonb)`, and its upsert is folded in as `ON CONFLICT` with managed-field guards. Collapsible only behind a dialect member for structured binding, and only once upsert is separated from it. |
| `Upsert` | **A decision, not grammar.** Three idiomatic strategies with different semantics: `OUTPUT INSERTED` plus `IF @@ROWCOUNT` on SQL Server, `LAST_INSERT_ID()` and an explicit transaction for managed-field scope on MySQL, `RETURNING` with `ON CONFLICT` and an affected-rows cross-scope check on PostgreSQL. Stays adapter-owned. |
| `Describe` | **Not collapsible.** A one-line delegation to each store's capability declaration, which is adapter-owned by design. An earlier draft of this record called it 94% identical; that was an artifact of a range-matching script running past the member's end. |
| Data access | **Was the structural split; no longer is.** SQLite used raw ADO throughout while the other three used Dapper exclusively, which ARCH-0093 made a hard constraint. Reading the call sites showed the three never used Dapper's compiled materializer, so it was removed (PMC-047) and all four now execute through the same AOT-clean surface. |

Two things the reading turned up that are not about the collapse:

- **`Koan.Data.Relational/Ado` is dead.** `AdoCommands` and `SqlParameters` are tracked, AOT-clean, and used by
  nobody — SQLite hand-rolls its commands instead of calling them. Their documentation still describes a
  "Dapper-backed twin with the same surface" that was deliberately retired (R11-02), so the surface has
  outlived both its twin and its only intended caller. `QueryIdJsonAsync` also pins the retired `(Id, Json)`
  storage shape, which the mapping model has since outgrown. Carried as PMC-047.
- **A textual similarity ratio was computed twice and rejected twice.** Raw ratios put `GetMany` at 34% while
  the two bodies are the same code; folding the mechanical differences away made it worse, scoring `UpdateSet`
  at 3% through a distortion the normalizer introduced. Where the question is whether two implementations are
  the same, read them.

Still required before this record is accepted:

- The remaining unread members: `Batch`, `UpdateSet`, `ExecuteSql`, `QueryRaw`, `CountRaw`, `UpsertMany`,
  `DeleteMany`, `DeleteAll`, `RemoveAll`, `ConditionalReplaceAsync`, `Open`, `Ready`, `Provision`, `Validate`,
  `Materialize`, `Where`, `ExecuteAsync`. Two members already moved from "known divergence" to "collapsible"
  once read — `Order` and `Query` — and one moved the other way, `Upsert`. Assume nothing about the rest.
- A named collapse order, smallest and most certain first, with the suites green at each step.
