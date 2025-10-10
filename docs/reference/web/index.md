---
type: REF
domain: web
title: "Web Pillar Reference"
audience: [developers, architects, ai-agents]
last_updated: 2025-09-28
framework_version: v0.6.3
status: current
validation:
  date_last_tested: 2025-09-28
  status: verified
  scope: docs/reference/web/index.md
---

# Web Pillar Reference

## Contract

- **Inputs**: ASP.NET Core application with `services.AddKoan()`, Koan data models, and familiarity with controller conventions.
- **Outputs**: Production-ready HTTP surfaces built on `EntityController<T>`, custom controllers, payload transformers, and consistent auth/pagination policies.
- **Error Modes**: Missing capability flags (soft-delete, moderation) for entities, unhandled hook cancellations, streaming endpoints without pagination, or authentication providers not registered.
- **Success Criteria**: REST endpoints stand up with minimal code, custom routes reuse shared pipeline services, payloads remain consistent across surfaces, and auth/pagination behavior is predictable.

### Edge Cases

- **Entity caps** – honor feature flags from the Data pillar before surfacing moderation or soft-delete endpoints.
- **Transformer order** – payload transformers run post-hook; avoid mutating entities directly.
- **Streaming endpoints** – always accept `CancellationToken` in custom actions to avoid thread starvation.
- **Auth bootstrapping** – provider metadata must be configured before `AddKoan()` runs in hosted scenarios.
- **Cache headers** – set explicit caching when serving static projections; defaults are conservative.

---

## Pillar Overview

Koan.Web layers opinionated controller patterns on top of ASP.NET Core:

- Automatic CRUD endpoints through `EntityController<T>`
- Shared orchestration via `IEntityEndpointService`
- Attribute routing with MVC controllers only (no inline `MapGet`/`MapPost`)
- Payload transformers for response shaping
- Out-of-the-box authentication discovery endpoints and health probes

Install `Koan.Web` alongside the Data pillar for end-to-end CRUD in minutes.

---

## Quick Start: Two-File API

```csharp
// Models/Product.cs
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
}

// Controllers/ProductsController.cs
[Route("api/[controller]")]
public class ProductsController : EntityController<Product> { }
```

This delivers:

- `GET /api/products` – list
- `GET /api/products/{id}` – details
- `POST /api/products` – create
- `PUT /api/products/{id}` – update
- `DELETE /api/products/{id}` – delete

Health endpoints (`/api/health`, `/api/health/live`, `/api/health/ready`) come from Koan.Core.

---

## Entity Controllers in Depth

Extend `EntityController<T>` to add intent-specific routes while keeping the built-in CRUD surface.

```csharp
[Route("api/[controller]")]
public class ProductsController : EntityController<Product>
{
    [HttpGet("featured")]
    public Task<Product[]> GetFeatured() => Product.Featured();

    [HttpPost("{id}/activate")]
    public async Task<IActionResult> Activate(string id)
    {
  var product = await Product.Get(id);
        if (product is null) return NotFound();

        product.IsActive = true;
        await product.Save();
        return Ok();
    }
}
```

### Pagination & Streaming

- Prefer `FirstPage`/`Page` when exposing list endpoints to clients.
- For background jobs, stream data with `QueryStream` and wrap the controller action in `IAsyncEnumerable`.
- Reference: [Pagination attribute](./pagination-attribute.md) for reusable policy annotations.

---

## Custom Controllers & Composition

When you need custom orchestration or cross-entity projections, inherit from `ControllerBase` and call entity statics.

```csharp
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    [HttpGet("analytics/revenue")]
    public async Task<IActionResult> GetRevenue([FromQuery] int days = 30)
    {
        var orders = await Order.Query()
            .Where(o => o.Created > DateTimeOffset.UtcNow.AddDays(-days))
            .ToArrayAsync();

        var revenue = orders.Sum(o => o.Total);
        return Ok(new { revenue, orderCount = orders.Length });
    }
}
```

Controllers, hosted services, GraphQL resolvers, and MCP endpoints all share behavior via `IEntityEndpointService`. Reuse it to process domain requests consistently outside MVC.

---

## Payload Transformers

Transformers shape responses without mutating the entity. They run after lifecycle hooks and before serialization.

```csharp
public class ProductTransformer : IPayloadTransformer<Product>
{
    public Task<object> TransformResponse(Product product, TransformContext context)
        => Task.FromResult<object>(new
        {
            product.Id,
            product.Name,
            product.Price,
            Url = $"/products/{product.Id}",
            InStock = product.Quantity > 0
        });
}
```

Attach transformers via configuration or attribute decoration depending on the surface. See [EntityController Transformers](../../decisions/WEB-0035-entitycontroller-transformers.md).

---

## Authentication & Authorization

### Provider Setup

```bash
dotnet add package Koan.Web.Auth.Connector.Google

// Program.cs
builder.Services.AddKoan();
```

Endpoints provided out of the box:

- `GET /.well-known/auth/providers`
- `POST /auth/challenge/{provider}`
- `POST /auth/callback`
- `POST /auth/logout`

### Policy Enforcement

```csharp
[Authorize]
public class OrdersController : EntityController<Order>
{
    [HttpPost]
    [Authorize(Policy = "CanCreateOrders")]
    public override Task<IActionResult> Post([FromBody] Order entity) => base.Post(entity);
}
```

Register custom claims transformations and fallback policies through Koan’s auth capability modules. Reference ADR [WEB-0047](../../decisions/WEB-0047-capability-authorization-fallback-and-defaults.md).

---

## Error Handling & Observability

- Use ASP.NET Core middleware for global error shaping.
- Combine with Koan’s tracing so downstream adapters capture request context.

```csharp
public class GlobalExceptionMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "validation_failed",
                details = ex.Errors
            });
        }
    }
}
```

Add structured logging via `ILogger` and correlate request IDs; Koan bundles default enrichers.

---

## Configuration & Environment

```json
{
  "Koan": {
    "Web": {
      "CorsOrigins": ["http://localhost:3000"],
      "Auth": {
        "Providers": {
          "google": {
            "ClientId": "{GOOGLE_CLIENT_ID}",
            "ClientSecret": "{GOOGLE_CLIENT_SECRET}"
          }
        }
      }
    }
  }
}
```

Environment overrides:

```bash
export Koan__Web__Auth__Providers__google__ClientId=your-client-id
export Koan__Web__Auth__Providers__google__ClientSecret=your-secret
```

---

## Related Reading

- [Pagination attribute reference](./pagination-attribute.md)
- [Entity Endpoint Service](./entity-endpoint-service.md)
- [AI Pillar Reference](../ai/index.md) for chat/embedding endpoints
- [Data Pillar Reference](../data/index.md) for entity patterns shared with controllers

## GraphQL

Auto-generated schema from entities:

```bash
dotnet add package Koan.Web.GraphQL
```

Schema automatically includes all `EntityController<T>` types.

Query example:

```graphql
query {
  products(where: { category: "Electronics" }) {
    id
    name
    price
  }
}
```

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+

