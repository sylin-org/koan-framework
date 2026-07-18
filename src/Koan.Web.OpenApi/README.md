# Sylin.Koan.Web.OpenApi

Publish a wire-faithful OpenAPI 3.1 document and a governed interactive UI from a Koan web application.

## Install

```powershell
dotnet add package Sylin.Koan.Web.OpenApi
```

The reference composes through the application's existing `AddKoan()` call. No `AddOpenApi`, `MapOpenApi`, Swagger,
or application-pipeline call is required.

## Smallest meaningful result

Run the application and inspect:

- `/openapi/v1.json` — enabled by the package reference;
- `/swagger` — enabled by default only in Development.

The document includes Koan application identity, Entity pagination/headers, negotiated transformer media types, and
the Newtonsoft names, ignored members, and string enums used by the actual REST wire.

## Deliberate configuration

```json
{
  "Koan": {
    "OpenApi": {
      "Enabled": true,
      "EnableUi": true,
      "RoutePattern": "/contracts/{documentName}.json",
      "UiRoute": "docs",
      "RequireAuthenticationOutsideDevelopment": true
    }
  }
}
```

Outside Development the UI is absent unless `EnableUi` is explicitly `true`. When enabled there, an unauthenticated
request receives `401` by default. Configure a default authentication scheme, or deliberately set
`RequireAuthenticationOutsideDevelopment` to `false` when an open UI is truly intended. `Enabled=false` removes both
the document and UI.

## Guarantees and boundaries

- One option family controls the document and UI; no legacy Swagger configuration or manual activation path exists.
- Startup reporting states the effective document route, UI route, enablement, and authentication posture.
- The OpenAPI schema describes Koan's REST wire; MCP-only exclusions do not silently change the REST contract.
- The document endpoint itself is not an authorization boundary. Disable it or apply host/network policy when the API
  contract must not be public.
- This package does not provide API versioning, client generation, multiple-document orchestration, OAuth UI setup, or
  XML-comment aggregation.

See [TECHNICAL.md](TECHNICAL.md) for startup ordering, schema fidelity, and security behavior.
