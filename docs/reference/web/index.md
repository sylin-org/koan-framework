---
type: REF
domain: web
title: "Web Pillar Reference"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: source-first
validation:
  date_last_tested: 2026-07-15
  status: reviewed
  scope: public controller, endpoint, hook, transformer, relationship, health, and facts source inventory
---

# Web Pillar Reference

## Contract

- **Input:** an ASP.NET Core application composed with `AddKoan()`, an Entity model, and an
  attribute-routed MVC controller.
- **Output:** conventional Entity CRUD/query endpoints backed by the shared
  `IEntityEndpointService<TEntity, TKey>` policy and execution seam.
- **Errors:** invalid filters/sorts/paging, authorization denial, adapter limitations, safety bounds,
  transformer failures, and relationship negotiation failures become explicit HTTP results.
- **Success:** the application declares business models and routes while Koan owns routine parsing,
  persistence orchestration, pagination metadata, hooks, shaping, and inspectability.

## Shortest supported shape

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>
{
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

var app = builder.Build();
await app.RunAsync();
```

The package reference expresses intent; `AddKoan()` compiles and activates referenced Koan modules. Application
code still owns its model, route, authorization declarations, and any business-specific actions.
Koan.Web maps controllers by default through its startup filter (`AutoMapControllers = true`); an
application only owns explicit pipeline mapping when it disables that default.

## EntityController behavior

`EntityController<TEntity, TKey>` provides collection, query, get-new, get-by-id, single/bulk upsert,
single/bulk/query/all delete, and patch actions. `EntityController<TEntity>` is its string-key alias.
See the HTTP API reference for the exact verbs, bodies, headers, and status codes.

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
public sealed class TodosController : EntityController<Todo>
{
}
```

- Collection requests accept `page`, `pageSize` (or legacy `size`), sort, filter, shape, set, and
  relationship options subject to endpoint policy.
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

- Base Entity operations use the shared authorization seam; declare standard
  `[Authorize]`/`[AllowAnonymous]` and Koan scope requirements on the entity or applicable surface.
  Do not add REST-only `CanRead`/`CanWrite` overrides.
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

## Maturity boundary

Koan is pre-1.0. The sources above support a concise MVC/Entity path, shared endpoint policies,
hooks, transforms, health, and facts. They do not provide blanket production certification, automatic
security-provider configuration, universal streaming, or unlimited relationship expansion. Validate
the adapters, authentication setup, safety limits, and topology used by each application.

## References

- [HTTP API](../../api/web-http-api.md)
- [Detailed Web HTTP reference](http-api.md)
- [WEB-0035 — EntityController transformers](../../decisions/WEB-0035-entitycontroller-transformers.md)
- [ARCH-0092 — Entity exposure surfaces](../../decisions/ARCH-0092-entity-exposure-surfaces.md)
- [ARCH-0112 — bounded relationship negotiation](../../decisions/ARCH-0112-bounded-relationship-negotiation.md)
- [DATA-0107 — provider-bounded Entity streams](../../decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Koan.Web source](../../../src/Koan.Web/)
