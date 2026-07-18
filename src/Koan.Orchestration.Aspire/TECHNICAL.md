---
uid: reference.modules.koan.orchestration.aspire
title: Koan.Orchestration.Aspire – Technical Reference
description: Distributed resource discovery, AppHost integration, and self-orchestration services for Koan modules targeting .NET Aspire.
since: 0.6.3
packages: [Sylin.Koan.Orchestration.Aspire]
source: src/Koan.Orchestration.Aspire/
validation:
  date: 2025-09-29
  status: verified
---

## Contract

- Discover Koan modules that implement `IKoanAspireResources` and invoke their resource registration inside an Aspire AppHost.
- Provide opt-in, self-orchestration services (Docker-backed) when running outside Aspire to keep the "Reference = Intent" principle alive.
- Surface orchestration mode detection and configuration providers so dependent modules receive correct connection strings across self, Compose, Kubernetes, and AppHost scenarios.
- Hook into Koan boot reporting to document orchestration mode, provider availability, and networking decisions.

## Core components

| Area                             | Types                                                                                                                            | Notes                                                                                                                                                                               |
| -------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Discovery & wiring               | `KoanAspireExtensions`, `KoanAssemblyDiscovery`, `KoanDiscoveryResult`                                                           | Scans loaded assemblies, queues registrars by priority, handles logging, and returns resource names.                                                                                |
| Aspire resource contract         | `IKoanAspireResources` from `Sylin.Koan.Orchestration.Aspire.Abstractions`                                                       | Inert optional capability on the assembly's single `KoanModule`, with `RegisterAspireResources`, `Priority`, and `ShouldRegister`.                                                 |
| Orchestration module             | `Initialization.AspireModule`                                                                                                    | Detects `KoanEnv.OrchestrationMode`, injects matching config providers, registers self-orchestration hosted service in `SelfOrchestrating` mode, and contributes boot report notes. |
| Self orchestration               | `DockerContainerManager`, `KoanDependencyOrchestrator`, `KoanSelfOrchestrationService`, `SelfOrchestrationConfigurationProvider` | Spins up dependencies via Docker, waits for health, cleans orphaned containers, and synthesizes connection strings for local usage.                                                 |
| Configuration adapters           | `SelfOrchestrationConfigurationProvider`, `DockerComposeConfigurationProvider`, `KubernetesConfigurationProvider`                | Inject connection strings + orchestration metadata per detected mode without forcing app code changes.                                                                              |
| Provider selection (placeholder) | `UseKoanProviderSelection`                                                                                                       | Applies container runtime hints (`ASPIRE_CONTAINER_RUNTIME`) once Koan provider election logic is integrated.                                                                       |

## Discovery & registration flow

1. `AddKoanDiscoveredResources()` is called from an Aspire AppHost (`Program.cs`).
2. The extension configures dashboard defaults, creates a logger, and calls `ForceLoadKoanAssemblies` so referenced `Koan.*.dll` assemblies are present in the AppDomain.
3. `KoanAssemblyDiscovery.GetKoanAssemblies()` enumerates loaded assemblies, filtering official `Koan.` packages quickly and admitting custom loaded assemblies by the Aspire resource capability. Assemblies are wrapped in `KoanAssemblyInfo` for diagnostics when `GetDetailedAssemblyInfo()` is requested.
4. Each assembly is inspected for one concrete `KoanModule` implementing `IKoanAspireResources`. Contributors passing `ShouldRegister(cfg, env)` are queued with their declared `Priority`; multiple contributors in one assembly are a corrective error.
5. Contributors are ordered ascending by priority and `RegisterAspireResources(builder, cfg, env)` is invoked inside a try/catch. Failures are logged and re-thrown as `InvalidOperationException` to stop AppHost boot.
6. The helper returns the shared `IDistributedApplicationBuilder`, enabling fluent chaining, and logs the number of registered resource providers.

### Explicit registration

- `AddKoanModule<TModule>()` allows deterministic registration for tests or selective bootstraps. The type must be both `KoanModule` and `IKoanAspireResources`; it respects `ShouldRegister`, wraps errors, and logs operations.

## Aspire resource contract (`IKoanAspireResources`)

- The contract lives in the isolated `Sylin.Koan.Orchestration.Aspire.Abstractions` package. Referencing its
  vocabulary does not activate this functional Aspire runtime.
- Implemented by the functional assembly's single `KoanModule`, keeping DI and AppHost resource ownership on one semantic owner.
- Members:
  - `RegisterAspireResources(builder, configuration, environment)` – create Aspire resources (databases, caches, external services) from configuration.
  - `Priority` (default 1000) – smaller numbers register first (infrastructure before apps).
  - `ShouldRegister(configuration, environment)` – skip heavy resources (e.g., AI stacks) outside development or when configuration chooses external providers.
- Suggested priority bands are documented in the interface comments (100–500 for infrastructure, ≥1000 for apps/background workers).

## Orchestration mode detection & boot reporting

- `Initialization.AspireModule` runs through `AddKoan()` when the package is referenced.
- It reads `KoanEnv.OrchestrationMode`, emits the current session ID, and selects a configuration provider:
  - `SelfOrchestrating` → `SelfOrchestrationConfigurationProvider`
  - `DockerCompose` → `DockerComposeConfigurationProvider`
  - `Kubernetes` → `KubernetesConfigurationProvider`
  - AppHost and Standalone keep existing configuration.
- In `SelfOrchestrating` mode it wires `IKoanContainerManager`, `IKoanDependencyOrchestrator`, and `KoanSelfOrchestrationService` (hosted service) so dependencies are spun up during app boot.
- `Report` publishes orchestration mode, session ID, environment flags, forced mode overrides, network validation toggles, and Docker availability to the boot report.

## Self orchestrated dependencies

- `KoanDependencyOrchestrator` discovers `IKoanOrchestrationEvaluator` implementations, evaluates required dependencies, and spins them up via `IKoanContainerManager` (Docker manager by default).
- Session/app identifiers are derived from `KoanEnv.SessionId` and entry assembly names to tag containers and environment variables (`KOAN_APP_ID`, `KOAN_APP_INSTANCE`).
- Dependencies are started in priority order, health-checked (`WaitForContainerHealthyAsync`), and tracked for cleanup. Failures escalate via `InvalidOperationException` so the host can abort start.
- Cleanup covers:
  - Session-specific containers on stop.
  - Orphaned containers older than one hour (crash resilience).
  - Containers belonging to previous app sessions sharing the same app instance.
- Configuration providers generate connection strings matching the orchestration mode (localhost, service names, or Kubernetes FQDN) and add metadata (`Koan:Orchestration:*`) consumed by other components.

## Provider selection helpers

- `UseKoanProviderSelection(preferredProvider)` sets `ASPIRE_CONTAINER_RUNTIME` to `docker`, `podman`, or the result of `SelectOptimalProvider`. The current implementation defaults to Docker and logs warnings if selection fails. Future integration will reuse Koan CLI provider election logic.

## Diagnostics & logging

- Logging uses the `Koan.Orchestration.Aspire` category when a logger is available from the builder service provider.
- Discovery logs include assembly names, queued resource contributors, and explicit module registrations. `RegisterAspireResources` failures bubble with contextual messaging.
- Dashboard configuration pre-wires environment variables for development to simplify local dashboards (port `15888`, OTLP endpoint `4317`). Failures degrade gracefully with warnings.

## Validation notes

- Focused code reviewed: `Extensions/KoanAspireExtensions.cs`, `Discovery/KoanAssemblyDiscovery.cs`, the isolated Aspire contract, `Initialization/AspireModule.cs`, and `SelfOrchestration/*` on 2026-07-17.
- Confirmed that self-orchestration only activates in `KoanEnv.OrchestrationMode == SelfOrchestrating` and that configuration providers are mode-specific.
- Verified container cleanup paths tag with `koan.session`, `koan.app-instance`, and auto-cleanup labels.
- Doc build (`docs:build`) executed post-update; build passes with existing backlog warnings (unrelated XML comments in data-backup projects).
