# Sylin.Sora.Web

ASP.NET Core integration for Sora: controller-first routing, health endpoints, well-known endpoints, and observability bootstrap.

- Target framework: net9.0
- License: Apache-2.0

## Install

```powershell
dotnet add package Sylin.Sora.Web
```

## Notes
- Prefer MVC controllers with attribute routing
- Health endpoints and OpenAPI wiring are opt-in via config

## Links
- Web API: https://github.com/sylin-labs/sora-framework/blob/dev/docs/api/web-http-api.md
- Decisions: https://github.com/sylin-labs/sora-framework/blob/dev/docs/decisions/WEB-0035-entitycontroller-transformers.md
