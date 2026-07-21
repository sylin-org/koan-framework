# Sylin.Koan.Web.OpenApi technical contract

## Responsibility

`OpenApiModule` owns the complete package lifecycle: typed options, Microsoft OpenAPI 3.1 registration, document and
operation transformers, Newtonsoft schema mirroring, one startup filter, and provenance. There is no second Swagger
bootstrap, option model, startup filter, or public `Add`/`Use` path.

## Runtime

The module binds `KoanOpenApiOptions` from `Koan:OpenApi`. `KoanOpenApiStartupFilter` resolves the options once per host,
adds the optional Swagger UI middleware, lets the application/Koan web pipeline compose, and maps the OpenAPI document
through ASP.NET endpoint routing.

The document defaults enabled because the package reference declares projection intent. The UI defaults enabled only
when `IHostEnvironment.IsDevelopment()` is true. Explicit document disablement dominates UI enablement.

Outside Development, an enabled UI installs a path-scoped authentication gate by default. It accepts an already
authenticated principal or invokes the default ASP.NET authentication scheme. No principal or no usable default scheme
returns `401` with a correction; it never falls through to static UI content. Applications can explicitly turn that gate
off through the same option family.

## Schema fidelity

`NewtonsoftSchemaMirror` projects Koan REST's Newtonsoft naming, ignored-member, and string-enum conventions onto the
System.Text.Json metadata consumed by `Microsoft.AspNetCore.OpenApi`. Document and operation transformers then add
application identity, pagination headers, Koan headers, and registered transformer media types.

## Deliberate non-guarantees

- the document endpoint is not authenticated automatically;
- no API versioning or multi-document registry;
- no generated clients;
- no Swagger OAuth-client configuration; and
- no XML documentation aggregation.
