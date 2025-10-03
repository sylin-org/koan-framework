---
uid: reference.modules.koan.core.adapters
title: Koan.Core.Adapters – Technical Reference
description: Adapter foundation, capability registry, and readiness lifecycle shared across Koan modules.
since: 0.6.3
packages: [Sylin.Koan.Core.Adapters]
source: src/Koan.Core.Adapters/
validation:
  date: 2025-09-29
  status: verified
---

## Contract

- Provide the base abstractions (`IKoanAdapter`, `BaseKoanAdapter`) that unify adapter configuration, health, and capability negotiation.
- Expose readiness orchestration so adapters initialize deterministically and surface health/report metadata to the boot report.
- Supply orchestration-aware bridges (e.g., `OrchestrationRuntimeBridge`) so adapters can integrate with Koan CLI/recipes without bespoke wiring.

## Core abstractions

| Component                                           | Responsibility                                                                                                                                                                    |
| --------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IKoanAdapter`                                      | Contract for adapter identification, service type, bootstrap metadata, initialization, capability checks, and health status.                                                      |
| `BaseKoanAdapter`                                   | Partial implementation that injects `ILogger`/`IConfiguration`, provides option & connection helpers, wraps health checks, and routes capability probes to `AdapterCapabilities`. |
| `AdapterCapabilities` DSL                           | Fluent API for declaring supported operations across categories (`Health`, `Configuration`, `Security`, `Messaging`, `Orchestration`, `Data`).                                    |
| `BootstrapReport` helpers                           | Convenience APIs to push adapter metadata into the Koan boot report.                                                                                                              |
| `OrchestrationRuntimeBridge` / `OrchestrationAware` | Provide no-op adapters with orchestration awareness hooks for CLI/recipe integration.                                                                                             |

## Readiness pipeline

- Auto-registered services (`Initialization/KoanAutoRegistrar`) wire the readiness infrastructure when the package is referenced.
- `AdapterInitializationService` enumerates registered adapters (`IKoanAdapter`) on application start, invoking `InitializeAsync` and applying retry logic via `DefaultRetryPolicyProvider`.
- `AdapterReadinessMonitor` continually checks adapter health based on `AdaptersReadinessOptions` and emits `ReadinessStateChangedEventArgs` when state transitions occur.
- `AdaptersReadinessOptions` (bound from `Koan:Adapters:Readiness`) governs default retry policies, initialization timeouts, and monitoring toggles.
- `AdapterReadinessStateManager` tracks per-adapter state (`Hold`, `Ready`, `Faulted`) and exposes `IAdapterReadiness` for components that depend on adapter availability.

## Configuration helpers

- `AddKoanOptions<AdaptersReadinessOptions>` binds readiness policies; defaults are reported through the boot report.
- `BaseKoanAdapter.GetOptions<T>` automatically reads `Koan:Services:{AdapterId}` (with legacy fallbacks) and materializes strongly-typed option classes.
- `BaseKoanAdapter.GetConnectionString` resolves connection strings across modern and legacy configuration paths, falling back to null when absent.
- `BaseKoanAdapter.IsEnabled` evaluates multi-pattern `Enabled` switches, defaulting to true if unspecified.

## Capability negotiation

- `AdapterCapabilities` collects features via `Supports(...)` and is surfaced through `IKoanAdapter.Capabilities`.
- `BaseKoanAdapter.SupportsCapability<TEnum>` fans out across known capability enums, allowing Koan subsystems to reason about adapter behavior without explicit type checks.
- Implementers override `Describe(AdapterCapabilities caps)` (see README sample) to populate categories such as `query:vector`, `index:create`, etc.

## Orchestration integration

- `OrchestrationRuntimeBridge` connects adapters to orchestrators (Docker, Podman, Aspire) by publishing capability metadata for container scaffolding.
- `AdapterCapabilities.WithOrchestration()` marks features used by the Koan CLI when generating Compose/apphost artifacts.
- `OrchestrationAware` base class provides convenience methods for adapters needing to emit orchestration-specific state.

## Auto-registration

- `Koan.Core.Adapters.Initialization.KoanAutoRegistrar` adds readiness hosted services, default retry policy providers, and adapter options. It also writes readiness configuration to the boot report (`Adapters.Readiness:*`).
- Registration is idempotent: `TryAddEnumerable` ensures multiple references do not duplicate hosted services.
- Logger scaffolding records initialization progress in debug logs.

## Validation notes

- Code reviewed: `BaseKoanAdapter.cs`, `AdapterCapabilities.cs`, readiness services under `Readiness/`, and `Initialization/KoanAutoRegistrar.cs` (2025-09-29).
- Confirmed configuration helpers respect legacy keys and avoid null dereferences.
- Verified readiness services register via hosted services and integrate with `AdaptersReadinessOptions`.
- Doc build validated through `docs:build` task (2025-09-29).
