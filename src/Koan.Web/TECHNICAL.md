---
uid: reference.modules.Koan.web
title: Koan.Web - Technical Reference
description: Contracts, configuration, and architecture for Koan’s ASP.NET Core integration.
packages: [Sylin.Koan.Web]
source: src/Koan.Web/
last_updated: 2026-07-18
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
- `IWebContextContributor` and `WebContext` for ordered, scoped request-context decisions
- Transformers for payload shaping (see WEB-0035)
- `GET /.well-known/Koan/facts` projects `IKoanRuntimeFacts.Current` through `KoanFactJson`.

## Configuration

- Use typed Options for feature toggles (health, OpenAPI, transformers)
- Runtime facts are exposed automatically in Development. Set
  `Koan:Web:ExposeObservabilitySnapshot=true` for an intentional non-Development exposure.
- Avoid inline endpoints; keep routing in controllers
- `EnableStaticFiles` retains Koan's conventional static-file wiring, but middleware is skipped when
  ASP.NET exposes `NullFileProvider`; API-only applications therefore need no empty `wwwroot` folder.

## Usage guidance

- Implement controllers with attribute routing only
- For data access inside controllers, prefer first-class model statics:
  - `Item.FirstPage(...)`, `Item.Page(...)`, `Item.QueryStream(...)`

### Request context

- Register scoped `IWebContextContributor` implementations with standard DI. The Web module mounts one internal
  runner after authentication; applications do not add contributor middleware manually.
- Contributors run by `Order`. After each asynchronous contribution returns, the runner synchronously enters its
  pending principal/capability/filter scopes before invoking the next contributor. Later contributors can therefore
  depend on earlier validated identity or tenant context. All entered scopes wrap downstream execution and unwind in
  reverse order.
- `WebContext.Where<TEntity>(expression)` lowers through Data's normalized filter compiler and AND-composes with
  other predicates for that Entity type. One Web-owned `IReadFilterContributor` supplies the immutable current map
  to Data's canonical read fold; Data remains unaware of HTTP vocabulary.
- `Reject()` stops the request before endpoints with an existence-hiding response. A contributor must distinguish an
  inapplicable request from applicable-but-invalid evidence explicitly.
- Global Entity cache reads/writes are bypassed while a dynamic read predicate is active. Static model decoration is
  not required for cache safety.

## Edge cases and limits

- Large responses: prefer paging/streaming; set appropriate cache/timeout policies
- Auth/permissions: integrate with Koan.Web.Auth modules where needed
- Context predicates authorize reads only. Controller policies still own write decisions; raw storage and raw SQL do
  not pass through Entity read filters, and request predicates are not durable job context.

## Observability and security

- Integrate with logging/tracing; expose health endpoints when enabled
- Use transformers to control payload shapes and security concerns
- Runtime facts are redacted but still disclose module/decision names; retain an operational access
  boundary and do not cache the response.

## Design and composition

- Composes with Koan.Data modules via application model statics
- Keeps web concerns in controllers; avoids startup inline endpoints

### Startup ownership

- The generic-host binder owns the process-default `AppHost` binding for the host lifetime.
- `KoanWebStartupFilter` flow-scopes the Web application provider while Koan and downstream startup
  filters construct the pipeline. Startup contributors can therefore use ambient Entity operations
  without replacing a newer attached process owner.
- Exiting pipeline construction restores the prior ambient host. This ownership boundary does not
  change middleware, contributor, endpoint, or startup-filter ordering.

## Deployment and topology

- Library packaged for ASP.NET Core apps; no standalone runtime

## Performance guidance

- Favor pagination and projection to reduce payload size

## Compatibility and migrations

- Target frameworks: net10.0

## References

- [Web API](https://github.com/sylin-org/Koan-framework/blob/main/docs/api/web-http-api.md)
- [WEB-0035 — EntityController transformers](https://github.com/sylin-org/Koan-framework/blob/main/docs/decisions/WEB-0035-entitycontroller-transformers.md)
- [Engineering guardrails](https://github.com/sylin-org/Koan-framework/blob/main/docs/engineering/index.md)
- [Runtime facts](https://github.com/sylin-org/Koan-framework/blob/main/docs/engineering/runtime-facts.md)

## Relationship expansion safety

- `?with=all` preserves each related type's request-option/visibility predicates and delegates child
  execution to Data.Core's `IRelationshipQueryExecutor`. Collections issue one child query per edge,
  not one query per root.
- `EntityEndpointOptions.RelationshipMaxResults` defaults to 200 rows per edge across the request.
  `RelationshipFallbackMaxCandidates` defaults to null, so scan-backed adapters fail closed unless an
  application explicitly chooses a finite candidate budget.
- Unsupported or implicit scans return 422. Candidate/result limit overflow returns 413. The response
  carries stable reason, relationship, provider, correction, and limit fields; no entity keys or
  provider configuration are included.
- MCP uses `IEntityEndpointService` and therefore receives the same authorization, limits, errors, and
  runtime facts. These are direct-edge guarantees, not recursive graph-depth or parent-batching claims.
