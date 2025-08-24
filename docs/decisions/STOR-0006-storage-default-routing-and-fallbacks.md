---
id: STOR-0006
slug: STOR-0006-storage-default-routing-and-fallbacks
domain: storage
status: accepted
date: 2025-08-24
title: Storage default routing and fallbacks (minimal-config behavior)
---

Context

Sora storage supports profiles and routing rules. For small apps and development, we want a sane default that works with minimal configuration, without risking silent misroutes in production.

Decision

- Add options to control defaulting:
  - StorageOptions.DefaultProfile: string? — used when no profile is provided by the caller.
  - StorageOptions.FallbackMode: Disabled | SingleProfileOnly | NamedDefault. Default: SingleProfileOnly.
  - StorageOptions.ValidateOnStart: bool. Default: true.
- Resolution order when caller does not specify a profile:
  1) Use DefaultProfile when set; must exist in Profiles.
  2) If FallbackMode == SingleProfileOnly and Profiles has exactly one item, use it and emit a warning/audit.
  3) Otherwise, fail with a clear error.
- If the caller specifies an explicit profile, resolution requires an exact match (fail fast if unknown).

Scope

- Applies to core orchestrator (IStorageService) resolution only. Routers/rules remain first-class when configured.
- Does not change provider behavior; providers remain IO-only and stateless regarding routing.

Consequences

- Dev/local: zero-config single-provider works out of the box.
- Prod: requires DefaultProfile or rules; adding a second profile no longer silently changes behavior.
- Observability: a warning is logged when implicit SingleProfileOnly fallback is used.

Implementation notes

- StorageService.Resolve implements the above order and logs a warning on SingleProfileOnly fallback.
- Options binding under Sora:Storage supports DefaultProfile and FallbackMode.

Follow-ups

- Emit audit/metric (router.fallback.count) when fallback is used.
- Add startup validator/health check to enforce explicit default in production.

References

- STOR-0001, STOR-0003
---
