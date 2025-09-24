# Sylin.Koan.Web

ASP.NET Core integration for Koan: controller-first routing, health endpoints, well-known endpoints, and observability bootstrap.

- Target framework: net9.0
- License: Apache-2.0

## Capabilities

- MVC controller-first routing (attribute-routed)
- Health endpoints and OpenAPI wiring (opt-in)
- Transformers for payload shaping (see WEB-0035)

## Install (minimal setup)

```powershell
dotnet add package Sylin.Koan.Web
```

## Usage - quick examples

- Expose REST endpoints via controllers, not inline endpoints:

```csharp
public sealed class ItemsController : EntityController<Item, Guid>
{
	// GET /api/items
	[HttpGet("api/items")]
	public async Task<IActionResult> GetAll(CancellationToken ct)
		=> Ok(await Item.FirstPage(50, ct));
}
```

- Use transformers for response shaping; see decision WEB-0035.

See TECHNICAL.md for contracts, options, and integration details.

## Customization

- Configuration and advanced usage are documented in [`TECHNICAL.md`](./TECHNICAL.md).

## References

- Web API conventions: `/docs/api/web-http-api.md`
- Decision: `/docs/decisions/WEB-0035-entitycontroller-transformers.md`
- Engineering guardrails: `/docs/engineering/index.md`
