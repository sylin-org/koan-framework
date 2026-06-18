---
name: koan-bootstrap
description: Project setup and boot — minimal Program.cs (AddKoan), Reference = Intent auto-registration, KoanModule (ARCH-0086) and IKoanAutoRegistrar, ProvenanceModuleWriter boot reports, KoanEnv/Configuration helpers
pillar: core
status: current
last_validated: 2026-06-18
---

# Koan Bootstrap & Auto-Registration

## Trigger this skill when you see

- A `Program.cs` / `Startup` / project-setup question, or `builder.Services.AddKoan()`
- `KoanModule`, `IKoanAutoRegistrar`, `IKoanInitializer`, `[KoanDiscoverable]`, `KoanRegistry`
- `Describe(...)` / `Report(...)` boot reports, `ProvenanceModuleWriter`, `BootReport` (removed type)
- `KoanEnv.IsDevelopment` / `IsProduction` / `InContainer` / `DumpSnapshot`
- `Configuration.Read(...)` / `Configuration.ReadFirst(...)`
- Manual `services.AddScoped/AddSingleton/AddHostedService` in `Program.cs`, `AddDbContext`, `AddControllers`
- "Reference = Intent", "auto-registration", "module not discovered", "boot failure", "service not found"
- Talk of `[Before]` / `[After]` module ordering, or registering app-specific services

## Core principle

**`services.AddKoan()` is the only wiring line.** Framework modules self-register by being referenced (Reference = Intent). For *your* services, author one **`KoanModule`** (ARCH-0086) — a single self-describing unit (id + DI + ordered startup + provenance) that is auto-discovered and ordered, never hand-registered. It implements `IKoanAutoRegistrar`, so existing discovery applies unchanged.

<!-- validate -->
```csharp
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;     // AddNote / AddSetting extensions on ProvenanceModuleWriter
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MyApp.Initialization;

// One self-describing unit: id + DI + ordered startup + provenance. Auto-discovered (source-gen),
// ordered by [Before]/[After]. No registration call anywhere — referencing the assembly is the intent.
public sealed class MyAppModule : KoanModule
{
    public override string Id => "MyApp";

    public override void Register(IServiceCollection services)          // DI wiring (was Initialize)
    {
        services.AddScoped<ITodoService, TodoService>();
        if (KoanEnv.IsProduction)
            services.AddSingleton<IEmailService, SmtpEmailService>();
    }

    public override Task Start(IServiceProvider sp, CancellationToken ct) // one-time ordered startup
        => Task.CompletedTask;

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version, "Application services");
        module.AddSetting("Environment", env.EnvironmentName);

        // Multi-key fallback: setting -> env var -> default.
        var apiKey = Configuration.ReadFirst(cfg, "dev-key", "App:ApiKey", "APP_API_KEY");
        module.AddNote($"ApiKey source resolved (default={apiKey == "dev-key"})");

        if (!cfg.GetSection("Email:Smtp").Exists())
            module.SetStatus("degraded", "Email configuration missing — notifications disabled");
    }
}

public interface ITodoService { }
public sealed class TodoService : ITodoService { }
public interface IEmailService { }
public sealed class SmtpEmailService : IEmailService { }
```

The whole `Program.cs` stays minimal — `AddKoan()` discovers and runs `MyAppModule` for you:

```csharp
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();                 // discovers every module + adapter by reference
var app = builder.Build();
app.Run();                                  // middleware auto-configured by referenced packages
```

## Reference = Intent activation

Adding a package reference *is* the registration — there is no matching `Add*()` call to write.

| Add this reference | Effect |
|---|---|
| `Koan.Core` + `services.AddKoan()` | Boot, discovery, provenance — the only mandatory line |
| `+ Koan.Data.Connector.Mongo` (or Postgres/Sqlite/Redis…) | Data adapter discovered, configured, elected by capability |
| `+ Koan.Web` | Controllers + middleware auto-wired |
| `+ Koan.AI` / `Koan.Jobs` / `Koan.Cache` | Pillar self-registers; no `AddKoanAI()` / `AddKoanJobs()` needed to *enable* it |
| Your assembly with a `KoanModule` / `IKoanAutoRegistrar` | Discovered + ordered automatically |

## Authoring choices

- **`KoanModule` (preferred, ARCH-0086)** — `Id`, `Register(services)`, `Start(sp, ct)` (one-time ordered startup, DI available), `Report(module, cfg, env)`. `Start` folds the "register a bootstrap `IHostedService` inside `Initialize`" idiom into one verb. Recurring/pokable work stays on the `IKoanBackgroundService` family.
- **Raw `IKoanAutoRegistrar`** — `ModuleName`, `ModuleVersion`, `Initialize(services)`, `Describe(ProvenanceModuleWriter, IConfiguration, IHostEnvironment)`. Still works (the module bridges to it); migrate opportunistically.
- **Many implementers of one contract** — mark the *interface* `[KoanDiscoverable]` and read `KoanRegistry.GetDiscoveredImplementors(typeof(IMyPlugin))`. Never hand-roll an `AppDomain.GetAssemblies()` scan (it misses lazily-loaded assemblies).

## Boot report: ProvenanceModuleWriter

`Report`/`Describe` receives a `ProvenanceModuleWriter`. There is **no `BootReport` type** and **no `AddModule`/`AddWarning`**.

| Member | Purpose |
|---|---|
| `module.Describe(version, description?)` | Set module identity (instance, fluent) |
| `module.SetStatus(status, detail?)` | Operational status: `"ok"` / `"degraded"` / `"error"` (this is how you flag non-fatal config issues) |
| `module.AddSetting(key, value)` | Structured setting entry (extension; `Koan.Core.Hosting.Bootstrap`) |
| `module.AddNote(message)` | Plain note (extension; same namespace) |
| `module.SetSetting(key, b => …)` / `module.SetNote(key, b => …)` | Builder forms with source/secret/state tracking |
| `module.AddTool(name, route, description?)` | Register an exposed tool/endpoint |

## Environment & configuration helpers

```csharp
if (KoanEnv.IsDevelopment) { /* dev seed */ }
if (KoanEnv.InContainer)   { /* container tuning */ }
KoanEnv.DumpSnapshot(logger);                          // boot snapshot to a logger

var key = Configuration.Read(cfg, "App:ApiKey", "dev-key");                // single key + default
var any = Configuration.ReadFirst(cfg, "dev-key", "App:ApiKey", "APP_API_KEY"); // first non-null across keys
```

Use `KoanEnv.EnvironmentName` (not a fictional `CurrentEnvironment`). For multi-key fallbacks use `ReadFirst` — `Read` takes exactly one key plus a default.

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `services.AddScoped<IFooRepo, FooRepo>()` for data access | `Entity<T>` static facade — no repository to register (Reference = Intent) |
| `AddDbContext` / `AddControllers` / `AddHostedService` in `Program.cs` | Move into a `KoanModule.Register`; controllers/middleware auto-wire via `Koan.Web` |
| `AddKoan()` followed by `AddKoanData()` / `AddKoanWeb()` / `AddKoanAI()` | Single `AddKoan()` already discovers every module |
| `Describe(BootReport report)` / `report.AddModule(...)` / `report.AddWarning(...)` | `Report/Describe(ProvenanceModuleWriter, ...)`; `module.Describe(...)` + `module.SetStatus("degraded", detail)` |
| `Configuration.Read(cfg, default, "A", "B")` (multi-key) | `Configuration.ReadFirst(cfg, default, "A", "B")` — `Read` is single-key |
| `KoanEnv.CurrentEnvironment` | `KoanEnv.EnvironmentName` |
| Hand-rolled `AppDomain.GetAssemblies()` plug-in scan | `[KoanDiscoverable]` on the interface + `KoanRegistry.GetDiscoveredImplementors(...)` |
| Implementing `IKoanAutoRegistrar` for new app modules | Extend `KoanModule` (ARCH-0086) — id + DI + `Start` + `Report` in one unit |

## Escape hatches

- **Need ordering vs another module?** Annotate the module with `[Before(typeof(OtherModule))]` / `[After(...)]` — discovery topologically sorts; never sequence by hand in `Program.cs`.
- **Conditional registration** — branch on `KoanEnv.*` or `cfg.GetValue<bool>(...)` inside `Register`. (Reading config there needs a built provider; prefer feature flags resolved at use-site.)
- **Truly framework-foreign service** that must run before the container is built — that work belongs in `Register`, not `Start`.
- **Raw `IKoanAutoRegistrar`** remains supported when you can't take the `KoanModule` base (e.g. an existing class hierarchy).

## See also

- [Reference card: data.md](../../../docs/reference/cards/data.md) — pillar map (Entity facade the bootstrap exposes)
- [Bootstrap lifecycle deep-dive](../../../docs/guides/deep-dive/bootstrap-lifecycle.md) — discovery, ordering, provenance
- [Framework utilities guide](../../../docs/guides/framework-utilities.md) — `Configuration`, `KoanEnv`, options helpers
- [Sample: S0.ConsoleJsonRepo/Program.cs](../../../samples/S0.ConsoleJsonRepo/Program.cs) — minimal bootstrap
- [Sample: S8.Canon registrar](../../../samples/S8.Canon/Initialization/KoanAutoRegistrar.cs) — canonical `Describe` with `ProvenanceModuleWriter`
- [ARCH-0086 — KoanModule](../../../docs/decisions/ARCH-0086-koan-module.md) — the module primitive
