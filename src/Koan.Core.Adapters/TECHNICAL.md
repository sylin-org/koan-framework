---
uid: reference.modules.koan.core.adapters
title: Koan.Core.Adapters – Technical Reference
description: Readiness pipeline, options binding, and boot-report helpers shared across Koan adapters.
since: 0.6.3
packages: [Sylin.Koan.Core.Adapters]
source: src/Koan.Core.Adapters/
---

## Contract

`Koan.Core.Adapters` is infrastructure, not an adapter base class. It supplies three reusable
concerns so each adapter (data, AI, cache, messaging) does not re-implement them:

1. a **readiness pipeline** that initializes adapters on startup and gates operations until
   they are ready;
2. **options binding** helpers for adapter configuration; and
3. **boot-report (provenance)** helpers for consistent self-reporting.

There is no `IKoanAdapter` / `BaseKoanAdapter` type and no capability DSL in this project.
Adapters declare their own provider types and capabilities elsewhere.

## Readiness pipeline

### State and policy

| Type                    | Responsibility                                                                                  |
| ----------------------- | ----------------------------------------------------------------------------------------------- |
| `AdapterReadinessState` | `Initializing`, `Ready`, `Degraded`, `Failed`.                                                  |
| `ReadinessPolicy`       | `Immediate` (fail fast if not ready), `Hold` (wait up to timeout), `Degrade` (proceed anyway).  |
| `ReadinessStateManager` | Thread-safe state holder; `TransitionTo(...)`, `IsReady` (Ready **or** Degraded), `Wait(timeout, ct)` backed by a `TaskCompletionSource`, and a `StateChanged` event. |
| `ReadinessStateChangedEventArgs` | Carries previous/current state and a UTC timestamp.                                     |

### Interfaces

- `IAdapterReadiness` — what an adapter exposes: `ReadinessState`, `IsReady`,
  `ReadinessTimeout`, `IsReadyAsync(ct)`, `WaitForReadiness(timeout?, ct)`, the
  `ReadinessStateChanged` event, and the underlying `StateManager`.
- `IAdapterReadinessConfiguration` — `Policy`, `Timeout`, `EnableReadinessGating`.
- `IAsyncAdapterInitializer` — `InitializeAsync(ct)`; implemented by adapters that need async
  startup work.

### Hosted services

- `AdapterInitializationService` (`IHostedService`) enumerates every registered
  `IAsyncAdapterInitializer` on `StartAsync`, groups them into ordered **waves** via any
  registered `IAdapterInitializationOrder` policies (lowest `Priority` first; the unordered
  remainder runs last), and runs each wave with `Task.WhenAll`. Each initializer runs under a
  retry/timeout policy from `IRetryPolicyProvider`. An initializer that throws is logged and
  skipped — it does not abort host startup.
- `AdapterReadinessMonitor` (`IHostedService`) subscribes to each `IAdapterReadiness`'s
  `StateManager.StateChanged` and pushes a health entry (`adapter:{TypeName}`) to the
  `IHealthAggregator`, mapping `Ready`→Healthy, `Degraded`→Degraded, `Failed`→Unhealthy. It is
  a no-op when `EnableMonitoring` is false or no aggregator is present.

### Retry / timeout

- `IRetryPolicyProvider.GetPolicy(adapterType)` → `IAdapterInitializationRetryPolicy`.
- `DefaultRetryPolicyProvider` / `DefaultAdapterInitializationRetryPolicy` apply
  `AdaptersReadinessOptions.InitializationTimeout` as a linked-token cancellation deadline and
  log a timeout when it elapses.

### Operation gating

`AdapterReadinessExtensions`:

- `WithReadinessAsync<T>(this object adapter, Func<Task<T>> op, ct)` — if the adapter is an
  `IAdapterReadiness` (and gating is enabled), applies its `ReadinessPolicy`: `Immediate`
  throws `AdapterNotReadyException` when not ready, `Hold` awaits `WaitForReadiness(timeout)`,
  `Degrade` proceeds. Non-readiness adapters pass through.
- `WithReadinessAsync<T, TEntity>(...)` — same gating, plus **schema auto-provisioning**: on a
  schema-not-found style failure it issues `DataInstructions.EnsureCreated` via
  `IInstructionExecutor<TEntity>` (when the adapter implements it) and retries the operation
  once.
- `WithReadiness(this object adapter, Func<Task> op, ct)` — void overload.
- `AdapterNotReadyException : InvalidOperationException` — exposes `AdapterType` and
  `CurrentState`.

## Options binding

- `IAdapterOptions` — `Readiness` (an `IAdapterReadinessConfiguration`) and `DefaultPageSize`.
  Page-size *capping* is intentionally excluded; `DefaultPageSize` is a fallback for callers
  that omit a size, not an enforced cap (output-layer limits live in `Koan.Web`).
- `AdapterReadinessConfiguration` — concrete options (`Policy = Hold`, `Timeout = 30s`,
  `EnableReadinessGating = true` by default).
- `AdaptersReadinessOptions` — module defaults bound from `Koan:Adapters:Readiness`:
  `DefaultPolicy` (`Hold`), `DefaultTimeout` (`30s`), `InitializationTimeout` (`5m`),
  `EnableMonitoring` (`true`).
- `AdapterOptionsConfigurator<TOptions> : IConfigureOptions<TOptions>` — abstract base that, on
  `Configure`, calls `ConfigureProviderSpecific(options)` then applies shared readiness and
  paging keys. Key resolution prefers a per-provider key
  (`Koan:Data:{Provider}:Readiness:*`, `Koan:Data:{Provider}:DefaultPageSize`) and falls back
  to the shared key (`Koan:Data:Readiness:*`, `Koan:Data:DefaultPageSize`). `ProviderName` is
  supplied by the subclass.

Configuration keys are centralized in the internal `Infrastructure.ConfigurationConstants`
(`Koan:Data:*`, `Koan:Services:*`, `Koan:AI:*`, `Koan:Cache:*`, `Koan:Adapters:Readiness`).

## Boot reporting

`AdapterBootReporting` provides `ProvenanceModuleWriter` extension methods so adapters report
consistently:

- `ReportAdapterConfiguration<TOptions>(moduleName, moduleVersion, options, reportProviderSpecific?)`
  — writes `DefaultPageSize` and the three readiness settings, then invokes an optional
  provider-specific callback.
- `ReportConnectionString(moduleName, connectionString, settingName?)` — redacts secrets via
  `Redaction.DeIdentify`.
- `ReportStorageTargets(moduleName, database?, container?, scope?)` and
  `ReportPerformanceSettings(moduleName, queryTimeout?, connectionTimeout?, retryCount?)`.
- `ResolveConnectionString(configuration, IServiceDiscoveryAdapter, parameters, fallback, healthCheckTimeout?)`
  — runs autonomous discovery and falls back to the supplied delegate on failure.
- `ConfigureForBootReport<TOptions>(...)` / `ConfigureForBootReportWithConfigurator<TOptions, TConfigurator>(...)`
  — materialize a configured options instance for reporting without a running host.

`BootstrapReport` is a record (`AdapterId`, `DisplayName`, `ServiceType`, `BootstrapState`,
optional `Message` / `Duration` / `Metadata`) with `Success` / `Failed` / `Skipped` factories
and fluent `WithMetadata(...)`. `BootstrapState` is `Success` / `Failed` / `Skipped`.

## Orchestration awareness

- `OrchestrationAwareAttribute` — a marker attribute for orchestration-aware methods/classes.
- `UnifiedServiceMetadata` (in `Koan.Orchestration`) — `ServiceKind`, `IsOrchestrationAware`,
  and a `Capabilities` list with a case-insensitive `HasCapability(...)`.

## Auto-registration

`Koan.Core.Adapters.Initialization.KoanAutoRegistrar` (module name
`Koan.Core.Adapters.Readiness`) wires the package on reference:

- `services.AddKoanOptions<AdaptersReadinessOptions>(SectionPath)`;
- `services.TryAddSingleton<IRetryPolicyProvider, DefaultRetryPolicyProvider>()`;
- `services.TryAddEnumerable(...)` for `AdapterInitializationService` and
  `AdapterReadinessMonitor` as `IHostedService` (idempotent across multiple references);
- `Describe(...)` reads the four readiness settings with source tracking and publishes them to
  the boot report via `ProvenanceModuleWriter` / `AdaptersReadinessProvenanceItems`.

Adapters register their own `IAsyncAdapterInitializer` and `IConfigureOptions<TOptions>` in
their own registrars; this package only provides the shared infrastructure they bind to.
