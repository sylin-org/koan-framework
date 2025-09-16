---
id: ARCH-0011
slug: ARCH-0011-logging-and-headers-layering
domain: ARCH
status: Accepted
date: 2025-08-16
---

# 0011: Layering for logging and response headers

Context
- Logging setup lived inside the S1 sample app and varied per application.
- Minimal secure response headers were injected in the app pipeline.
- We want clear layering: framework provides sensible defaults; apps define policy.

Decision
- Move default logging to Koan.Core (configured by `AddKoanCore`):
  - SimpleConsole provider, single-line with timestamps.
  - Category filters: Microsoft/System at Warning, Hosting.Lifetime at Information, Koan at Information.
- Centralize secure headers in Koan.Web via startup filter controlled by `KoanWebOptions`:
  - Adds `X-Content-Type-Options=nosniff`, `X-Frame-Options=DENY`, `Referrer-Policy=no-referrer`.
  - Optional `Content-Security-Policy` value via `KoanWebOptions.ContentSecurityPolicy`.
- Keep application-level policy in apps:
  - ProblemDetails registration and exception pipeline.
  - Rate limiting policies.
  - Any additional logging providers/levels via configuration.

Consequences
- Apps get sensible logs without extra code; appsettings can override providers/levels.
- Secure headers are applied consistently across Koan.Web apps; CSP is opt-in/configurable.
- Samples (S1) are simpler and focus on policies (ProblemDetails, rate limiting) rather than framework plumbing.

Alternatives considered
- Keep logging and headers per application: more boilerplate, inconsistent defaults.
- Separate security package for headers: overkill for current scope; can be revisited later.
- Use third-party header libraries now: optional; the built-in layer is minimal and non-invasive.

See also
- 0010: Meta packages (Koan, Koan.App)
- 0009: Unify on `IEntity<TKey>`
