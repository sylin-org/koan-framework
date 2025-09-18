---
uid: reference.modules.Koan.web
title: Koan.Web - Technical Reference
description: Contracts, configuration, and architecture for Koan’s ASP.NET Core integration.
since: 0.2.x
packages: [Sylin.Koan.Web]
source: src/Koan.Web/
---

## Contract

- Inputs/Outputs
  - ASP.NET Core MVC controllers, attribute routing, action results
  - EntityController<T,TKey> for entity-backed REST
- Options
  - Health endpoints/OpenAPI wiring (opt-in)
  - Conventions for well-known endpoints
- Error modes
  - Standard ASP.NET Core pipeline behaviors; 4xx/5xx as appropriate
- Success criteria
  - Controller-first routing; discoverable, testable endpoints

## Key types and surfaces

- `EntityController<TEntity, TKey>`
- Transformers for payload shaping (see WEB-0035)

## Configuration

- Use typed Options for feature toggles (health, OpenAPI, transformers)
- Avoid inline endpoints; keep routing in controllers

## Usage guidance

- Implement controllers with attribute routing only
- For data access inside controllers, prefer first-class model statics:
  - `Item.FirstPage(...)`, `Item.Page(...)`, `Item.QueryStream(...)`

## Edge cases and limits

- Large responses: prefer paging/streaming; set appropriate cache/timeout policies
- Auth/permissions: integrate with Koan.Web.Auth modules where needed

## Observability and security

- Integrate with logging/tracing; expose health endpoints when enabled
- Use transformers to control payload shapes and security concerns

## Design and composition

- Composes with Koan.Data modules via application model statics
- Keeps web concerns in controllers; avoids startup inline endpoints

## Deployment and topology

- Library packaged for ASP.NET Core apps; no standalone runtime

## Performance guidance

- Favor pagination and projection to reduce payload size

## Compatibility and migrations

- Target frameworks: net9.0

## References

- Web API: `/docs/api/web-http-api.md`
- Decision - transformers: `/docs/decisions/WEB-0035-entitycontroller-transformers.md`
- Engineering guardrails: `/docs/engineering/index.md`
