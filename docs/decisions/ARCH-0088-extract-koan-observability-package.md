# ARCH-0088: Extract the OpenTelemetry wiring into a `Koan.Observability` package

**Status**: Accepted (2026-06-17)
**Date**: 2026-06-17
**Deciders**: Enterprise Architect
**Scope**: Resolves assessment card **G1** (Track G — Koan.Core diet). Moves the OpenTelemetry (OTel) SDK wiring out of `Koan.Core` into a new leaf package **`Koan.Observability`** (`Sylin.Koan.Observability`), so that telemetry becomes **Reference = Intent** (referencing the package enables it) and no framework package hard-pulls the OpenTelemetry dependencies. The health/probe primitives stay in `Koan.Core`.
**Related**: **ARCH-0033** (the original OpenTelemetry integration — this ADR re-homes that wiring without changing its behavior) · **ARCH-0086** (KoanModule / auto-registrar discovery — the Reference=Intent mechanism) · **ARCH-0045** (proactive assembly loading for auto-registration) · **ARCH-0010** (meta-packages / package layering) · the redesign "Koan.Core diet" track.

---

## Context

`Koan.Core` carried 5 OpenTelemetry package references and the `AddKoanObservability` wiring, even though **`AddKoanCore` never calls it** — telemetry is invoked explicitly by apps (`AddKoanObservability()` in `Program.cs`). So every consumer of the kernel transitively pulled the OTel SDK whether or not it wanted telemetry, inverting the framework's "Reference = Intent" principle (a package's dependencies should express *intent*, not be incidental kernel weight).

Empirical re-derivation (read of `ServiceCollectionExtensions.cs`, `ObservabilityOptions.cs`, `Health/**`, `Probes/**`, `Koan.Core.csproj`, `Koan.Web.csproj`, `AppRuntime.cs`, `WellKnownController.cs`, the `IKoanAutoRegistrar` contract) established:

1. **The OTel wiring is localized to one file.** `ServiceCollectionExtensions.AddKoanObservability` is the *only* file in `Koan.Core` importing `OpenTelemetry.*`; it owns all 5 package refs. `PipelineObservabilityExtensions` is pure logging (no OTel).
2. **`AddKoanCore` never calls it.** `AppRuntime` reads `IOptions<ObservabilityOptions>` null-safely; apps without telemetry boot fine. Confirmed "nothing breaks by its absence".
3. **Health/Probes are zero-coupled to OTel.** `Health/**` and `Probes/**` reference neither OpenTelemetry nor `ObservabilityOptions`. Clean cut.
4. **`ObservabilityOptions` is type-referenced from inside `Koan.Core`.** `AppRuntime.Discover` reads it by type — so moving it out would create a `Koan.Core → Koan.Observability` reference, and since `Koan.Observability → Koan.Core` (for the registrar contract), that is a **dependency cycle**. The card's "move ObservabilityOptions too" conflates pure config with the SDK wiring.
5. **`Koan.Web` carried 3 *dead* OTel package refs.** `Koan.Web` referenced 3 OTel packages but has **zero** `OpenTelemetry` usage in its code (the `/observability` well-known endpoint reads `ObservabilityOptions` — a POCO — and `System.Diagnostics.Activity`, neither of which needs the OTel packages). So extracting only from `Koan.Core` would leave every web app still transitively pulling 3 OTel packages — the opt-in would be only half-real.
6. **A registrar re-enables it on reference.** `IKoanAutoRegistrar.Initialize(IServiceCollection)` is sufficient — `AddOpenTelemetry()` is a pure `IServiceCollection` extension, no `IHostBuilder` hook needed. Auto-registrar discovery (ARCH-0045/0086) loads referenced assemblies, so referencing `Koan.Observability` runs its registrar.

---

## Decision

1. **New leaf package `Koan.Observability`** (`Sylin.Koan.Observability`, `KoanPackageKind=Periphery`). It contains:
   - The moved `ServiceCollectionExtensions.cs` (`AddKoanObservability`) — **namespace preserved as `Koan.Core.Observability`** so existing `using`s and explicit callers compile with only a package-reference change (no namespace churn).
   - The 5 OpenTelemetry package references.
   - A `KoanAutoRegistrar` whose `Initialize` calls `AddKoanObservability()` — **Reference = Intent**: referencing the package enables traces + metrics + OTLP export at boot. The public `AddKoanObservability(configure)` method stays for apps that need custom configuration.
   - It `ProjectReference`s `Koan.Core` (for `IKoanAutoRegistrar`, `ObservabilityOptions`, `AddKoanOptions`, the config constants).

2. **`Koan.Core` drops** the 5 OTel package refs and `ServiceCollectionExtensions.cs`. It **keeps** `ObservabilityOptions` (a pure config POCO — moving it would cycle with `AppRuntime`), `PipelineObservabilityExtensions`, and all of `Health/**` + `Probes/**`.

3. **`Koan.Web` drops** its 3 dead OTel package refs (zero code usage). With (2) and this, **no framework package hard-pulls OpenTelemetry** — telemetry is purely opt-in fleet-wide.

4. **Consumers updated.** Samples that called `AddKoanObservability()` explicitly (`S5.Recs`, `S8.Canon`, `S8.Canon.Api`) instead **reference `Koan.Observability`** and drop the explicit call — the registrar enables it (avoids the double-`WithTracing`/`WithMetrics` registration that calling it both via the registrar *and* explicitly would cause).

### Rejected / corrected
- **Move `ObservabilityOptions` out of Core** (card-literal): rejected — creates a `Core ↔ Observability` cycle via `AppRuntime`. Config stays; only the *wiring* (the heavy part with the packages) moves.
- **Leave `Koan.Web`'s OTel refs**: rejected — the opt-in goal would be only half-met (web apps still pull 3 OTel packages). The refs are provably dead, so removing them is free.

---

## Consequences

**Positive**
- `Koan.Core` sheds 5 transitive package dependencies; the kernel no longer carries the OTel SDK. Reference = Intent is restored: telemetry ships only to apps that reference `Koan.Observability`.
- No behavior change for apps that opt in (the wiring is byte-identical, just re-homed + auto-invoked).
- Apps that don't want telemetry no longer pay for it in their dependency graph.

**Cost / caveats**
- One new project (`+1` part). Justified: it converts an always-on kernel dependency into an opt-in leaf — a net reduction in the kernel's hard surface, consistent with the "Koan.Core diet".
- The namespace stays `Koan.Core.Observability` despite living in the `Koan.Observability` assembly — a deliberate minimal-churn choice (assembly ≠ namespace). Noted here so it is not mistaken for an error.

## R11-05 refinement (2026-07-18)

The extraction remains correct, but the retained public `AddKoanObservability` branch did not survive package-product
graduation. Automatic module activation and a manual callback required a sentinel, allowed callback timing to diverge
from the already-compiled provider, and built a temporary service provider during registration. The fixed source list
also omitted most current `Koan.*` instruments, metrics did not subscribe to Koan meters, and OTLP headers applied only
to traces.

The current contract supersedes only those implementation details:

- the package reference plus the application's existing `AddKoan()` call is the sole Koan activation path;
- one internal immutable plan compiles host configuration and environment without a temporary provider;
- standard OpenTelemetry builder APIs are the advanced extension path;
- tracing and metrics subscribe to the single `Koan.*` namespace boundary, so new framework instruments join
  automatically;
- OTLP endpoint and headers apply consistently to trace and metric exporters;
- invalid booleans, sample rates, and endpoints reject module composition correctively;
- module reporting and shared composition facts explain active signals and exporter kind without disclosing endpoint
  or header values.

Core still owns the inert `ObservabilityOptions` and health/probe primitives. Production without an OTLP endpoint
remains deliberately inactive. No Contracts, Web, exporter, contributor, or source-registry package is introduced.

**Follow-up (out of scope)**
- The separately-migrated Agyo observability *bundle* (`Sylin.Agyo.Observability`, card C5) MAY consume `Sylin.Koan.Observability` for its OTel piece now that this exists.
