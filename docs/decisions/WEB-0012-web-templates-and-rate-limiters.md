---
id: WEB-0012
slug: WEB-0012-web-templates-and-rate-limiters
domain: WEB
status: Accepted
date: 2025-08-16
---

# 0012: Web launch templates and rate limiters
# 0012 — Web launch templates and rate limiter registration

Context
- We introduced simple launch templates for ASP.NET Core apps to reduce boilerplate: `AsWebApi()`, `WithExceptionHandler()`, and `WithRateLimit()`.
- The rate limiter DI extensions live in Microsoft.AspNetCore.RateLimiting. Bringing that reference into a class library complicates restore and adds an unnecessary dependency for apps that don’t need it.

Decision
- `AsWebApi()` enables controllers, static files, secure headers, and ProblemDetails. It does not register a rate limiter.
- `WithExceptionHandler()` and `WithRateLimit()` only toggle middleware wiring via a shared `WebPipelineOptions` that the startup filter reads.
- Apps are responsible for registering a limiter via `AddRateLimiter(...)` when they opt in to rate limiting.

Consequences
- Sora.Web has no hard dependency on the rate limiting package; apps control it.
- Enabling `WithRateLimit()` without registering a limiter is safe; `UseRateLimiter()` becomes a no-op.
- Documentation and samples show registering a simple fixed-window limiter in the app.
