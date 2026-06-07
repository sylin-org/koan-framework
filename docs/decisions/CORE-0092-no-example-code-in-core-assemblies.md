# CORE-0092: No example/demo code in framework core assemblies

**Status**: **Accepted (2026-06-07)** — architect-approved.
**Date**: 2026-06-07
**Deciders**: Enterprise Architect
**Scope**: Example/demo code must not ship in framework **core** assemblies, where Koan's reflective discovery auto-activates it in every consuming app.
**Related**: KoanBackgroundServiceAutoRegistrar · ARCH-0079 (caught by the SEC-0001 auth e2e) · jobs-howto.md.

---

## 1. Context

`Koan.Core/BackgroundServices/Examples/` shipped **live** demo code inside the core assembly:

- **`ExampleServices.cs`** — `[KoanStartupService] DatabaseMigrationService` (a `Task.Delay(5000)` demo), plus `TranslationService` / `NotificationService` / etc. Because they carry the discovery attributes and live in `Koan.Core`, `KoanBackgroundServiceAutoRegistrar` registers them in **every** app with `RunInDevelopment = true` — so the demos run on boot, and `DatabaseMigrationService` trips `FailFastOnStartupFailure`, **aborting host startup** in a minimal host.
- **`FluentApiUsageExample.cs`** — a live `[ApiController] ExampleController` exposing `/api/example/...` endpoints in every app, and pulling a `Microsoft.AspNetCore.Mvc` surface into `Koan.Core`.

This was caught by the SEC-0001 HTTP auth e2e: a minimal app failed to boot because a *demo* migration service aborted startup. Nothing in the repo references these example types.

## 2. Decision

**Example/demo code — especially anything auto-discovered (background services, controllers, registrars, contributors) — does not ship in framework core assemblies.** The `Examples/` folder is removed. The authoring patterns it illustrated belong in documentation (jobs-howto / framework-utilities) and, if a runnable demo is wanted, in a dedicated **sample** project — never in core, where discovery activates it unconditionally.

## 3. Consequences

- Minimal hosts boot cleanly; no demo services run and no demo endpoints are exposed in real apps.
- An inadvertent MVC dependency in `Koan.Core`'s example is gone.
- Lean-core: the framework core no longer carries illustrative code that consumers pay for at boot.

## 4. Alternatives considered

1. **Relocate to a sample.** The services *auto-activate*, so a passive class-library sample wouldn't demonstrate them, and an executable sample would simply re-run the startup-abort. Overhead without a clean demo.
2. **Make inert** (strip discovery attributes, keep in core). Fixes the boot abort but keeps demo code — and an MVC dependency — in core, violating lean-core. Rejected.
3. **Remove** (this decision). Cleanest; the patterns live in docs.

## 5. References

- `src/Koan.Core/BackgroundServices/` (the framework; examples removed) · jobs-howto.md (the authoring patterns).
