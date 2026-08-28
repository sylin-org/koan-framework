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

Query-style verbs (`POST /query`, `GET /new`, bulk forms) exist beside these; see the surface
reference. When an external contract needs an exotic verb, subclass and delegate to the same
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
