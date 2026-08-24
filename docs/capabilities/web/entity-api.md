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
