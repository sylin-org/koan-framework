# Web Reference

Policies and patterns for HTTP endpoints in Sora.

## Controllers only
- Attribute-routed MVC controllers are required.
- Do not declare endpoints inline (no MapGet/MapPost/etc.).

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

## Skeletons

```csharp
[ApiController]
[Route("api/[controller]")]
public class MoviesController : EntityController<Movie>
{
}
```
