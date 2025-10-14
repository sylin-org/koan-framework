---
type: REF
domain: data
title: "Adapter Diagnostics and Boot Reporting"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-10-14
framework_version: v0.6.3
validation:
  date_last_tested: 2025-10-14
  status: drafted
  scope: docs/reference/data/adapter-diagnostics.md
links:
  adr:
    - decisions/DATA-0061-data-access-pagination-and-streaming
  related:
    - reference/data/index
    - reference/orchestration/index
    - guides/deep-dive/bootstrap-lifecycle
---

# Adapter Diagnostics and Boot Reporting

## Contract

- **Inputs**: Data adapter project with `AdapterConnectionDiagnostics<TOptions>` registered; options configurator inheriting from `AdapterOptionsConfigurator<TOptions>`; boot report augmenters deriving from `AdapterConnectionBootReportAugmenter<TOptions>`.
- **Outputs**: Mutable `BootReport` entries that reflect the resolved connection string (redacted when required), discovery provenance, and adapter health notes without manual duplication in `KoanAutoRegistrar`.
- **Error Modes**: Diagnostics snapshot never recorded (augmenter skips updates), adapter options evaluated before diagnostics registration, or missing consumers list causing incomplete boot settings.
- **Success Criteria**: Each adapter surfaces a single snapshot, boot report entries mutate in-place after orchestrators finish, and module notes describe auto-discovery outcomes with the latest health status.

### Edge Cases

- Auto discovery disabled per adapter -> snapshots use `DiscoveryDisabled` and the augmenter emits `auto` defaults.
- Discovery falls back to localhost -> capture the fallback reason so the boot report shows degraded state.
- Secret-bearing adapters (Redis, SQL) -> override `TreatValueAsSecret` to keep redaction intact.
- Streaming providers -> ensure `AdapterConnectionDiagnostics<T>` lives as a singleton to avoid per-request allocations.
- Multiple consumers -> supply every configurator/factory type name so boot report links remain accurate.

---

## Overview

Koan adapters now share a unified diagnostics pipeline:

1. **Record** – options configurators resolve connection strings, then record an `AdapterConnectionSnapshot` via `AdapterConnectionDiagnostics<TOptions>`.
2. **Augment** – the framework runs `IBootReportAugmenter` instances after modules self-describe; the base `AdapterConnectionBootReportAugmenter<TOptions>` mutates settings, descriptions, and notes using the snapshot.
3. **Report** – modules on the boot screen show current connection provenance without duplicating logic across registrars.

This pattern keeps adapter configuration logic focused while ensuring boot diagnostics stay consistent.

---

## Registration Pattern

`KoanAutoRegistrar` handles option binding, diagnostics, and the augmenter registration in a few lines. Example for MongoDB:

```csharp
public void Initialize(IServiceCollection services)
{
    services.AddKoanOptions<MongoOptions>();
    services.AddSingleton<AdapterConnectionDiagnostics<MongoOptions>>();
    services.AddSingleton<IConfigureOptions<MongoOptions>, MongoOptionsConfigurator>();
    services.AddSingleton<IDataAdapterFactory, MongoAdapterFactory>();
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IBootReportAugmenter>(
        sp => ActivatorUtilities.CreateInstance<MongoBootReportAugmenter>(sp)));
}
```

Use the same `ActivatorUtilities` pattern for other adapters so augmenters come from the container even when they require extra dependencies.

---

## Recording Snapshots

Inside your configurator, record the snapshot once the connection details are known:

```csharp
protected override void ConfigureProviderSpecific(MongoOptions options)
{
    AdapterConnectionSnapshot snapshot;

    if (!string.IsNullOrWhiteSpace(explicitConnectionString))
    {
        snapshot = AdapterConnectionSnapshots.Explicit(explicitConnectionString);
        options.ConnectionString = explicitConnectionString;
    }
    else if (UseAutoDiscovery(options))
    {
        snapshot = ResolveAutonomousConnection(databaseName, username, password);
        options.ConnectionString = snapshot.ConnectionString;
    }
    else
    {
        snapshot = AdapterConnectionSnapshots.Preconfigured(options.ConnectionString ?? string.Empty);
    }

    snapshot = snapshot.WithDatabase(options.Database);
    _diagnostics?.Record(snapshot);
}
```

Every adapter should choose the correct helper (`Explicit`, `Preconfigured`, `Discovery`, etc.) so downstream augmenters can infer provenance.

---

## Boot Report Augmenters

Extend `AdapterConnectionBootReportAugmenter<TOptions>` to describe module-specific metadata while reusing the base logic:

```csharp
internal sealed class MongoBootReportAugmenter
    : AdapterConnectionBootReportAugmenter<MongoOptions>
{
    private static readonly string[] Consumers =
    {
        "Koan.Data.Connector.Mongo.MongoOptionsConfigurator",
        "Koan.Data.Connector.Mongo.MongoClientProvider",
        "Koan.Data.Connector.Mongo.MongoAdapterFactory"
    };

    public MongoBootReportAugmenter(
        IOptions<MongoOptions> options,
        AdapterConnectionDiagnostics<MongoOptions> diagnostics)
        : base("Koan.Data.Connector.Mongo", "MongoDB", options, diagnostics, Consumers)
    {
    }
}
```

Only override base behavior when necessary. For Redis or SQL Server, set `TreatValueAsSecret` to keep connection strings redacted.

---

## Streaming and Paging Providers

Adapters that expose streaming APIs (`AllStream`, `QueryStream`) should ensure diagnostics stay lightweight:

- Register diagnostics as a singleton.
- Record snapshots only once per configuration cycle.
- Avoid logging secrets; rely on boot report redaction instead.

When auto discovery yields large or rotating hosts (e.g., ElasticSearch clusters), include metadata via `snapshot.WithMetadata("cluster", value)` so downstream tools can identify which node handled discovery.

---

## Verification Checklist

- [ ] Options configurator resolves connection, records snapshot, and logs provenance.
- [ ] Registrar registers diagnostics singleton plus the augmenter factory.
- [ ] Augmenter inherits from `AdapterConnectionBootReportAugmenter<TOptions>` with consumers array.
- [ ] Secret-bearing adapters override `TreatValueAsSecret`.
- [ ] Project builds (`dotnet build`) succeed for each adapter.
- [ ] Boot report shows updated description and note after startup (run `KoanEnv.DumpSnapshot` in dev to confirm).

Following the pattern keeps boot diagnostics accurate while letting adapters evolve independently.
