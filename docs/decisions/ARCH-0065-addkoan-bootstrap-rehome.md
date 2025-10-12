# ARCH-0065 AddKoan bootstrap lives in Koan.Core

**Contract**

- **Inputs:** Current bootstrap extensions (`AddKoanCore`, `AddKoan`) across `Koan.Core` and `Koan.Data.Core`, Koan auto-initializer pipeline, documentation and samples referencing the one-liner.
- **Outputs:** `AddKoan` implemented in `Koan.Core`, `Koan.Data.Core` exposing only `AddKoanDataCore`, updated docs/tests/samples to import the core namespace, additional initializers if any data wiring is no longer reached implicitly.
- **Error Modes:** Circular project references between `Koan.Core` and `Koan.Data.Core`, missing data services after the move due to reliance on implicit bootstrap, drift between docs and real extension location.
- **Success Criteria:** No shims or forwarding extensions, hosts can call `AddKoan()` with only the core package, data features auto-register via existent initializers when the assembly is referenced, docs and samples reflect the new structure.

## Context

In the current tree the flagship `AddKoan()` extension is defined inside `Koan.Data.Core`. The method wires the core kernel (`AddKoanCore()`), runs module bootstrap, and folds in data-specific setup. This placement leaks a dependency from the general Koan host bootstrap into a specific vertical (data), forcing every minimal app—even non-data workloads—to reference the data assembly just to obtain the canonical startup verb. The documentation already claims the opposite, describing `AddKoan()` as a `Koan.Core` primitive.

The framework’s greenfield posture emphasizes clean separation of concerns, module auto-registration via `IKoanInitializer`, and the "reference = intent" principle. Keeping `AddKoan()` in `Koan.Data.Core` conflicts with those goals and complicates the story around optional packages.

## Decision

Re-home the `AddKoan(IServiceCollection)` extension inside `Koan.Core` alongside `AddKoanCore()`. The core bootstrap will:

1. Invoke `AddKoanCore()` to configure logging, health, runtime, and environment services.
2. Execute the module bootstrapper (`AppBootstrapper.InitializeModules`) so that any referenced assemblies (data, web, messaging, etc.) can hang their own registrations via `IKoanInitializer`/`IKoanAutoRegistrar`.
3. Remain free of vertical-specific references, ensuring the method is available with only the core package.

`Koan.Data.Core` will retain `AddKoanDataCore()` for data-only layering (adapter discovery, schema helpers, recipes). It will no longer declare `AddKoan()`; data functionality continues to attach itself through initializers when the assembly is referenced.

## Rationale

- **SOC Alignment:** Core bootstrapping belongs in the core assembly. Data becomes optional rather than an implicit dependency of every Koan host.
- **Documentation Accuracy:** Docs already promise `AddKoan()` from `Koan.Core`; the code should fulfill that contract rather than rely on an external namespace import.
- **No Shims:** Eliminates the need for forwarding or alias extensions while preserving a single source of truth.
- **Initializer Model:** The Koan auto-registration pipeline already surfaces module capabilities once assemblies are referenced, so the core bootstrap can remain agnostic.

## Implementation Plan

1. **Move the extension:** Relocate `AddKoan(IServiceCollection)` into `Koan.Core` by editing the existing `ServiceCollectionExtensions` file directly. Invoke `AddKoanCore()` and `AppBootstrapper.InitializeModules` from that method so the bootstrap lives entirely in core without partials or shims.
2. **Trim data assembly:** Remove the current `AddKoan()` method from `Koan.Data.Core`, preserving `AddKoanDataCore()` and any supplementary helpers.
3. **Audit initializers:** Confirm data services previously wired inside the old method are reachable via `IKoanInitializer` implementations. Add missing initializers where gaps exist.
4. **Update usages:** Adjust samples, tests, documentation, and snippets so `AddKoan()` references come from the core namespace. Retain or add explicit calls to `AddKoanDataCore()` where data semantics are required.
5. **Regression checks:** Run the standard unit/integration suites for web, data connectors, and representative samples to ensure the bootstrap still lights up expected services.
6. **Docs & release notes:** Reflect the relocation in the docs (especially getting-started, guides, and any ADRs that mention the extension) and call out the change in release communications.

## Consequences

- Minimal apps can depend on `Koan.Core` alone without importing data packages.
- Projects that implicitly relied on `AddKoan()` to bring in data must now reference the data assembly (or call `AddKoanDataCore()`) explicitly, making the dependency obvious.
- The initializer pipeline solidifies as the canonical mechanism for module self-registration.

## Status

Implemented – 2025-10-12.

### Implementation Notes

- `services.AddKoan()` now lives in `Koan.Core/ServiceCollectionExtensions.cs`, running `AddKoanCore()` and the module bootstrapper without data-specific hooks.
- `Koan.Data.Core` exposes only `AddKoanDataCore()`; a new `KoanDataCoreInitializer` wires data services when the assembly is referenced.
- `Koan.Data.Direct` declares `KoanDataDirectInitializer` so direct-mode services attach via reflection-free initialization.
- Samples/tests import `Koan.Core` for the one-liner; data scenarios invoke `AddKoanDataCore()` explicitly when needed.
