# Web Reference

Policies and patterns for HTTP endpoints in Koan.

## Contract
- Attribute-routed MVC controllers only; no inline endpoints in startup.
- Transformers shape payloads consistently for EntityController.
- Emit headers: `Koan-Trace-Id`; `Koan-InMemory-Paging: true` when fallback pagination happened.

## Example

```http
POST /api/movies/query HTTP/1.1
Content-Type: application/json

{
	"filter": { "Genres": "*Drama*" },
	"page": 1,
	"size": 20
}

// Response headers
Koan-Trace-Id: 7e9226b2...
Koan-InMemory-Paging: true
X-Total-Count: 125
X-Page: 1
X-Page-Size: 20
X-Total-Pages: 7
```

## Transformers and payload shaping
- Use transformers with EntityController for consistent shapes.
- Reference: decisions/WEB-0035-entitycontroller-transformers.md

## Defaults and security
- Secure response headers by default; CSP is opt-in.
- ProblemDetails for error payloads.
- Reference: decisions/ARCH-0011-logging-and-headers-layering.md

## GraphQL
- GraphQL module and controller integration.
- Naming and discovery rules.
- References: decisions/WEB-0041-graphql-module-and-controller.md, decisions/WEB-0042-graphql-naming-and-discovery.md

See also: `api/well-known-endpoints.md` for `/.well-known/Koan/*` routes.

## Edge cases
- Invalid filters/payloads: prefer ProblemDetails with validation details; never return 200 on invalid inputs.
- Large queries/timeouts: enforce paging caps and timeouts; set `Koan-InMemory-Paging` when fallback occurs; document limits.
- Auth/permissions: apply auth scopes before filters; avoid leaking fields across tenants.
- Transformer errors: map exceptions to ProblemDetails and include correlation via `Koan-Trace-Id`.
- Inline endpoints: disallowed; routes must live in MVC controllers for discoverability and tests.

Note: Well-known header names and route prefixes are documented in `docs/api/web-http-api.md`. Prefer linking to that page over repeating literals.
