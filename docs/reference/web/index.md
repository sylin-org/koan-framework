---
type: REF
domain: web
title: "Web Pillar Reference"
audience: [developers, architects, ai-agents]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current
validation: 2025-01-17
---

# Web Pillar Reference

**Document Type**: REF
**Target Audience**: Developers, Architects, AI Agents
**Last Updated**: 2025-01-17
**Framework Version**: v0.2.18+

---

## Overview

HTTP layer with controllers, authentication, and API generation.

**Package**: `Koan.Web`

## Entity Controllers

Full REST API from entity inheritance:

```csharp
[Route("api/[controller]")]
public class ProductsController : EntityController<Product>
{
    // Automatic endpoints:
    // GET /api/products
    // GET /api/products/{id}
    // POST /api/products
    // PUT /api/products/{id}
    // DELETE /api/products/{id}

    // Add custom endpoints
    [HttpGet("featured")]
    public Task<Product[]> GetFeatured() => Product.Featured();

    [HttpGet("search")]
    public Task<Product[]> Search(string q) => Product.Search(q);
}
```

## Entity Endpoint Service
The shared orchestration layer introduced in v0.2.18+ extracts pagination, dataset routing, hook execution, and response shaping into `IEntityEndpointService`. REST controllers already delegate to it; other adapters (GraphQL, MCP, jobs, agents) can reuse the same behaviour by constructing an `EntityRequestContext` and calling the service.
Key pieces:
- `IEntityHookPipeline` so all surfaces share the same hook sequencing.
- `EntityEndpointOptions` and `EntityRequestContextBuilder` for consistent defaults and context creation.
- `IEntityEndpointDescriptorProvider` for operation metadata consumed by MCP, CLI, or GraphQL tooling.
See [Entity Endpoint Service](entity-endpoint-service.md) for setup, examples, and configuration tips.

## Custom Controllers

```csharp
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var order = new Order
        {
            CustomerEmail = request.Email,
            Total = request.Items.Sum(i => i.Price)
        };

        await order.Save();
        return Ok(order);
    }
}
```

## Authentication

### Quick Setup

```bash
dotnet add package Koan.Web.Auth.Google
```

```csharp
// Program.cs - that's it
builder.Services.AddKoan();
```

### Endpoints

- `GET /.well-known/auth/providers` - Available providers
- `POST /auth/challenge/{provider}` - Start auth flow
- `POST /auth/callback` - Handle provider callback
- `POST /auth/logout` - Sign out

### Usage

```csharp
[Authorize]
public class OrdersController : EntityController<Order>
{
    [HttpPost]
    [Authorize(Policy = "CanCreateOrders")]
    public override Task<IActionResult> Post([FromBody] Order entity)
    {
        return base.Post(entity);
    }
}
```

## Payload Transformers

Shape request/response data:

```csharp
public class ProductTransformer : IPayloadTransformer<Product>
{
    public Task<object> TransformResponse(Product product, TransformContext context)
    {
        return Task.FromResult<object>(new
        {
            product.Id,
            product.Name,
            product.Price,
            Url = $"/products/{product.Id}",
            InStock = product.Quantity > 0
        });
    }
}
```

## Configuration

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
          },
          "microsoft": {
            "ClientId": "{MS_CLIENT_ID}",
            "ClientSecret": "{MS_CLIENT_SECRET}"
          }
        }
      }
    }
  }
}
```

Environment variables:
```bash
export Koan__Web__Auth__Providers__google__ClientId=your-client-id
export Koan__Web__Auth__Providers__google__ClientSecret=your-secret
```

## Security

Built-in security headers and HTTPS redirection in production.

```csharp
// Custom authorization policies
[Authorize(Policy = "AdminOnly")]
[Authorize(Roles = "Manager,Admin")]
```

## Error Handling

Global error handling with structured responses:

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
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "Validation failed",
                details = ex.Errors
            }));
        }
    }
}
```

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
