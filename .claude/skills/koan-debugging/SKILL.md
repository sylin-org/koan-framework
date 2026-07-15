---
name: koan-debugging
description: Framework-specific troubleshooting — boot report analysis, provider elections, auto-registration failures, capability-mismatch errors, container/env diagnostics. Trigger on "service not found", "module not discovered", "provider does not support LINQ", wrong adapter elected, GUID/id not generated, boot fails.
pillar: Core
status: current
last_validated: 2026-06-18
---

# Koan Framework Debugging

## Trigger this skill when you see

- `InvalidOperationException: Unable to resolve service for type '...'` (DI miss) or `[WARNING] Koan:modules ... not found`
- `NotSupportedException: ... does not support LINQ` / capability-mismatch errors
- A wrong provider elected — `data→json (expected: mongodb)` / `(fallback)` in the boot report
- Boot failures, container start-up exceptions, `Unable to connect to database`
- Questions about reading the **boot report**, provider elections, or `KoanEnv` environment detection
- N+1 query floods, `OutOfMemoryException` from `.All()`, or other framework-shaped perf symptoms
- References to `KoanAutoRegistrar`, `ProvenanceModuleWriter`, `Data<T,K>.Capabilities`, `KoanEnv`

## Core principle

**Debug Koan from the boot report and the capability set, not from generic .NET intuition.** Most "it doesn't work" failures are a missing reference (Reference = Intent didn't fire), a non-discoverable registrar, a fallback election, or a provider that lacks a capability you assumed. Probe — never assume — the provider's `CapabilitySet` (ARCH-0084) and branch on what it actually advertises.

<!-- validate -->
```csharp
using Koan.Core;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Microsoft.Extensions.Logging;

public static class DiagnoseProvider
{
    // Capability-aware branch: probe the provider's CapabilitySet (ARCH-0084),
    // never assume a feature. Falls back to in-memory filtering when the
    // adapter can't push the predicate down.
    public static async Task<IReadOnlyList<Todo>> CompletedTodos(ILogger logger)
    {
        logger.LogInformation("Environment: {Env}", KoanEnv.EnvironmentName);
        if (KoanEnv.IsDevelopment) KoanEnv.DumpSnapshot(logger);

        var caps = Data<Todo, string>.Capabilities;          // unified CapabilitySet
        if (caps.Has(DataCaps.Query.Linq))
            return await Todo.Query(t => t.Done);            // pushed down to the store

        var all = await Todo.All();                          // in-memory fallback
        return all.Where(t => t.Done).ToList();
    }
}

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}
```

`Data<T,K>.Capabilities` is the single source of truth. `QueryCaps` / the `[Flags] QueryCapabilities` enum / `.HasFlag(...)` were removed by ARCH-0084 — capabilities are now tokens you test with `caps.Has(DataCaps.Query.Linq)` (and `DataCaps.Write.FastRemove`, surfaced as `Todo.SupportsFastRemove`).

## Reading the boot report (your first move)

```bash
docker logs koan-app --tail 40 | grep "Koan:"        # or KoanEnv.DumpSnapshot(logger) in Development
```

```
[INFO] Koan:discover postgresql: server=localhost;database=myapp ... OK
[INFO] Koan:discover mongodb:    connection timeout FAILED
[INFO] Koan:modules data→postgresql (elected: connection successful)
[INFO] Koan:modules web→controllers (discovered: 5 controllers)
[INFO] Koan:modules MyApp v1.0.0 (services: TodoService)
[WARNING] Email SMTP missing — using console fallback
```

| Look for | Means |
|---|---|
| `OK` per discovered provider | discovery + connection succeeded |
| `data→postgresql` (not `→json (fallback)`) | the adapter you intend was elected |
| your module listed with version | the registrar was discovered |
| `FAILED` / `(fallback)` | connection failed → it silently dropped to a lower tier |

## Common symptoms → cause → fix

| Symptom | Likely cause | Fix |
|---|---|---|
| `Unable to resolve service for type 'ITodoService'` | `KoanAutoRegistrar` missing, `internal`, or not in a referenced assembly | make it `public sealed`, implement `IKoanAutoRegistrar`, register in `Initialize` |
| `Koan:modules MyModule not found` | no `<ProjectReference>`/`<PackageReference>`, or assembly not copied to output | add the reference (Reference = Intent), confirm the dll ships |
| `NotSupportedException: ... does not support LINQ` | adapter lacks `DataCaps.Query.Linq` (JSON / InMemory / Redis) | branch on `caps.Has(DataCaps.Query.Linq)`, fall back to `.All()` + in-memory `.Where(...)` |
| `data→json (expected: mongodb)` | connection failed → fallback, or provider package missing | check connection string + service health; pin with `[DataAdapter("mongodb")]` |
| `Entity ID is required but not set` | using `Entity<T,TKey>` (custom key) without assigning `Id` | use `Entity<T>` for auto GUID v7, or set the custom key yourself |
| N+1 query flood | `Todo.Get(id)` inside a loop | one call: `await Todo.Get(ids)` |
| `OutOfMemoryException` on `.All()` | materializing a huge set | On a `ProviderBoundedPaging` adapter use `AllStream`; InMemory/JSON/Redis reject, so page/materialize explicitly or choose a qualified adapter. |

## Discoverability: the registrar contract

A registrar must be `public`, implement `IKoanAutoRegistrar` (which extends `IKoanInitializer`), and live in a referenced assembly. `Describe` writes provenance through `ProvenanceModuleWriter` — its verbs are `Describe(version, description)`, `SetStatus`, `SetSetting`, `SetNote` (there is no `AddModule`/`AddWarning`).

```csharp
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "MyApp";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
        => services.AddScoped<ITodoService, TodoService>();

    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
        => module.Describe(ModuleVersion).SetStatus("ready");
}
```

> Prefer the `KoanModule` primitive (ARCH-0086 — `Id` / `Register` / `Start` / `Report`) for new modules; it implements `IKoanAutoRegistrar` so the same discovery + `[Before]`/`[After]` ordering applies.

## Environment detection

```csharp
logger.LogInformation("Env={Env} Dev={Dev} Prod={Prod} Container={C} Magic={M}",
    KoanEnv.EnvironmentName, KoanEnv.IsDevelopment, KoanEnv.IsProduction,
    KoanEnv.InContainer, KoanEnv.AllowMagicInProduction);
KoanEnv.DumpSnapshot(logger);   // one-line structured snapshot of the resolved environment
```

`KoanEnv.EnvironmentName` is the string (there is no `CurrentEnvironment`). In containers, use the service hostname (`Host=postgres`) — `localhost` is the classic "Unable to connect" cause.

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `Data<T,K>.QueryCaps` / `QueryCapabilities.HasFlag(...)` / `.ProviderName` | `Data<T,K>.Capabilities.Has(DataCaps.Query.Linq)` — the unified `CapabilitySet` (ARCH-0084) |
| `KoanEnv.CurrentEnvironment` | `KoanEnv.EnvironmentName` |
| Manual `services.AddXxx()` for a module that ships a registrar | reference the package — Reference = Intent wires it; double-registration masks the real boot order |
| Catching the LINQ `NotSupportedException` and swallowing it | branch on `caps.Has(DataCaps.Query.Linq)` *before* querying |
| `Todo.Get(id)` in a loop while debugging an N+1 | `Todo.Get(ids)` batch |
| Reasoning about elections from memory | read the boot report / `DumpSnapshot` — elections are reported, not guessed |

## Escape hatches

- **Force an adapter** when election is ambiguous: `[DataAdapter("mongodb")]` on the entity pins the provider and removes fallback guessing.
- **Trace a single stuck path**: `KoanEnv.DumpSnapshot(logger)` plus `grep "Koan:"` on the boot log isolates discovery vs election vs your module.
- **Drop to the store** when you need to confirm the data physically landed: `IDataService.Direct(...)` (`Koan.Data.Core`) bypasses the entity decorators for a raw read/write probe.
- **Capability probe in code**: `Data<T,K>.As<TCapability>()` returns `null` when the backing adapter doesn't implement an optional interface — the cast *is* the probe.

## See also

- [Reference card: data.md](../../../docs/reference/cards/data.md) — the pillar map (capabilities, providers, verbs)
- [Troubleshooting: bootstrap-failures.md](../../../docs/guides/troubleshooting/bootstrap-failures.md) — DI / discovery failures
- [Troubleshooting: adapter-connection-issues.md](../../../docs/guides/troubleshooting/adapter-connection-issues.md) — election + connectivity
- [Deep dive: bootstrap-lifecycle.md](../../../docs/guides/deep-dive/bootstrap-lifecycle.md) — discovery → registrar → boot report order
- [ARCH-0084 — unified capability model](../../../docs/decisions/ARCH-0084-unified-capability-model.md) — why `CapabilitySet.Has(token)` replaced the flags enum
