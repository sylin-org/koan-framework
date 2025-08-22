# OpenAPI generation for Sora-based APIs

We recommend Swashbuckle for ASP.NET Core. Sora ships an optional helper package: `Sora.Web.Swagger` (NuGet ID: `Sylin.Sora.Web.Swagger`).

Enablement policy
- Non-Production (Development/Staging): Swagger is enabled by default when Sora.Web.Swagger is referenced.
- Production: Swagger is OFF by default unless
    - `Sora__Web__Swagger__Enabled=true` OR
    - `Sora:AllowMagicInProduction=true` (env: `Sora__AllowMagicInProduction=true`)

What Sora.Web.Swagger wires
- Registers Endpoints API Explorer and SwaggerGen.
- Loads XML docs automatically (when present) to enrich API descriptions.
- Adds common response headers to docs: `Sora-Trace-Id`, `Sora-InMemory-Paging`.
- Exposes UI at `/swagger` by default (prefix configurable).

Quick start (Sora.Web.Swagger)
1) Add a project reference to `Sora.Web.Swagger` (or install package `Sylin.Sora.Web.Swagger`).
2) In `Program.cs`:
    - `using Sora.Web.Swagger;`
    - `app.UseSoraSwagger();`
3) In Production, set `Sora__Web__Swagger__Enabled=true` or `Sora:AllowMagicInProduction=true` to expose the UI.

Documenting Sora-specific conventions
- Include response headers like `Sora-Trace-Id` and `Sora-InMemory-Paging` where relevant.
- Document well-known endpoints under `/.well-known/sora/*`.
- For query endpoints that accept JSON filters, describe the filter scheme or link to docs/15-entity-filtering-and-query.md.

References
// removed tutorial reference
- docs/15-entity-filtering-and-query.md
- docs/ddd/04-cqrs-and-eventing-in-sora.md
