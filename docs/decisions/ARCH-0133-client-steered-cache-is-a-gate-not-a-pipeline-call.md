---
id: ARCH-0133
slug: client-steered-cache-is-a-gate-not-a-pipeline-call
domain: Architecture
status: Accepted
date: 2026-08-22
title: Client-steered cache is a gate decision, not a pipeline call
related:
  - ARCH-0075
  - ARCH-0128
  - WEB-0069
---

# ARCH-0133: Client-steered cache is a gate decision, not a pipeline call

## Context

`KoanCacheControlMiddleware` maps a caller's `Cache-Control: no-cache` / `no-store` — and the
framework's own `X-Koan-Cache: refresh|bypass|readonly` — onto `EntityContext.WithCacheBehavior` for
the request. [ARCH-0075](ARCH-0075-koan-cache-pillar.md) made it opt-in through
`app.UseKoanCacheControl()`, and the reference documentation showed that call.

Opt-in was the right instinct. Honouring these headers hands every caller a lever on server-side
caching: a client that sends `no-store` forces an expensive query to miss, on every request it likes.
That is a real exposure and it should not arrive by accident.

The mechanism was the wrong half. An `app.Use…()` call in `Program.cs` is exactly the manual
framework registration Koan's application grammar excludes, and this one was worse than most: the
extension's own documentation told applications to place it *early, before controllers*, so the
ambient scope exists by the time Entity calls run. That is a correctness requirement pushed onto
every consumer — the failure mode [WEB-0069](WEB-0069-web-pipeline-contributors.md) introduced
`IKoanWebPipelineContributor` to eliminate, after ordering-dependent registration broke the
SEC-0001 dev identity.

Two seams already existed and neither was used. Middleware placement belongs to a pipeline
contributor at a named stage. A convenience that is safe in development and risky in production
belongs to `KoanEnv.Gate` under [ARCH-0128](ARCH-0128-environment-posture-is-a-named-decision.md),
whose whole purpose is that this decision not be re-derived at each call site.

## Decision

**The reference composes the middleware. The gate decides whether it honours anything. The
application writes no pipeline call.**

- `CacheControlPipelineContributor` mounts the middleware at `KoanWebPipelineStage.BeforeRouting`,
  so the request's cache behaviour is established before any Entity call — a framework-owned
  ordering guarantee rather than a consumer instruction.
- The posture is a `KoanMagic`: capability *client-steered cache behaviour*, risk *any caller can
  send `Cache-Control: no-store` and force this application's cached reads to miss*, remedy
  *set `Koan:Web:CacheControl:HonorClientHeaders`*. Development, Staging, Test, and CI honour the
  headers; Production requires that flag or `Koan:AllowMagicInProduction`.
- It **announces rather than enforces**. Skipping is a coherent outcome — the application still
  serves, it just stops taking cache instructions from callers — so a refusal is logged, not thrown.
- The gate is evaluated once, when the pipeline is built. Nothing is re-decided per request.
- `UseKoanCacheControl` is **deleted**, not deprecated. Leaving it would keep a second way to mount
  the same middleware, at an order the framework no longer controls.

## Consequences

- An application that referenced `Koan.Web` and never called the extension now honours these headers
  in development. That is the intended composition, and it is the change most likely to be noticed.
- The exposure that motivated opt-in is now *stronger*, not weaker: previously any production host
  that called the extension honoured caller headers with nothing standing in the way. Now production
  requires a named consent flag, and refusing says which capability, which risk, and which setting.
- A host that wants the old always-on production behaviour sets one configuration key, and that key
  appears in startup reporting like every other gated convenience.
- Fixing this surfaced an unrelated defect of the same family. `MediaWebModule` registered
  `IOverlayResolver → DefaultOverlayResolver` unconditionally while its required `IMediaSource` is
  registered only when a `MediaEntity` exists. Container validation runs in Development and not in
  Production, so *any* development host referencing `Koan.Media.Web` without a media entity failed to
  build its service provider, while production hosts booted. The resolver is now registered only when
  a source is, which is the law the module's own discovery comment already stated: a bare reference
  must not stop the host from starting.

## References

- `src/Koan.Web/Middleware/CacheControlPipelineContributor.cs`
- `src/Koan.Web/Infrastructure/KoanCacheControlOptions.cs`
- `src/Koan.Media.Web/Initialization/MediaWebModule.cs`
- `tests/Suites/Cache/Web/Koan.Tests.Cache.Web/Specs/KoanCacheControlTestServerSpec.cs`
- `docs/reference/data/cache.md`
