---
type: REFERENCE
domain: web
title: "Pagination"
audience: [developers, architects]
status: current
last_updated: 2026-08-22
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-22
  status: verified
  scope: read against src/Koan.Web — PaginationAttribute, PaginationPolicy, PaginationSafetyBounds, EntityController.GetCollection, and the headers EntityEndpointService emits
---

# Pagination

Every `EntityController<T>` collection endpoint paginates. With no attribute and no configuration a
collection answers with **50 items**, and a client may ask for up to **200**.

```http
GET /api/todos
```

```http
200 OK
X-Page: 1
X-Page-Size: 50
X-Total-Pages: 8
X-Total-Count: 372
Link: </api/todos?page=2&size=50>; rel="next", </api/todos?page=8&size=50>; rel="last"

[{ "id": "01937d2c-…", "title": "Ship it" }, …]
```

**The body is the collection itself, not an envelope.** Paging travels in headers, so a client that
does not care about paging deserializes an ordinary array.

| Header | Meaning |
| --- | --- |
| `X-Page`, `X-Page-Size` | the page actually served, after clamping |
| `X-Total-Pages` | pages at that size |
| `X-Total-Count` | matching rows; absent when `IncludeCount = false` |
| `Link` | `first`, `last`, and `prev`/`next` where they exist |
| `Koan-InMemory-Paging` | `true` when the adapter could not page the query and the framework paged the materialized result |

That last header is the one worth alerting on: it means the whole matching set crossed the process
boundary before being cut down.

## Choosing the shape

`[Pagination]` sits on the controller, or on a single action to override the controller.

```csharp
[Pagination(Mode = PaginationMode.Required, DefaultSize = 20, MaxSize = 100)]
public sealed class TransactionsController : EntityController<Transaction>;
```

| Property | Default | Effect |
| --- | --- | --- |
| `Mode` | `On` | when paging applies — see below |
| `DefaultSize` | `50` | page size when the client names none |
| `MaxSize` | `200` | ceiling on a client-requested size |
| `IncludeCount` | `true` | whether the total is counted and reported |
| `DefaultSort` | none | sort applied when the client names none |

### Modes

- **`On`** — always paginate. The client moves through pages and may resize within `MaxSize`.
- **`Required`** — always paginate, and `?all=true` does not turn it off. This is the mode for data
  whose full extent should not leave the process in one response.
- **`Optional`** — paginate only when the client asks, by sending `page`, `pageSize`, or `size`.
  Otherwise the full set is returned, subject to the absolute cap below.
- **`Off`** — never paginate. No paging headers are emitted and no count is taken.

`Optional` and `Off` return unbounded results by construction. They fit bounded reference data — a
country list, a status enumeration — and they are the wrong answer for anything that grows with use.

### Stable pages need a sort

Paging an unsorted query is undefined across every backend: page 2 may repeat or skip rows from page
1, because nothing obliges the store to return the same order twice. Name a sort the store can serve
cheaply.

```csharp
[Pagination(DefaultSort = "-createdAt,id")]
public sealed class EventsController : EntityController<Event>;
```

`DefaultSort` uses the same grammar as the `?sort=` parameter: comma-separated fields, `-` for
descending, `+` or nothing for ascending. Fields resolve against the entity, and one that does not
resolve is rejected — an unknown `?sort=` field answers `400`, and an unresolvable `DefaultSort`
answers `400` naming `PaginationAttribute.DefaultSort`. A client `?sort=` replaces `DefaultSort`
rather than appending to it.

## Safety bounds

The attribute expresses intent; deployment sets the ceiling. Bounds bind from configuration with the
`Koan.Web` reference — nothing is registered by the application.

```json
{
  "Koan": {
    "Web": {
      "Pagination": {
        "MinPageSize": 1,
        "MaxPageSize": 500,
        "AbsoluteMaxRecords": 10000
      }
    }
  }
}
```

**Bounds win over the attribute.** `DefaultSize` and `MaxSize` are clamped into
`[MinPageSize, MaxPageSize]` as the policy is resolved, so a controller asking for `MaxSize = 1000`
under a `MaxPageSize` of `500` gets `500`. The bounds normalize themselves after binding:
`MaxPageSize` is held to at most `1000`, and `AbsoluteMaxRecords` is raised to at least
`MaxPageSize`, so a partial configuration still yields usable limits instead of failing startup.

`AbsoluteMaxRecords` is the backstop under `Optional` and `Off`. A request that would return more
than the cap is refused with **`413 Payload Too Large`** and a body naming the limit, and the refusal
is logged as a warning with the entity and path. Unpaged is a choice; unbounded is not.

## Deciding per request

`GetPaginationPolicy()` resolves the policy — method attribute, then controller attribute, then the
framework default, clamped by the bounds. Override it where the shape depends on who is asking.

```csharp
public sealed class ReportsController : EntityController<Report>
{
    protected override PaginationPolicy GetPaginationPolicy()
    {
        var policy = base.GetPaginationPolicy();
        return User.IsInRole("analyst")
            ? policy with { Mode = PaginationMode.Optional, MaxSize = 1000 }
            : policy;
    }
}
```

`PaginationPolicy` is a record, so `with` keeps the resolved bounds and changes only what this
decision owns. A policy built from scratch discards the deployment's ceiling.

To shape the query rather than the policy, override `BuildOptions()`, which returns the parsed
`QueryOptions` before the query runs.

```csharp
protected override QueryOptions BuildOptions()
{
    var options = base.BuildOptions();
    if (!User.IsInRole("analyst"))
    {
        options.PageSize = Math.Min(options.PageSize, 25);
    }

    return options;
}
```

`BuildOptions()` runs before the query, so a value set here is what the store is asked for — not a
filter applied to results that already came back.

## Client parameters

```http
GET /api/products?page=2&pageSize=50
GET /api/products?page=1&size=50          # size is accepted, and is what Link headers carry
GET /api/products?sort=category,-price
GET /api/products?all=true                # honoured under Optional, ignored under Required
```

A `page` below `1` is treated as `1`; a negative `page` answers `400`. A page size below `1` falls
back to `DefaultSize`.

## Related

- [Web reference](index.md) — the endpoint surface these parameters belong to, including `?filter=`,
  which narrows the set before it is counted and paged
