---
id: DATA-0170
slug: a-list-continues-from-one-sealed-keyset-cursor
domain: Data
status: Accepted
date: 2026-11-07
title: A list continues from one sealed keyset cursor over a declared order
amends:
  - DATA-0151
  - WEB-0087
related:
  - WEB-0076
  - DATA-0092
  - DATA-0119
---

# DATA-0170: A list continues from one sealed keyset cursor over a declared order

## Context

A list a person or an agent reads page after page, newest first or by name, had only numbered pages. A numbered page is a position:
a Topic started while a reader is on page one moves every row down one, so page two repeats the last row of page one, and a Topic
removed skips one. Each page also makes the store walk every row before it, and the endpoint counted the whole set for headers no
reader of a long list needs. DATA-0151 had already made identity-ordered streams seek after the last identity they read, through the
internal `IdentitySeek`, but a list ordered by anything else (a date, a name, a position) could not seek, because "a seek over a
tuple of caller keys would need each store's null placement". So every application with a long list built its own: Tangent holds
six hand-built cursors, each with its own filter, its own text form, and some with their own seal.

**The business sentence:** read the next page of this list, in this order, from where the last page ended.

```csharp
static readonly Keyset<Receipt> Newest = Keyset<Receipt>.By(order => order.OrderByDescending(receipt => receipt.At));

var page = await Receipt.Page(Newest, after, 50, receipt => receipt.ConnectionId == id, ct); // page.Items, page.Next
```

```csharp
[Route("t/{tangent}")]
[Declared]
public sealed class TopicsController : EntityController<Post>, IRequestOptionsHook<Post>, IEmitHook<Post>
{
    [HttpGet("")]
    [Tool(Name = "read_tangent")]
    [Pagination(DefaultSize = 20, MaxSize = 50, DefaultSort = "-LastActivityAt")]
    public override Task<IActionResult> GetCollection(CancellationToken ct) => base.GetCollection(ct);

    // ...an options hook narrows the rows; an emit hook composes the page, linking ctx.Next.
}
```

## Decision

1. **`Keyset<TEntity>` is a declared order a list continues through:** the members it names, then the Entity identity, which breaks
   every tie and takes the direction of the last member named (ascending where none is). `Keyset<T>.By(order => ...)` or
   `Keyset<T>.By("-LastActivityAt")` builds it once; a member that holds many values or is an object is refused where it is built,
   and so is an identity that is not a `string`, `Guid`, `int` or `long` (DATA-0151's provider-stable identities).
2. **`Entity.Page(order, after, size, ...)` answers at most `size` rows after `after`, and the key the next page continues from.**
   The read is the caller's filter, the order and `size + 1` rows as one first page: the extra row says whether a next page exists,
   and nothing is counted. `after` is a `KeysetKey`, the values of the order's members on the last row of the page before, read off
   that row; null reads the first page. The application decides its own loop, so a reader that judges rows itself reads `Next`
   again until its page is full.
3. **The seek compares in the store.** The rows after a key are, for each member, those no further than the key's value and, at its
   own value, the rest of the order after the key's (`k <= v AND (k < v OR ...)` descending), so an index on the order answers each
   page as one range. It is an ordinary filter, pushed like every other, and the page is ordered by the same store, so both use the
   store's own comparison of each member. A provider that cannot push the comparison, or the caller's whole filter with it, is
   refused before it reads, and one that did not apply the order or the page is refused after, each with `NotSupportedException`;
   Koan never pages a keyset in memory, so every page reads at most `size + 1` rows.
4. **A member that can hold null keeps the list to rows where it is set.** Stores place nulls differently, and a null has no place
   to continue from, so a `DateTimeOffset?` or `string` member adds `member != null` to every page.
5. **A key leaves the server sealed.** `Keyset<T>.Write` and `Read` carry a key as text typed by the order's members, so it reads
   back exactly, a date to the tick. `SealedCursor.Seal` and `Open` in Koan.Web protect that text with ASP.NET Data Protection under
   one purpose per list, so a reader holds it without reading the values it holds, and one altered, sealed for another list or by
   another key ring opens to nothing. `SealedCursor.Refusal()` is the 400 that names `after`.
6. **A declared controller's collection is keyset-only.** Its order is its `[Pagination]`'s `DefaultSort` (the identity alone where
   it names none), compiled where the routes are, so an order no list can continue through stops the host. It takes `after`, the
   sealed key, and `size`, at most `[Pagination]`'s `MaxSize` as the host's page bounds (`Koan:Web:Pagination`) read it, the bound
   its route tool states, and its `DefaultSize` when left out; it pages by nothing else, so
   `page`, `pageSize` and `all` are refused as words it does not take, and an `[Input]` member named after any of the five stops the
   host. It answers `{ "items": [...], "next": "..." }`, `next` null on the last page, with no offset, count, `X-Total-*`, `X-Page*`
   or `Link` header. An `after` that does not open is refused 400 naming `after`. The purpose it seals under is its controller's
   type, so one list's cursor never continues another.
7. **An emit hook composes the page with `ctx.Next`,** the `after` of the next page or null, set before the collection's emit. When a
   hook replaces the answer, the record it composes is the answer.
8. **Both doors run one definition.** The collection's route tool takes `after` and `size` beside its input, with `size`'s bound in
   its description, and its answer is the same object, so a tool call continues with the `next` it was given.
9. **`IdentitySeek` is gone.** An identity-ordered stream and the verified cutover's root reader seek through a `Keyset<T>` of the
   identity alone, with the same filter and the same refusal as before.

## What this amends

- DATA-0151 §4: a seek over a caller's key needs no store's null placement once a nullable member narrows the list to rows where it
  is set. Streams still page a caller's order by number; only lists read a page at a time seek over it.
- WEB-0087 §6 and its consequence: a declared collection does not open numbered paging by declaring `Page`; it continues from
  `after`, a page of `size` rows, and nothing else.

## Consequences

- A row present for the whole reading is read exactly once, whatever another writer inserts or removes ahead of the cursor or behind
  it; a row inserted ahead is read, one inserted behind is not. It is not a snapshot: a row whose order members change during the
  reading may be read twice or not at all.
- A declared collection's JSON answer is an object, not an array, so a tool's answer is structured content.
- A store whose filter support lacks comparison, `AnyOf` or `AllOf`, or that does not order and page in its store, cannot serve a
  keyset list, and says so. SQLite and the in-memory store are proven here; another connector is proven when its own suite runs a
  keyset list.
- A cursor sealed by one key ring does not open under another, so a reader holding one across a key ring's rotation past its
  retention starts the list again, refused with `after` named.

## Ergonomics

New public concepts: `Keyset<TEntity>` (`By`, `Order`, `KeyOf`, `Write`, `Read`), `KeysetKey`, `KeysetPage<TEntity>` (`Items`,
`Next`), `Entity.Page(order, after, size, ...)`, `SealedCursor` (`Seal`, `Open`, `Refusal`), `HookContext<T>.Next`, and the adapter-
facing `EntityCollectionRequest.Keyset` with `KeysetRead`. `[Pagination]` keeps its members; `DefaultSort` is a declared collection's
order. A declared collection needs no new attribute: the order, the default and the bound are where a collection's paging already
lives.

## Coalescence

- **Owner:** `Keyset<TEntity>` in `Koan.Data.Core.Querying` owns the order, the seek and the key's text, and `Data<TEntity, TKey>.Page`
  is the one read that pages it; `SealedCursor` owns the seal; `EntityOperation` compiles a declared collection's order once per
  action, which REST and the route tool's schema both read.
- **Absorbed:** `IdentitySeek` (DATA-0151), now a `Keyset<T>` of the identity alone. Tangent's hand-built cursors (Receipt, Applications,
  People's seal, ViewableTangents', TopicList's `after` and Export's) are its consumers and move onto it in their own packages.
- **Not built:** a `Link` header beside `next` (one truth for where the list goes on); a backward cursor (no reader asks for one);
  counting a keyset list (it would walk the set the seek exists not to walk).

## Evidence

- `DeclaredControllerSpecsBase` (Koan.Web.AdapterSurface, SQLite and in memory, real `AddKoan()` hosts): a (text, id) order returns
  every row once across an insert ahead of the cursor and one behind it, with a tie on the text at a page's edge; a (date desc, id)
  order with instants shared does the same through the page its emit hook composes from `ctx.Next`; an altered `after`, one that is
  no cursor and one sealed for another list are refused naming `after`; a page holds at most its bound, carries no count or paging
  header, and `page` and a `size` that is no number are refused naming them.
- `EntityStreamingSpec` (Koan.Tests.Data.Core, a provider that records its queries): a keyset page asks for one row past the page,
  as page one, in the order with the identity last, with the seek as one range, and counts nothing; a key reads back exactly as
  written; a provider without comparison pushdown is refused before it reads, and so is a filter it cannot apply whole, and one that
  did not apply the order after; an order
  over an object is refused where it is built. The identity-seek specs of streams and the cutover hold unchanged.
- `DeclaredOperationsSpec` (Koan.Mcp.Verbs.Tests, real HTTP and real `tools/call`): the collection's tool takes its input, `after` and
  `size` with its bound as the host's page policy reads it, and a page holds at most that bound on both doors; a list continues from
  the `next` its page answered, alike on both doors, and a cursor that does not open is refused naming `after`.
