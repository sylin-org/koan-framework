# Koan.Core.Adapters

Shared infrastructure that adapters (data, AI, cache, messaging) lean on for three
cross-cutting concerns: **readiness gating**, **options binding**, and **boot reporting**.

This package does not define an adapter base class or a capability DSL. Adapters declare
their own provider types and capabilities; `Koan.Core.Adapters` provides the small set of
helpers below that every adapter would otherwise re-implement.

## Contract

- **Purpose**: Provide reusable readiness, configuration, and provenance-reporting primitives
  for Koan adapters so each adapter does not hand-roll the same startup/health/report plumbing.
- **Primary inputs**: `IAsyncAdapterInitializer` implementations, adapter options types
  implementing `IAdapterOptions`, and configuration under `Koan:Adapters:Readiness` /
  `Koan:Data:*`.
- **Outputs**: Ordered async initialization on startup, health pushes to the
  `IHealthAggregator`, strongly-typed options bound from configuration, and consistent boot
  report (provenance) entries.
- **Failure modes**: An initializer that throws is logged and skipped (it does not abort
  startup); a readiness wait can time out (`AdapterNotReadyException` / `TimeoutException`).
- **Success criteria**: Adapters initialize deterministically, surface readiness to health,
  and report their configuration through the boot report without bespoke wiring.

## What this package gives you

### 1. Readiness pipeline

- `AdapterReadinessState` (`Initializing`, `Ready`, `Degraded`, `Failed`) and `ReadinessPolicy`
  (`Immediate`, `Hold`, `Degrade`).
- `ReadinessStateManager` — thread-safe state holder with a `Wait(timeout, ct)` signal and a
  `StateChanged` event.
- `IAdapterReadiness` — the readiness surface an adapter exposes (current state, `IsReady`,
  `IsReadyAsync`, `WaitForReadiness`, the underlying `ReadinessStateManager`).
- `IAsyncAdapterInitializer` — implement `InitializeAsync(ct)` to participate in startup.
- `AdapterReadinessExtensions.WithReadinessAsync(...)` — wrap an operation so it honours the
  adapter's `ReadinessPolicy` (and, with the `TEntity` overload, retries once after schema
  auto-provisioning on a schema-not-found failure).
- `AdapterNotReadyException` — carries the offending `AdapterType` and `CurrentState`.

### 2. Options binding

- `IAdapterOptions` — common shape (`Readiness`, `DefaultPageSize`). Page-size *capping* is
  intentionally not here; `DefaultPageSize` is a fallback, not a cap.
- `IAdapterReadinessConfiguration` / `AdapterReadinessConfiguration` — per-adapter readiness
  policy, timeout, and gating toggle.
- `AdaptersReadinessOptions` — module-wide defaults bound from `Koan:Adapters:Readiness`
  (`DefaultPolicy`, `DefaultTimeout`, `InitializationTimeout`, `EnableMonitoring`).
- `AdapterOptionsConfigurator<TOptions>` — abstract `IConfigureOptions<TOptions>` base that
  applies readiness + paging keys (per-provider and shared `Koan:Data:*` fallbacks); override
  `ConfigureProviderSpecific` for the rest.

### 3. Boot reporting

- `AdapterBootReporting` — extension methods on `ProvenanceModuleWriter`:
  `ReportAdapterConfiguration<TOptions>(...)`, `ReportConnectionString(...)` (redacts secrets),
  `ReportStorageTargets(...)`, `ReportPerformanceSettings(...)`, plus
  `ConfigureForBootReport*` helpers and `ResolveConnectionString(...)` for discovery-aware
  connection resolution.
- `BootstrapReport` record + `BootstrapState` enum (`Success`/`Failed`/`Skipped`) — a
  structured per-adapter status with fluent `WithMetadata(...)`.

## Quick start

```csharp
using Koan.Core.Adapters;

// 1. Participate in ordered startup initialization.
internal sealed class MyAdapterInitializer : IAsyncAdapterInitializer
{
    public Task InitializeAsync(CancellationToken ct = default) => /* open pool, ping, etc. */;
}

// 2. Bind options with the shared configurator.
internal sealed class MyAdapterOptionsConfigurator(
    IConfiguration config,
    ILogger<MyAdapterOptionsConfigurator>? logger,
    IOptions<AdaptersReadinessOptions> readiness)
    : AdapterOptionsConfigurator<MyAdapterOptions>(config, logger, readiness)
{
    protected override string ProviderName => "MyProvider";
    protected override void ConfigureProviderSpecific(MyAdapterOptions options) { /* ... */ }
}

// 3. Gate operations on readiness.
var result = await adapter.WithReadinessAsync(() => adapter.QueryAsync(...), ct);
```

Register `IAsyncAdapterInitializer` and your `IConfigureOptions<MyAdapterOptions>` in your
adapter's own `IKoanAutoRegistrar` (Reference = Intent). This package's own registrar wires the
readiness hosted services automatically when it is referenced.

## Auto-registration

`Koan.Core.Adapters.Initialization.KoanAutoRegistrar` (module `Koan.Core.Adapters.Readiness`):

- binds `AdaptersReadinessOptions` from `Koan:Adapters:Readiness`;
- registers `IRetryPolicyProvider` → `DefaultRetryPolicyProvider`;
- adds the two hosted services `AdapterInitializationService` and `AdapterReadinessMonitor`
  via `TryAddEnumerable` (idempotent across multiple package references);
- publishes the readiness defaults to the boot report through `ProvenanceModuleWriter`.

## Related packages

- `Koan.Core` — configuration, environment, provenance (`ProvenanceModuleWriter`), and the
  `IHealthAggregator` that the readiness monitor pushes to.
- `Koan.Orchestration.Abstractions` — service-discovery types used by `ResolveConnectionString`.
- `Koan.Data.Abstractions` — instruction contracts used by the schema-provisioning retry path.

## Documentation

- [`TECHNICAL.md`](./TECHNICAL.md) — readiness lifecycle, options binding, boot reporting, and
  the auto-registration contract in depth.
