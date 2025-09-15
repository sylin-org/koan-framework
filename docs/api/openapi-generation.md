# OpenAPI generation for Koan-based APIs

We recommend Swashbuckle for ASP.NET Core. Koan ships an optional helper package: `Koan.Web.Swagger` (NuGet ID: `Sylin.Koan.Web.Swagger`).

Enablement policy
- Non-Production (Development/Staging): Swagger is enabled by default when Koan.Web.Swagger is referenced.
- Production: Swagger is OFF by default unless
    - `Koan__Web__Swagger__Enabled=true` OR
    - `Koan:AllowMagicInProduction=true` (env: `Koan__AllowMagicInProduction=true`)

What Koan.Web.Swagger wires
- Registers Endpoints API Explorer and SwaggerGen.
- Loads XML docs automatically (when present) to enrich API descriptions.
- Adds common response headers to docs: `Koan-Trace-Id`, `Koan-InMemory-Paging`.
- Exposes UI at `/swagger` by default (prefix configurable).

Quick start (Koan.Web.Swagger)
1) Add a project reference to `Koan.Web.Swagger` (or install package `Sylin.Koan.Web.Swagger`).
2) In `Program.cs`:
    - `using Koan.Web.Swagger;`
    - `app.UseKoanSwagger();`
3) In Production, set `Koan__Web__Swagger__Enabled=true` or `Koan:AllowMagicInProduction=true` to expose the UI.

Documenting Koan-specific conventions
- Include response headers like `Koan-Trace-Id` and `Koan-InMemory-Paging` where relevant.
- Document well-known endpoints under `/.well-known/Koan/*`.
- For query endpoints that accept JSON filters, describe the filter scheme or link to docs/15-entity-filtering-and-query.md.

References
// removed tutorial reference
- docs/15-entity-filtering-and-query.md
- docs/ddd/04-cqrs-and-eventing-in-Koan.md
