---
id: DATA-0151
slug: identity-ordered-streams-seek-after-the-last-identity
domain: Data
status: Accepted
date: 2026-10-21
title: A stream in identity order continues after the last identity it read
related:
  - DATA-0107
  - DATA-0113
  - DATA-0119
  - DATA-0143
  - DATA-0148
  - DATA-0149
amended_by: DATA-0170
---

# DATA-0151: A stream in identity order continues after the last identity it read

> **Amended by [DATA-0170](DATA-0170-a-list-continues-from-one-sealed-keyset-cursor.md) (2026-11-07): `IdentitySeek` is a `Keyset<T>` of the identity alone,**
> and a list read a page at a time seeks over a caller's order too, a nullable member narrowing it to rows where it is set. Streams still page a caller's order by number.

Amends [DATA-0107](DATA-0107-provider-bounded-entity-streams.md) and
[DATA-0113](DATA-0113-bulk-reads-use-the-strongest-strategy.md), which stay dated records.

## Context

Every provider-bounded stream asked for numbered pages, `LIMIT n OFFSET (page - 1) * n` or MongoDB's `Skip`. A numbered
page is a position, and a position moves when another writer changes the rows before it:

- two deletes behind the read made the next page start two rows late, so those rows were never read. A Move
  reported success with them still in the source (NK-306), and `await foreach (var t in Todo.AllStream()) await
  t.Remove();` visited about half the rows (NK-207);
- an insert behind the read, or a row entering the predicate behind it, made the next page repeat a row. A Move
  copied it twice and journaled it twice (NK-205);
- page `k` makes the store walk past `k * n` rows, so reading a large selection costs quadratically, and the cutover
  pager did the same (NK-206);
- the offset is an `Int32`, so a stream stopped at about 2^31 rows, after the transfer had written them, and transfer
  counters were `int` as well (NK-254).

Identities other than `string` did not stream at all, so a Guid- or int-keyed Entity on SQLite, PostgreSQL or MongoDB was
transferred through one materialized read (NK-179, NK-016).

## Decision

1. **A stream whose order leads with the Entity identity seeks.** That is every unsorted `AllStream()` and
   `QueryStream(predicate)`, an explicit `Id` or `-Id` order, and so every transfer and backup read. The first page is
   `LIMIT n`; each next page is the read's own filter `AND Id > last` (`<` descending), again as page 1. `IdentitySeek`
   in `Koan.Data.Core.Querying` owns it. The seek is an ordinary `Gt`/`Lt` filter node, pushed through the same
   pushdown every query uses; Koan never compares identities in memory.
2. **The store's comparison decides.** The page is ordered by the same identity column the seek compares, so both use
   the store's own order of that key: text by its collation (SQLite, PostgreSQL text keys), a native `uuid` or integer
   column by its type (PostgreSQL), a BSON value by BSON order (MongoDB). The identity is unique under that comparison,
   so the order has no ties for the seek to cross. A stream's sequence is the one a single unpaged read of the same
   store returns; it is not a cross-provider order.
3. **`string`, `Guid`, `int` and `long` identities stream.** `QueryStreamCoordinator.IsProviderStableIdentifierType`
   admits them, proven on SQLite, PostgreSQL and MongoDB by `IdentitySeekConvergence`. `BulkRead` therefore streams
   those Entities on a bounded provider instead of materializing them. Any other key type is still refused by the
   stream and materialized by `BulkRead`, with the reason in the fact and the transfer's warning.
4. **What stays numbered.** A stream ordered by a caller's own key still pages by number: a seek over a tuple of
   caller keys would need each store's null placement, which Koan does not normalize. A provider that cannot push the
   seek also falls back to numbered pages. The `koan.data.stream.execution` selected fact says which pager ran.
5. **A provider that ignores the seek is refused.** A page that contains the row the seek continued from would make
   the stream repeat forever; it is rejected as `InvalidStreamReceipt` before any of it is yielded.
6. **The verified cutover pager seeks too.** `IDataEntityRootSession.ReadPage(after, pageSize)` continues after the last
   identity, and a root whose adapter cannot push the seek is blocked at plan time like one without bounded paging.
7. **Transfer counters are 64-bit.** `TransferResult.ReadCount`, `CopiedCount`, `DeletedCount` and
   `TransferAuditBatch.BatchNumber`, `TotalProcessed` are `long`.

## Consequences

- A row present in the source for the whole read is returned exactly once, whatever another writer inserts or deletes.
  A row inserted behind the read is not returned, a row deleted ahead of it is not, and a row inserted ahead of it is.
  It is not a snapshot: a row updated during the read is returned as its page read it. A Move still deletes only rows it
  copied and that are unchanged (DATA-0143, DATA-0148, DATA-0149 are untouched).
- Each page costs the same however deep the read is, and no page number bounds a stream or a cutover.
- A Guid-, int- or long-keyed transfer on a bounded provider no longer carries the "materialized query" warning.
- `TransferResult` and `TransferAuditBatch` counters changed type from `int` to `long`. Reading them into `var`, `long`,
  arithmetic, string formatting, JSON or assertions compiles unchanged; assigning one to an `int` needs a cast, and a
  binary compiled against the `int` members must be rebuilt. BUILD-0073 permits the change inside the 1.x train.
