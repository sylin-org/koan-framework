---
type: GUIDE
domain: web
title: "API Delivery Playbook"
audience: [developers, architects, ai-agents]
last_updated: 2025-09-28
framework_version: v0.6.2
status: current
validation:
  date_last_tested: 2025-09-28
  status: verified
  scope: docs/guides/building-apis.md
---

# API Delivery Playbook

## Contract

- **Inputs**: Working knowledge of ASP.NET Core controllers, Koan entities, and the Data pillar.
- **Outputs**: An intentional HTTP surface where CRUD, custom routes, payload shaping, and auth policies are unified.
- **Error Modes**: Duplicate business logic in controllers, inconsistent pagination, unsecured capability endpoints, or stream endpoints without cancellation.
- **Success Criteria**: Entity controllers expose CRUD + focused routes, transformers keep responses consistent, validation & auth policies live close to the action, and observability is wired in.

### Edge Cases

- **Cross-pillar coupling** – controllers should only call entity statics or shared services; avoid direct data store access.
- **Long-running jobs** – do not serve heavy processing inside HTTP; hand off to Flow pipelines or background workers.
- **Error surfaces** – translate lifecycle cancellations into HTTP 4xx with descriptive payloads.
- **Auth negotiation** – ensure `/auth/providers` returns your configured providers before relying on challenge endpoints.

---

## Guided Workflow

This playbook mirrors the canonical [Web Pillar Reference](../reference/web/index.md). Follow each stage when building or reviewing an API surface.

1. **Bootstrap** – Add `Koan.Web` and call `services.AddKoan()`.
2. **Expose CRUD** – Start with `EntityController<T>` and confirm standard operations.
3. **Add intent endpoints** – Layer custom routes for business cases (search, analytics, state transitions).
4. **Shape payloads** – Attach payload transformers for response consistency.
5. **Secure** – Apply policies, register providers, and guard capability-sensitive endpoints.
6. **Validate & Observe** – Add request validation, tracing, and structured logging.

---

## 1. Bootstrap & Health

- Install `Koan.Web` alongside your data adapters.
- Call `AddKoan()` in `Program.cs`; health endpoints light up automatically.
- Verify `/api/health`, `/api/health/live`, and `/api/health/ready` before layering features.

🔎 Reference: [Quick start](../reference/web/index.md#quick-start-two-file-api)

---

## 2. Extend Entity Controllers

- Keep CRUD while adding business routes with attribute routing.
- Use static helpers on entities for queries and flows; avoid injecting repositories.
- Return IActionResults for richer responses (pagination metadata, status codes).

#### Example – File Upload Endpoint

```csharp
[Route("api/[controller]")]
public class ProductsController : EntityController<Product>
{
    [HttpPost("{id}/image")]
    public async Task<IActionResult> UploadImage(string id, IFormFile file, CancellationToken ct)
    {
        var product = await Product.ById(id, ct);
        if (product is null) return NotFound();

        var image = await ProductImage.UploadAsync(file, ct);
        product.ImageId = image.Id;
        await product.Save();

        return Ok(new { imageUrl = $"/media/{image.Id}" });
    }
}
```

🔎 Reference: [Entity controllers in depth](../reference/web/index.md#entity-controllers-in-depth)

---

## 3. Compose Custom Controllers

- Switch to `ControllerBase` when orchestrating multiple aggregates or external services.
- Pull data via entity statics and Flow pipelines; avoid duplicating query logic.
- Embrace `IActionResult` to express success/failure paths cleanly.

🔎 Reference: [Custom controllers & composition](../reference/web/index.md#custom-controllers--composition)

---

## 4. Shape Payloads

- Implement `IPayloadTransformer<T>` for canonical API responses.
- Use transformers to add hypermedia links, computed fields, or redactions.
- Register transformers once; the entity endpoint service reuses them across surfaces (REST, GraphQL, MCP).

🔎 Reference: [Payload transformers](../reference/web/index.md#payload-transformers)

---

## 5. Secure the Surface

- Enable auth providers (OIDC, OAuth, SAML) before exposing restricted routes.
- Gate domain operations with policies mapped to roles or claims.
- Log challenge/response flows for observability and support.

#### Example – Policy-Protected Endpoints

```csharp
[Route("api/[controller]")]
[Authorize]
public class OrdersController : EntityController<Order>
{
    [HttpGet]
    public Task<Order[]> GetMyOrders(CancellationToken ct)
    {
        var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        return Order.ForCustomer(userEmail!, ct);
    }

    [HttpPost]
    [Authorize(Policy = "CanCreateOrders")]
    public override Task<IActionResult> Post([FromBody] Order entity)
    {
        entity.CustomerEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
        return base.Post(entity);
    }
}
```

🔎 Reference: [Authentication & authorization](../reference/web/index.md#authentication--authorization)

---

## 6. Validate & Observe

- Apply data annotations or FluentValidation for input models.
- Wrap controllers with middleware to capture validation errors, correlation IDs, and telemetry.
- Emit structured logs with request/response context.

#### Example – Error Handling Pattern

```csharp
[HttpPost("{id}/refund")]
public async Task<IActionResult> Refund(string id, [FromBody] RefundRequest request, CancellationToken ct)
{
    try
    {
        var order = await Order.ById(id, ct);
        if (order is null) return NotFound();

        await order.ProcessRefund(request.Amount, request.Reason, ct);
        return Ok();
    }
    catch (InvalidOperationException ex)
    {
        return BadRequest(new { error = ex.Message });
    }
    catch (InsufficientFundsException ex)
    {
        return StatusCode(StatusCodes.Status409Conflict, new { error = ex.Message });
    }
}
```

🔎 Reference: [Error handling & observability](../reference/web/index.md#error-handling--observability)

---

## 7. Configuration Checklist

- Configure CORS for any SPA or remote agent clients.
- Register auth provider secrets in environment variables or secret stores.
- Document capability toggles (pagination, moderation) for your team.

🔎 Reference: [Configuration & environment](../reference/web/index.md#configuration--environment)

---

## Review Checklist

- [ ] CRUD endpoints verified via `EntityController<T>`.
- [ ] Custom routes use entity statics or the entity endpoint service.
- [ ] Payload transformers return consistent shapes.
- [ ] Auth policies documented and enforced.
- [ ] Validation and error responses standardized.
- [ ] Logs and traces include correlation identifiers.

---

## Where to Go Next

- Generate OpenAPI documents for development environments.
- Add streaming endpoints backed by Flow pipelines for background processing.
- Explore GraphQL or MCP surfaces that reuse the same entity endpoint service.

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+
