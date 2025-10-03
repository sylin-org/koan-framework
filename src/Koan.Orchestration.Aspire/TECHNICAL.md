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

- Discover Koan modules that implement `IKoanAspireRegistrar` and invoke their resource registration inside an Aspire AppHost.
- Provide opt-in, self-orchestration services (Docker-backed) when running outside Aspire to keep the "Reference = Intent" principle alive.
- Surface orchestration mode detection and configuration providers so dependent modules receive correct connection strings across self, Compose, Kubernetes, and AppHost scenarios.
- Hook into Koan boot reporting to document orchestration mode, provider availability, and networking decisions.

## Core components

| Area                             | Types                                                                                                                            | Notes                                                                                                                                                                               |
| -------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Discovery & wiring               | `KoanAspireExtensions`, `KoanAssemblyDiscovery`, `KoanDiscoveryResult`                                                           | Scans loaded assemblies, queues registrars by priority, handles logging, and returns resource names.                                                                                |
| Registrar contract               | `IKoanAspireRegistrar`                                                                                                           | Optional extension for `KoanAutoRegistrar` implementers to describe Aspire resources with `RegisterAspireResources`, `Priority`, and `ShouldRegister`.                              |
| Orchestration auto-registration  | `Initialization.KoanAutoRegistrar`                                                                                               | Detects `KoanEnv.OrchestrationMode`, injects matching config providers, registers self-orchestration hosted service in `SelfOrchestrating` mode, and contributes boot report notes. |
| Self orchestration               | `DockerContainerManager`, `KoanDependencyOrchestrator`, `KoanSelfOrchestrationService`, `SelfOrchestrationConfigurationProvider` | Spins up dependencies via Docker, waits for health, cleans orphaned containers, and synthesizes connection strings for local usage.                                                 |
| Configuration adapters           | `SelfOrchestrationConfigurationProvider`, `DockerComposeConfigurationProvider`, `KubernetesConfigurationProvider`                | Inject connection strings + orchestration metadata per detected mode without forcing app code changes.                                                                              |
| Provider selection (placeholder) | `UseKoanProviderSelection`                                                                                                       | Applies container runtime hints (`ASPIRE_CONTAINER_RUNTIME`) once Koan provider election logic is integrated.                                                                       |

## Discovery & registration flow

1. `AddKoanDiscoveredResources()` is called from an Aspire AppHost (`Program.cs`).
2. The extension configures dashboard defaults, creates a logger, and calls `ForceLoadKoanAssemblies` so referenced `Koan.*.dll` assemblies are present in the AppDomain.
3. `KoanAssemblyDiscovery.GetKoanAssemblies()` enumerates loaded assemblies, filtering by `Koan.` prefix or presence of a `KoanAutoRegistrar` type. Assemblies are wrapped in `KoanAssemblyInfo` for diagnostics when `GetDetailedAssemblyInfo()` is requested.
4. Each assembly is inspected for a `KoanAutoRegistrar` that implements `IKoanAspireRegistrar`. Registrars passing `ShouldRegister(cfg, env)` are queued with their declared `Priority`.
5. Registrars are ordered ascending by priority and `RegisterAspireResources(builder, cfg, env)` is invoked inside a try/catch. Failures are logged and re-thrown as `InvalidOperationException` to stop AppHost boot.
6. The helper returns the shared `IDistributedApplicationBuilder`, enabling fluent chaining, and logs the number of registered resource providers.

### Explicit registration

- `AddKoanModule<TRegistrar>()` allows deterministic registration for tests or selective bootstraps. It respects `ShouldRegister`, wraps errors, and logs operations.

## Registrar contract (`IKoanAspireRegistrar`)

- Designed to be implemented alongside `IKoanAutoRegistrar` so modules can handle DI plus orchestration through a single type.
- Members:
  - `RegisterAspireResources(builder, configuration, environment)` – create Aspire resources (databases, caches, external services) from configuration.
  - `Priority` (default 1000) – smaller numbers register first (infrastructure before apps).
  - `ShouldRegister(configuration, environment)` – skip heavy resources (e.g., AI stacks) outside development or when configuration chooses external providers.
- Suggested priority bands are documented in the interface comments (100–500 for infrastructure, ≥1000 for apps/background workers).

## Orchestration mode detection & boot reporting

- `Initialization.KoanAutoRegistrar` runs as part of Koan auto-registration when the package is referenced.
- It reads `KoanEnv.OrchestrationMode`, emits the current session ID, and selects a configuration provider:
  - `SelfOrchestrating` → `SelfOrchestrationConfigurationProvider`
  - `DockerCompose` → `DockerComposeConfigurationProvider`
  - `Kubernetes` → `KubernetesConfigurationProvider`
  - AppHost and Standalone keep existing configuration.
- In `SelfOrchestrating` mode it wires `IKoanContainerManager`, `IKoanDependencyOrchestrator`, and `KoanSelfOrchestrationService` (hosted service) so dependencies are spun up during app boot.
- `Describe` publishes orchestration mode, session ID, environment flags, forced mode overrides, network validation toggles, and Docker availability to the boot report. It also records which networking strategy was elected via `AddProviderElection`.

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
- Discovery logs include assembly names, queued registrars, and explicit module registrations. `RegisterAspireResources` failures bubble with contextual messaging.
- Dashboard configuration pre-wires environment variables for development to simplify local dashboards (port `15888`, OTLP endpoint `4317`). Failures degrade gracefully with warnings.

## Validation notes

- Code reviewed: `Extensions/KoanAspireExtensions.cs`, `Discovery/KoanAssemblyDiscovery.cs`, `IKoanAspireRegistrar.cs`, `Initialization/KoanAutoRegistrar.cs`, `SelfOrchestration/*` (Docker manager, dependency orchestrator, configuration providers, hosted service) on 2025-09-29.
- Confirmed that self-orchestration only activates in `KoanEnv.OrchestrationMode == SelfOrchestrating` and that configuration providers are mode-specific.
- Verified container cleanup paths tag with `koan.session`, `koan.app-instance`, and auto-cleanup labels.
- Doc build (`docs:build`) executed post-update; build passes with existing backlog warnings (unrelated XML comments in data-backup projects).
