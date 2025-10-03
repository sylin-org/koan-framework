---
type: REF
domain: web
title: "Entity Endpoint Service"
audience: [developers, architects, ai-agents]
last_updated: 2025-09-24
framework_version: "v0.2.18+"
status: current
---

# Entity Endpoint Service

**Document Type**: Reference Guide
**Target Audience**: Developers, Architects, AI Agents
**Last Updated**: 2025-09-24
**Framework Version**: v0.2.18+

---

## Why This Exists

`EntityController<TEntity, TKey>` used to own every concern: HTTP query parsing, hook scheduling, repository routing, pagination math, relationship expansion, and response metadata. That tight coupling made cross-protocol reuse (GraphQL, MCP, CLI, agentic flows) impossible without duplicating orchestration logic. `EntityEndpointService` extracts that behaviour into a protocol-neutral layer so adapters focus on request translation, not business rules.

Key benefits:
- Single source of truth for entity CRUD orchestration.
- Protocol reuse across REST, GraphQL, MCP, jobs, and future adapters.
- Deterministic hook sequencing regardless of caller.
- Focused testing and easier telemetry because metadata lives on the shared context.

---

## Core Building Blocks

`Koan.Web.Endpoints` provides the shared abstractions:
- `IEntityEndpointService<TEntity, TKey>` ? async CRUD surface (`GetCollectionAsync`, `UpsertAsync`, `DeleteAsync`, etc.).
- `EntityEndpointService<TEntity, TKey>` ? default implementation consumed by HTTP controllers.
- `IEntityHookPipeline<TEntity>` ? injectable pipeline that orders `IAuthorizeHook`, `IRequestOptionsHook`, `ICollectionHook`, `IModelHook`, and `IEmitHook` implementations.
- `EntityEndpointOptions` ? central defaults for pagination, shapes, relationship expansion, and view negotiation.
- `EntityRequestContextBuilder` ? constructs `EntityRequestContext` instances for HTTP and non-HTTP callers.
- `EntityRequestContext` ? captures scoped services, `QueryOptions`, cancellation token, optional `HttpContext`, caller `ClaimsPrincipal`, headers, and warnings.
- Request contracts (`EntityCollectionRequest`, `EntityUpsertRequest<T>`, etc.) ? strongly typed inputs for the service.
- Result envelopes (`EntityCollectionResult<T>`, `EntityModelResult<T>`, `EntityEndpointResult`) ? hold payloads, short-circuit data, headers, and warnings.
- `IEntityEndpointDescriptorProvider` ? exposes operation metadata (verbs, dataset support, allowed shapes, default view) to other protocols.
- `HookContext<TEntity>` plus the default `HookRunner<TEntity>` (used by `DefaultEntityHookPipeline`).

Everything lives in `Koan.Web.Endpoints`, so other packages can depend on the abstractions without pulling in MVC.

---

## How HTTP Controllers Use It

`EntityController<TEntity, TKey>` is now a thin adapter:

1. Build `QueryOptions` from the incoming request, honouring `EntityEndpointOptions` and any `KoanDataBehaviorAttribute` overrides.
2. Use `EntityRequestContextBuilder` to create an `EntityRequestContext` containing DI services, cancellation token, `HttpContext`, and the current user.
3. Populate the appropriate request contract (`EntityCollectionRequest`, `EntityUpsertRequest<TEntity>`, etc.).
4. Call into the injected `IEntityEndpointService` and let the hook pipeline run.
5. Copy response headers/warnings and resolve short circuits (action or payload) before returning an `IActionResult`.

Example ? add custom filters before the shared service runs:

```csharp
public class OrdersController : EntityController<Order>
{
    protected override QueryOptions BuildOptions()
    {
        var opts = base.BuildOptions();
        opts.Filters.Add(new FilterSpec("status", "!=", "archived"));
        return opts;
    }
}
```

---

## Reusing the Service in Other Surfaces

```csharp
var options = new QueryOptions { Page = 1, PageSize = 25, Q = "status:open" };
var builder = scope.ServiceProvider.GetRequiredService<EntityRequestContextBuilder>();
var context = builder.Build(options, cancellationToken, httpContext: null, user: principal);

var request = new EntityCollectionRequest
{
    Context = context,
    FilterJson = jsonFilter, // optional
    Set = dataSetKey,
    IgnoreCase = true,
    Accept = "application/json; view=minimal"
};

var result = await endpointService.GetCollectionAsync(request);
var payload = result.Payload ?? result.Items;
```

Implementation checklist:
- Scope ? resolve `EntityRequestContextBuilder` (or provide your own factory) from the active scope.
- User ? supply the principal used for authorization; default to an empty principal if unauthenticated.
- HttpContext ? optional; only pass it when you need HTTP-specific hints like pagination defaults.
- Headers & warnings ? read `result.Headers` / `result.Warnings` and surface them via your protocol (GraphQL extensions, MCP diagnostics, CLI logs, etc.).
- Short circuits ? check `result.IsShortCircuited`. If `result.ShortCircuitResult` is null, use `result.ShortCircuitPayload` to propagate the hook-supplied payload.

---

## Hook Pipeline Order

The service resolves `IEntityHookPipeline<TEntity>` from DI (the default pipeline wraps `HookRunner<TEntity>`). Hooks execute in deterministic order:
1. Authorize hooks (`IAuthorizeHook<TEntity>`).
2. Request options hooks (`IRequestOptionsHook<TEntity>`).
3. Collection hooks (`ICollectionHook<TEntity>`).
4. Model hooks (`IModelHook<TEntity>`).
5. Emit hooks (`IEmitHook<TEntity>`).

`HookContext<TEntity>` exposes headers, warnings, user, and short-circuit helpers (either `IActionResult` or arbitrary payload). Non-HTTP callers should avoid relying on `HookContext.Http`.

---

## Request Shaping & Response Metadata

The service understands the same query language as `EntityController`:
- `q` ? free-text search via `IStringQueryRepository`.
- `filter` / `filterObj` ? JSON filter compiled into LINQ predicates when `ILinqQueryRepository` is available.
- `page` / `size` ? pagination. If the repository lacks native support, the service falls back to in-memory paging and emits `Koan-InMemory-Paging`, `X-Page`, `X-Page-Size`, `X-Total-Count`, and `X-Total-Pages` headers.
-  `sort` / `dir` ? converted into `QueryOptions.Sort`; the service applies ordering over the retrieved set before pagination so adapters see consistent results even when repositories do not implement sorting. 
- `view` / `Accept view=` ? negotiated into the `Koan-View` header.
- `shape` ? simplified responses (`map`, `dict`) when they appear in `EntityEndpointOptions.AllowedShapes`.
- `with=all` ? relationship expansion (when `EntityEndpointOptions.AllowRelationshipExpansion` is true).

Always propagate `result.Headers` and `result.Warnings` so clients see consistent metadata, regardless of protocol.

---

## Dataset & Capability Awareness

- The `Set` request field scopes operations inside a `DataSetContext`, preserving multitenant routing.
- `IQueryCapabilities` and `IWriteCapabilities` implementations on repositories populate `EntityRequestContext.Capabilities` and the `Koan-Write-Capabilities` header.

Implement these interfaces on custom repositories so downstream adapters receive accurate capability metadata.

---

## Configuration Defaults

Override `EntityEndpointOptions` just like other options:

```csharp
builder.Services.Configure<EntityEndpointOptions>(opts =>
{
    opts.DefaultPageSize = 50;
    opts.MaxPageSize = 250;
    opts.AllowedShapes = new List<string> { "map" }; // disable dict
    opts.AllowRelationshipExpansion = false;          // disallow with=all
    opts.DefaultView = "compact";
});
```

HTTP controllers automatically honour these defaults, and `IEntityEndpointDescriptorProvider` surfaces them to other protocols.

---

## Operation Descriptors

`IEntityEndpointDescriptorProvider` lets other surfaces discover entity behaviour:

```csharp
var provider = scope.ServiceProvider.GetRequiredService<IEntityEndpointDescriptorProvider>();
var descriptor = provider.Describe<Customer, string>();

foreach (var operation in descriptor.Operations)
{
    Console.WriteLine($"{operation.Kind} -> body? {operation.RequiresBody}, dataset? {operation.SupportsDatasetRouting}");
}

Console.WriteLine($"Allowed shapes: {string.Join(", ", descriptor.Metadata.AllowedShapes)}");
```

Use descriptors to keep GraphQL, MCP, CLI, and agentic adapters aligned with REST behaviour (operations, defaults, shapes) without reflection.

---

## Testing & Diagnostics

Because the orchestration lives outside MVC, you can test it directly:
1. Register in-memory fakes for `IDataService` and hook interfaces.
2. Build an `EntityRequestContext` with deterministic `QueryOptions` and a cancellation token.
3. Invoke the desired method and assert against the `EntityEndpointResult` payload, headers, warnings, and `ShortCircuitPayload`.

Recommended coverage:
- Pagination and header emission (including in-memory pagination headers).
- Hook short-circuit behaviour (both `IActionResult` and payload cases).
- Dataset routing (`Set` values) and capability headers.
- Shape/relationship transformations and bulk operations.

---

## Migration Notes

- **Existing REST endpoints** ? already flow through the service; keep overrides only for HTTP-specific customisations.
- **GraphQL** ? delegate to `IEntityEndpointService` instead of recreating repository logic to align hooks and metadata.
- **MCP / CLI / automation** ? construct `EntityRequestContext` from the session (user, dataset, cancellation) and reuse the service.
- **Custom controllers/recipes** ? migrate direct `Data<TEntity>.` calls to the service to inherit hooks, warnings, and capability headers.

---

**Last Validation**: 2025-09-24 by Framework Specialist
**Framework Version Tested**: v0.2.18+

