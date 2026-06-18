---
type: REF
domain: web
title: "Web — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-06-18
framework_version: v0.17.0
validation:
  date_last_tested: 2026-06-18
  status: verified
  scope: docs/reference/cards/web.md
---

# Web — pillar map

> One-screen map of the Web pillar. Full detail: [web/index.md](../web/index.md).

**What it does** — Instant REST for any `Entity<T>`. Derive one controller from `EntityController<T>` and you get GET (list + by-id) / POST / PUT / PATCH / DELETE plus `POST /query`, all with pagination, filtering, sort, capability headers, and request hooks. Reference = Intent: referencing `Koan.Web` auto-maps your controllers — no manual MVC wiring. Read-path visibility predicates are enforced per surface (WEB-0068).

## The one canonical pattern

```csharp
[Route("api/todos")]
public sealed class TodosController : EntityController<Todo> { }
```

That one line wires the full REST surface:

```text
GET    /api/todos?filter=…&sort=-createdAt&page=1&size=20   list (paged, capability headers)
GET    /api/todos/{id}            POST /api/todos            PUT  /api/todos/{id}
POST   /api/todos/query           PATCH /api/todos/{id}      DELETE /api/todos/{id}
```

Override `CanRead` / `CanWrite` / `CanRemove` to gate operations; add ordinary `[HttpGet("…")]` actions on the same controller for custom routes.

## ≤5 attributes you'll use

| Attribute | What it does |
|---|---|
| `[Pagination(DefaultSize, MaxSize, Mode)]` | Per-controller page sizing + mode (`On` / `Optional` / `Off`). |
| `[RequireCapability(...)]` | Gate an endpoint on a data capability (ARCH-0084). |
| `[KoanDataBehavior(...)]` | Declarative per-controller data behavior. |
| `[Authorize]` / `[Authorize(Roles="…")]` | Standard ASP.NET authn/authz (see the [auth card](auth.md)). |

## The escape hatch

Need a shape the generic controller doesn't give? Drop to a plain `ControllerBase` action and call the entity statics directly, or inject `IEntityEndpointService<TEntity, TKey>` (the service `EntityController` delegates to) to reuse the hooks/paging machinery in a hand-written endpoint:

```csharp
[HttpGet("api/todos/open")]
public Task<IReadOnlyList<Todo>> Open() => Todo.Query(t => !t.Done);
```

## The sample that shows it

[`samples/S1.Web`](../../../samples/S1.Web/README.md) — `EntityController<Todo>` REST plus the relationship system over a minimal web UI.
