---
type: REFERENCE
domain: web
title: "Entity HTTP API"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-24
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-24
  status: passed
  scope: docs/capabilities/web/entity-api.md - cold-executed via store-and-expose on SQLite: CRUD,
    filtered query, page/pageSize windows with X-Total-Count, removal to 404
---

# Entity HTTP API

Expose the Entity already used by application code as conventional CRUD, query, paging, and removal
endpoints with no repository or endpoint mapping layer.

## You need

| Piece | Package | Note |
|---|---|---|
| Entity HTTP conventions | `Sylin.Koan.Web` | inherit `EntityController<T>` and give it an attribute route |
| Business persistence | one Entity store | the controller uses ordinary Entity semantics underneath |
| Richer projections (optional) | `Sylin.Koan.Web.Extensions` | shaping, audit, and moderation remain explicit opt-ins |

## The constraint box

> **The constraint:** Every Entity endpoint must keep the shared authorization, hook, data,
> relationship, and emission pipeline underneath it. The selected data adapter still bounds filters,
> paging, streaming, and relationship expansion; HTTP cannot manufacture a capability the store did
> not declare.

## The verb map

Every verb is a face of the same governed write/read pipeline — none of them bypasses validation,
`[Access]`, hooks, stamps, audit, or facts:

| Verb | Meaning | Notes |
|---|---|---|
| `POST /api/todos` | **Upsert** — create or update through the governed pipeline | the canonical write; id absent/new = create |
| `PUT /api/todos/{id}` | **Replace by id** — route id wins; a body id that disagrees fails `409 web.put.idMismatch` | added by [WEB-0073](../../decisions/WEB-0073-entity-controller-governed-put.md); the body's id is normalized at the JSON level (the entity constructor assigns an id at bind time) |
| `PATCH /api/todos/{id}` | **Delta** — content type picks the dialect: `application/merge-patch+json` (RFC 7396, `null` clears), `application/json-patch+json` (RFC 6902 ops), or plain `application/json` (partial; `null` sets null) | one action dispatches by media type |
| `DELETE /api/todos/{id}` | Remove | |

Each write is one outcome: from its prior read to its last after-hook it is a hold where the store
offers one (SQLite), so a hook's rows commit with the row or neither does, and a hook's thrown
`Refusal` or a lifecycle `Cancel` answers a problem document with its word, not a 500
([WEB-0086](../../decisions/WEB-0086-an-entity-write-commits-as-one-and-refuses-like-a-command.md)).

A controller marked `[Declared]` serves only the operations it overrides, each at its own route, with `POST` that only creates;
`[Writes(...)]` names the members a write writes, `[Input<T>]` what it takes beside them, `KeyOf` maps an address to a row, and a
controller that implements a hook runs as that hook for its own requests, each hook reading the operation it serves as `ctx.Operation`
([WEB-0089](../../decisions/WEB-0089-a-hook-knows-its-operation-and-a-refused-form-reads-its-page-again.md)). A query word an
operation does not take is refused naming it, except that a page read by a sender that names no JSON, a link another site tagged, is
sent on (`302`) without it
([WEB-0090](../../decisions/WEB-0090-a-page-reached-with-words-it-does-not-take-is-sent-on-without-them.md)). Every controller refuses a caller-chosen `?set=` partition
unless it declares `[Partitions(...)]`
([WEB-0087](../../decisions/WEB-0087-a-declared-entity-controller-serves-what-it-declares.md)). A declared collection continues from a
sealed `after`, a page of `size` rows, in the order its `[Pagination]` declares, and counts nothing
([DATA-0170](../../decisions/DATA-0170-a-list-continues-from-one-sealed-keyset-cursor.md)).

A declared controller's writes also take a page's form, which names `PATCH` or `DELETE` in a `_method` field, and answer through
`Answers.Written`: a form is sent on (`303`) to its safe `return` path or the row's address (`AddressOf`), JSON reads the row with `201`
or `200` and its `Location`, and a refused form answers its problem with what it `sent`, or the page its controller hands from
`RefusedPage`, at the refusal's status. A form is read within the limits of the
routes at its address, and a body that cannot bind is refused naming its field. A verb beside the entity answers the same way
with `this.Written(...)`
([WEB-0088](../../decisions/WEB-0088-a-declared-entity-write-takes-a-form-and-answers-once.md)).

Query-style verbs (`POST /query`, `GET /new`, bulk forms) exist beside these; see the surface
reference. The `?filter=` payload speaks the closed query filter vocabulary —
[the query filter vocabulary](../data/entities.md#the-query-filter-vocabulary) is the operator table. When an external contract needs an exotic verb, subclass and delegate to the same
endpoint service — never to the data layer directly.

## The whole application declaration

| Application concern | Expression |
|---|---|
| Business state | `Todo : Entity<Todo>` |
| HTTP route | `[Route("api/todos")]` on `TodosController : EntityController<Todo>` |
| Persistence | the selected Data connector reference |
| Composition | the application's one existing `AddKoan()` call |

Override a virtual controller action only for business behavior around the governed path, and call
the base implementation to retain that path.

## Leaves

- **Pasteable build:** [store and expose](../../recipes/store-and-expose.md)
- **Tested contract:** [Web reference](../../reference/web/index.md)
- **Package contract:** optional projection mechanics:
  [Web Extensions README](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.Web.Extensions/README.md)

Authentication identifies the caller; [access rules](../trust/access-rules.md) decide what that
caller may do with this Entity.
