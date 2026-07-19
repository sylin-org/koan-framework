---
name: koan-web
description: Instant REST for Entity<T> — EntityController<T> CRUD + POST /query, custom [HttpGet]/[HttpPost] routes, override Upsert/Get/Delete, [Authorize] policies/roles, IEntityTransformer content shaping, the entity capability authorization model ([Access] per-action gate + EntityAccess<T> row Constrain + can:[] projection on Entity<T>, cross-surface via the IAuthorize seam — SEC-0004/ARCH-0092), IEntityEndpointService escape hatch
pillar: web
card: docs/reference/cards/web.md
status: current
last_validated: 2026-06-18
---

# Koan Web

## Trigger this skill when you see

- A controller deriving `EntityController<Todo>` / `EntityController<Todo, TKey>` (`Koan.Web.Controllers`)
- REST surface talk — `GET /api/todos`, `POST /api/todos/query`, pagination/filter/sort query strings, capability headers
- Custom endpoints alongside CRUD — `[HttpGet("…")]` / `[HttpPost("{id}/…")]` actions on the same controller
- Overriding a generated verb — `override Upsert(...)`, `override GetById(...)`, `override Delete(...)`
- `[Authorize]` / `[Authorize(Policy = "…")]` / `[Authorize(Roles = "…")]` on a controller (REST-only), or the **entity capability model** (`[Access(read:, write:, remove:)]` gate + `EntityAccess<T>` row `Constrain` + `can:[]` projection on `Entity<T>`, enforced on REST + MCP by the `IAuthorize` seam — SEC-0004 / ARCH-0092)
- Response shaping — `IEntityTransformer<TEntity, TShape>`, `AddEntityTransformer<...>(contentTypes)`, content negotiation, CSV/HAL output
- `[Pagination(...)]`, `[KoanDataBehavior(...)]`, `[RequireCapability(...)]` on a controller, `IEntityEndpointService<TEntity, TKey>`
- References to `Koan.Web`, "REST API", "web endpoint", "controller", "content negotiation", read-path visibility (WEB-0068)

## Core principle

**`EntityController<T>` is the whole REST surface.** Derive one controller from `EntityController<Todo>` and Reference = Intent auto-maps GET (list + by-id) / POST / PUT / PATCH / DELETE plus `POST /query`, with pagination, filtering, sort, capability headers, and request hooks — no manual MVC wiring. Add ordinary `[HttpGet("…")]` actions for custom routes, `override Upsert(...)` to intercept a generated verb, `[Authorize]` on the controller (REST authz) — or authorize the entity surface with **gate · constrain · project** (`[Access(...)]` per-action gate + `EntityAccess<T>` row `Constrain` + the `can:[]` projection on `Entity<T>`) for authz enforced on REST **and** MCP through the unified seam ([SEC-0004](../../../docs/decisions/SEC-0004-capability-authorization-gate-constrain-project.md) / [ARCH-0092](../../../docs/decisions/ARCH-0092-entity-exposure-surfaces.md)) — and an `IEntityTransformer<TEntity, TShape>` for alternate output shapes. Read-path visibility predicates are enforced **per surface** ([WEB-0068](../../../docs/decisions/WEB-0068-query-options-predicates.md)).

<!-- validate -->
```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Transformers;

public sealed class Order : Entity<Order>
{
    public string CustomerEmail { get; set; } = "";
    public decimal Total { get; set; }
    public bool Shipped { get; set; }
}

[Route("api/orders")]                                   // one line = full REST surface
[Pagination(DefaultSize = 20, MaxSize = 200)]           // per-controller page sizing
[Authorize]                                             // authn for every generated + custom action
public sealed class OrdersController : EntityController<Order>
{
    // ARCH-0092 removed the Can* gates. Override an action to disable/guard a single verb on THIS controller.
    // For authz enforced on EVERY surface (REST + MCP), declare the per-action [Access] gate on the ENTITY +
    // an EntityAccess<Order> realization for row scope (SEC-0004 gate·constrain·project — see the auth skill).
    public override Task<IActionResult> Delete(string id, CancellationToken ct)
        => Task.FromResult<IActionResult>(Forbid());    // disable DELETE on this controller

    [HttpGet("mine")]                                   // custom route beside the generated CRUD
    public Task<IReadOnlyList<Order>> Mine(CancellationToken ct)
    {
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
        return Order.Query(o => o.CustomerEmail == email, ct);   // IReadOnlyList<Order>
    }

    [HttpPost("")]                                      // intercept the generated POST
    [Authorize(Policy = "CanCreateOrders")]
    public override Task<IActionResult> Upsert([FromBody] Order model, CancellationToken ct)
    {
        model.CustomerEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
        return base.Upsert(model, ct);                  // reuse hooks/paging/visibility machinery
    }
}

// Alternate output shape via Accept negotiation (Terminal-stage transformer, registered Singleton)
public sealed class OrderCsvTransformer : IEntityTransformer<Order, string>
{
    public IReadOnlyList<string> AcceptContentTypes => new[] { "text/csv" };

    public Task<object> Transform(Order model, HttpContext httpContext)
        => Task.FromResult<object>($"email,total,shipped\n{model.CustomerEmail},{model.Total},{model.Shipped}\n");

    public Task<object> TransformMany(IEnumerable<Order> models, HttpContext httpContext)
        => Task.FromResult<object>(string.Join("", models.Select(m => $"{m.CustomerEmail},{m.Total}\n")));

    public Task<Order> Parse(Stream body, string contentType, HttpContext httpContext)
        => Task.FromResult(new Order());

    public Task<IReadOnlyList<Order>> ParseMany(Stream body, string contentType, HttpContext httpContext)
        => Task.FromResult<IReadOnlyList<Order>>(new List<Order>());
}

public static class WebRegistration
{
    public static void AddOrderShaping(IServiceCollection services)
        => services.AddEntityTransformer<Order, string, OrderCsvTransformer>("text/csv");  // Singleton
}
```

## Reference = Intent activation

| Add this | Effect |
|---|---|
| `Koan.Web` + `EntityController<Order>` (`Koan.Web.Controllers`) | Auto-maps GET (list/by-id) / POST / PUT / PATCH / DELETE + `POST /query` — no MVC wiring. |
| `[Route("api/orders")]` on the derived controller | The route prefix; the verbs hang off it (`POST /api/orders/query`, `PATCH /api/orders/{id}`, …). |
| `[HttpGet("…")]` / `[HttpPost("{id}/…")]` actions on the same controller | Custom business routes live beside the generated CRUD. |
| `AddEntityTransformer<TEntity, TShape, TTransformer>(contentTypes)` | Registers a **Singleton** Terminal-stage transformer, selected by `Accept` negotiation. |
| `AddEntityEnricher<TEntity, TEnricher>()` | Registers a Pipeline-stage enricher (multiple per type, run in priority order). |

## ≤5 attributes you'll reach for

| Attribute | What it does |
|---|---|
| `[Pagination(DefaultSize, MaxSize, Mode)]` | Per-controller page sizing + `PaginationMode.On` / `Optional` / `Off`. |
| `[RequireCapability("action")]` | Method-level capability-authorization gate (SEC-0002 `IAuthorize` seam, `Koan.Web.Extensions`). |
| `[Authorize]` / `[Authorize(Policy = "…")]` / `[Authorize(Roles = "…")]` | Standard ASP.NET authn/authz (see the [auth card](../../../docs/reference/cards/auth.md)). |

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `IPayloadTransformer<T>` / `TransformAsync(entity)` | None exist — it's `IEntityTransformer<TEntity, TShape>` with `Transform(model, httpContext)` / `Parse` / `AcceptContentTypes`. |
| `services.AddScoped<IPayloadTransformer<T>, …>()` | `services.AddEntityTransformer<TEntity, TShape, TTransformer>(contentTypes)` — registered **Singleton**, not Scoped. |
| `override Task<IActionResult> Post([FromBody] T entity)` | The generated POST handler is `override Upsert([FromBody] T model, CancellationToken ct)` — there is no `Post`. |
| `Task<List<Order>>` as a custom-action return | `Task<IReadOnlyList<Order>>` — entity statics (`Order.Query(...)`) return `IReadOnlyList<T>`, not `List<T>`. |
| `Order.Query().Where(o => ...)` (parameterless then filter) | `Order.Query(o => ..., ct)` — the predicate is the argument (canonical verb `Query`). |
| Hand-written `[HttpGet]`/`[HttpPost]` CRUD actions calling a repository | Derive `EntityController<Order>` — the CRUD surface is generated; only add genuinely custom routes. |
| `if (!isAdmin) return Forbid();` scattered in each action | `[Authorize(Policy=...)]` on the controller, or the per-action `[Access(remove: "is:admin")]` gate on `Entity<T>` (+ `EntityAccess<T>` for row scope) — declarative, enforced on every surface (SEC-0004 / ARCH-0092). |
| Hand-writing a `Where(o => o.OwnerId == me)` row filter in every read/update/delete action | Declare `Owner` once on an `EntityAccess<T>` realization and `Constrain(q, action)` — the rule IS the filter (collection + 404 + mass-delete bound + create-stamp all derive from it; SEC-0004). |
| `app.UseMvc()` / `services.AddControllers()` hand-wiring for Koan controllers | Referencing `Koan.Web` maps them automatically (Reference = Intent). |

## Escape hatches

- **Custom shape the generic controller doesn't give**: drop to a plain `ControllerBase` action and call entity statics directly, or inject `IEntityEndpointService<TEntity, TKey>` (`Koan.Web.Endpoints`) — the service `EntityController` delegates to — to reuse the hooks / paging / visibility machinery in a hand-written endpoint.
- **Intercept a generated verb**: `override Upsert(...)` / `override GetById(...)` / `override Delete(...)`, mutate then `return base.<Verb>(...)` (one way to guard a single verb on this controller now the `Can*` virtuals are gone). Cross-surface authz: declare the per-action `[Access(...)]` gate on the **entity** + an `EntityAccess<T>` realization for row scope (SEC-0004 gate · constrain · project — see [koan-auth](../koan-auth/SKILL.md)).
- **Per-request data behavior from HTTP**: `app.UseKoanCacheControl()` (opt-in) maps `Cache-Control: no-cache/no-store` and `X-Koan-Cache` onto `EntityContext.CacheBehavior` — see [koan-caching](../koan-caching/SKILL.md).
- **Read-path visibility (WEB-0068)**: predicates are enforced **per read surface** (REST + MCP via `EntityEndpointService`; GraphQL separately). When tightening a read filter, sweep every surface — a get-by-id path is a separate gate from list/query.

## See also

- [Reference card: web.md](../../../docs/reference/cards/web.md) — one-screen pillar map
- [Web pillar reference](../../../docs/reference/web/index.md) — full controller / negotiation detail
- [Web reference](../../../docs/reference/web/index.md) · [HTTP API conventions](../../../docs/reference/web/http-api.md)
- [TaskGraph](../../../samples/fundamentals/TaskGraph/README.md) — `EntityController<Todo>` CRUD + custom relationship routes
- [WEB-0068 — read-path visibility predicates](../../../docs/decisions/WEB-0068-query-options-predicates.md)
