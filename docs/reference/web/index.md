---
type: REFERENCE
domain: web
title: "Expose Entities through HTTP"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-18
framework_version: v1.0.0
validation:
  date_last_tested: 2026-07-18
  status: tested
  scope: controller/endpoint surfaces plus ordered request-context contribution and read projection
---

# Expose Entities through HTTP

Use Koan Web when the same Entity model needs a conventional HTTP API with governed CRUD, query,
paging, hooks, and inspectable correction paths.

An Entity model and an attribute-routed controller are the whole application side. Koan supplies the
CRUD and query endpoints behind them, and each one runs through the same
`IEntityEndpointService<TEntity, TKey>` — the seam that owns authorization, hooks, data access,
relationship expansion, and emission.

One seam owning those decisions is what makes a failure an HTTP result rather than a surprise. An
invalid filter, sort, or page answers `400`; a relationship expansion no adapter can serve fails
closed with `422`; a response past the safety cap answers `413`.

## Shortest supported shape

```csharp
using Koan.Data.Core.Model;      // Entity<T>
using Koan.Web.Controllers;      // EntityController<T>
using Microsoft.AspNetCore.Mvc;  // Route
using Koan.Core;                 // AddKoan()

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

var app = builder.Build();
await app.RunAsync();
```

The package reference expresses intent; `AddKoan()` compiles and activates referenced Koan modules. Application
code still owns its model, route, authorization declarations, and any business-specific actions.
Koan.Web maps controllers by default through its startup filter (`AutoMapControllers = true`); an
application only owns explicit pipeline mapping when it disables that default.

## Optional projection references

- `Sylin.Koan.Web.Extensions` adds `[RestEntity]` terse CRUD exposure and explicit audit and moderation
  controller realizations. It keeps generic controller declarations host-owned; package presence does not expose the
  richer capability controllers automatically.
- `Sylin.Koan.Web.OpenApi` publishes `/openapi/v1.json` from the existing `AddKoan()` composition. Its interactive UI
  defaults to `/swagger` in Development only and fails closed for unauthenticated callers when explicitly enabled in
  another environment.
- `Sylin.Koan.Web.Sse` lets a controller return `Sse.Stream(asyncValues)`. One `SseResult` handles typed JSON, raw text,
  and explicit envelopes for both MVC and framework transports; replay and heartbeat are not implied.

See the package-owned contracts for [Web Extensions](../../../src/Koan.Web.Extensions/README.md),
[OpenAPI](../../../src/Koan.Web.OpenApi/README.md), and [SSE](../../../src/Koan.Web.Sse/README.md). Each composes
through the same `AddKoan()` the application already calls.

## EntityController behavior

`EntityController<TEntity>` is the string-key alias for `EntityController<TEntity, TKey>`. Inheriting
from it and giving it a route is the whole declaration; these endpoints follow, relative to that
route.

| Route | Purpose |
| --- | --- |
| `GET /` | the collection, paged and filtered |
| `POST /query` | the same read with the filter in the body |
| `GET /new` | an unsaved instance carrying the model's defaults |
| `GET /{id}` | one entity |
| `POST /` | create or replace one |
| `POST /bulk` | create or replace many |
| `PATCH /{id}` | apply a JSON Patch document |
| `DELETE /{id}` | remove one |
| `DELETE /bulk` | remove the listed ids |
| `DELETE /?q=` | remove what the query matches |
| `DELETE /all` | remove every entity in the set |

Each is `virtual`. Override one to add business behavior around it, and call `base` to keep the
governed pipeline underneath.

The controller parses request syntax and translates HTTP. `IEntityEndpointService` owns the shared
authorization, hooks, data access, relationship, and emission pipeline so other Entity surfaces do
not need to duplicate those decisions.

## Pagination and queries

```csharp
[Route("api/todos")]
[Pagination(
    Mode = PaginationMode.On,
    DefaultSize = 50,
    MaxSize = 200,
    IncludeCount = true,
    DefaultSort = "-createdAt")]
public sealed class TodosController : EntityController<Todo>;
```

- Collection requests accept `page`, `pageSize` (`size` is accepted as an alias), sort, filter,
  shape, set, and relationship options subject to endpoint policy.

`filter` is URL-encoded JSON and `q` is a separate free-text slot:

```bash
curl --get   --data-urlencode 'filter={"status":"Pending"}'   http://localhost:5000/api/todos
```

Koan parses that into one filter tree. Each adapter receives only the nodes it declares it can
execute, and Koan evaluates the remainder before sorting and pagination — so the HTTP contract
returns the same result across query-capable adapters without claiming they all push the same work
down, or at the same cost. A malformed filter, an unknown field, or unsupported input returns
`400 Bad Request`; none of them is treated as an unfiltered request.
- When count metadata is enabled, collection responses include `X-Total-Count`; paged responses can
  also include RFC-style `Link` navigation headers.
- `POST /query` accepts the provider-agnostic JSON filter shape. It is not an `IQueryable` endpoint.
- `FirstPage`/`Page` are materialized Data APIs. `EntityController` does not promise an
  `IAsyncEnumerable` HTTP response merely because the selected adapter can stream internally.

For custom business actions, use first-class model APIs such as `Todo.Query(...)` and
`Todo.FirstPage(...)`. Use `Todo.AllStream(...)` or `Todo.QueryStream(...)` for background
consumer-paced work only when the elected adapter advertises `ProviderBoundedPaging`. SQLite,
PostgreSQL, SQL Server, CockroachDB, MongoDB, and Couchbase qualify today; InMemory, JSON, and Redis
reject before query/yield.

## Request-context contributors

Use one scoped `IWebContextContributor` when request evidence establishes business context that several downstream
surfaces must share. Koan invokes contributors automatically after authentication and before endpoints. A contributor
reads the standard `HttpContext`, validates evidence against server-side authority, and uses `WebContext` to contribute
a principal, a capability scope, typed Entity predicates, or rejection.

```csharp
public sealed class GalleryContext : IWebContextContributor
{
    public int Order => 200;

    public async ValueTask ContributeAsync(WebContext context)
    {
        var eventId = context.HttpContext.Request.Query["event"].ToString();
        if (!await Grants.Allows(context.SubjectId, eventId))
        {
            context.Reject();
            return;
        }

        context.Where<Photo>(photo => photo.EventId == eventId);
    }
}
```

The query value selects the scope to validate; it never grants access by itself. Contributors execute by `Order`, and
each contributor's accepted state is entered before the next contributor runs. Multiple predicates AND-compose and
flow through Data's existing read-filter fold, so raw Entity/key/Vector and Entity-backed Media reads inherit them.
Dynamic Entity cache access bypasses global entries while a predicate is active.

This is request-lifetime read context. Standard authorization policies still own writes. Raw storage and raw SQL do
not pass through Entity read filters, and request predicates are not serialized into durable jobs; those boundaries
must establish or re-resolve their own application authority.

## Extension seams

- `IModelHook<TEntity>` — before/after fetch, save, delete, and patch.
- `ICollectionHook<TEntity>` — before/after collection fetch.
- `IRequestOptionsHook<TEntity>` — adjust parsed query options.
- `IEmitHook<TEntity>` — replace or transform emitted model/collection payloads.
- `IEntityEnricher<TEntity>` — ordered same-shape output enrichment.
- `IEntityTransformer<TEntity, TShape>` — content-negotiated terminal input/output transformation.

Hooks are ordered application policy. Keep storage rules in the Data/domain layer and transport-only
translation in Web.

## Authorization and relationships

- Base Entity operations use the shared authorization seam. Declare standard
  `[Authorize]`/`[AllowAnonymous]` and Koan scope requirements on the entity or applicable surface,
  and the same decision holds wherever that entity is read — REST and MCP alike.
- `?access=true` opts a REST collection into the per-row capability sidecar when configured.
- `?with=...` expands declared direct relationships through the governed relationship executor.
  Native or resident execution is accepted by default; bounded fallback requires an explicit finite
  policy. Unsupported scans fail closed (422), and exceeded safety limits return 413.
- This contract covers direct edges. It does not promise arbitrary recursive graph traversal.

## Operator-facing behavior

- `GET /health/live` reports process liveness without dependency checks.
- `GET /health/ready` reports aggregated dependency readiness and returns 503 when a critical
  component is unhealthy.
- `GET /.well-known/Koan/facts` returns the host's current runtime explanation envelope.
- Startup reporting and runtime facts explain discovered modules and important selections; package
  presence alone is not proof that an optional adapter capability was elected.

## Where this surface stops

Koan.Web owns the Entity path over HTTP: the endpoints above, the shared policy behind them, hooks,
transforms, health, and facts. Three things stay with the application by design.

- **Which authentication provider, configured how.** Koan enforces the decision; it does not pick one.
- **What its own data may return at once.** The safety bounds have defaults, not knowledge of your rows.
- **Which adapters and topology it deploys on.** Streaming and relationship expansion are bounded by
  what the elected adapter advertises, and the boundary is reported at startup rather than assumed.

## References

- [PATCH normalization](../../api/patch-normalization.md)
- [Pagination](pagination.md)
- [OpenAPI package](../../../src/Koan.Web.OpenApi/README.md)
- [WEB-0035 — EntityController transformers](../../decisions/WEB-0035-entitycontroller-transformers.md)
- [ARCH-0092 — Entity exposure surfaces](../../decisions/ARCH-0092-entity-exposure-surfaces.md)
- [ARCH-0112 — bounded relationship negotiation](../../decisions/ARCH-0112-bounded-relationship-negotiation.md)
- [DATA-0107 — provider-bounded Entity streams](../../decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Koan.Web source](../../../src/Koan.Web/)
