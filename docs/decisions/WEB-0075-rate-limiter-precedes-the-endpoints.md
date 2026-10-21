---
id: WEB-0075
slug: rate-limiter-precedes-the-endpoints
domain: Web
status: Accepted
date: 2026-10-21
title: The rate limiter precedes the endpoints Koan maps
related:
  - WEB-0012
  - WEB-0069
---

# WEB-0075: The rate limiter precedes the endpoints Koan maps

## Context

WEB-0012 presents `services.AddRateLimiter(...)` plus `services.WithRateLimit()` as how an application turns on rate
limiting. `KoanWebStartupFilter` called `app.UseRateLimiter()` after `UseEndpoints`, which maps every controller and
every `IKoanEndpointContributor` route (MCP among them). The endpoint middleware executes a matched endpoint and does
not call the next middleware, so the limiter saw only requests that matched no endpoint. Entity CRUD, `POST /query` and
`/mcp` were never limited, with no signal (ledger NK-028).

## Decision

**`WithRateLimit()` places `UseRateLimiter()` after routing, authentication, authorization and the `AfterAuthorization`
stage, and before the endpoints.** Global limiters and endpoint metadata (`RequireRateLimiting`, `[EnableRateLimiting]`)
then apply to every route Koan maps. When `AutoMapControllers` is off the limiter stays at the end of Koan's middleware,
ahead of the endpoints the application maps itself. Enabling it without `AddRateLimiter` is still a no-op.

## Compatibility

- Applications that called `WithRateLimit()` now have their configured limiter applied to controller, entity and MCP
  routes. Requests over the limit receive the limiter's rejection status (503 unless `RejectionStatusCode` is set).
- The limiter now runs after authentication, so a partitioning function can read `HttpContext.User`.
- No public signature changes.

## Evidence

- `RateLimiterPlacementSpec` (Koan.Web.WellKnown.Tests): with a one-permit window, the second request to a controller
  route and to a contributed endpoint returns 429 under `WithRateLimit()` and 200 without it. Both opted-in cells
  returned 200 before this change.
