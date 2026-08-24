---
type: REFERENCE
domain: web
title: "Web"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-23
  status: passed
  scope: docs/capabilities/web.md - route table verified against leaf targets
---

# Web

Koan projects the Entity model rather than creating a second HTTP model. The route exists because
the Entity exists - and authorization, tenant scope, and adapter limits travel through the same
pipeline, so the API never becomes a second authority.

## Route by need

| The request says | Fetch |
|---|---|
| "expose this Entity over HTTP" | [Entity HTTP API](web/entity-api.md) |
| "we need an OpenAPI description / Swagger UI" | [OpenAPI README](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.Web.OpenApi/README.md) |
| "push live updates to the browser" | [SSE README](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.Web.Sse/README.md) |
| "give it a frontend / let people click it" | [frontend topology](web/frontend-topology.md) |

## Standing constraints

- Projection reuses the model: authorization, tenant scope, and lifecycle travel through the
  Entity pipeline, so the API never becomes a second authority.
- The route exists because the Entity exists - deleting the Entity deletes the surface, which is
  the contract, not a bug.

## Do not, at this level

- Do not hand-map CRUD endpoints that `EntityController<T>` already projects.
- Do not create a parallel DTO/HTTP model "for the API" without a named need.

For the one-screen maturity view, see [Web in the capability map](../reference/capability-map.md#web).
