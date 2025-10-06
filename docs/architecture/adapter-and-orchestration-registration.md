# Adapter and Orchestration Registration Standards

> **Contract**
> - **Inputs:** Koan modules that expose adapters or orchestration capabilities, their `KoanAutoRegistrar` implementations, and downstream samples consuming them.
> - **Outputs:** A consistent self-registration standard, checklists for new modules, and an audit log of gaps with remediation guidance.
> - **Failure modes:** Modules requiring manual DI wiring, duplicated controller/configuration setup in samples, or adapters that misreport capabilities because their bootstrap never executes.
> - **Success criteria:** Referencing a Koan adapter/orchestration package wires the expected services without host intervention, diagnostics accurately describe provider election, and samples demonstrate the canonical `.AddKoan()` entry point.

## Edge Cases

1. Applications referencing an adapter package without also pulling in the base pillar (for example, cache adapter without `Koan.Cache`).
2. Containers that resolve configuration late; option binding must succeed even when `IConfiguration` is provided by the host after auto-registration executes.
3. Multiple adapters for the same capability referenced together (memory + redis cache, several data connectors) requiring deterministic provider election.
4. Samples that customize MVC or Swagger manually, bypassing Koan’s pipeline and creating divergent behaviour.
5. Tooling assemblies (CLI, analyzers) that should *not* register runtime services but still need to document the exception explicitly.

---

## Adapter Self-Registration Pattern

Every adapter package (data, cache, web, messaging, secrets) must provide a `KoanAutoRegistrar` that:

- Invokes the pillar’s first-class registration (`services.AddKoanData…`, `services.AddKoanCache()`, etc.) or the adapter hook (`services.AddKoanCacheAdapter("redis")`) exactly once, relying on idempotent extension methods.
- Binds options via `services.AddKoanOptions<T>(Constants.Configuration.Section)` so late-bound configuration still applies without building a temporary `ServiceProvider`.
- Uses `services.TryAdd`/`TryAddEnumerable` for extensibility and avoids `services.BuildServiceProvider()` inside `Initialize` (no premature container builds).
- Emits boot telemetry with `report.AddModule`, `report.AddProviderElection`, and stable constants so the host understands which provider won.
- Leaves diagnostics (health contributors, capability registries) enabled by default to respect “reference = intent”.

### Reference Snippet

```csharp
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Cache.Adapter.Redis";
    public void Initialize(IServiceCollection services) => services.AddKoanCacheAdapter("redis");
    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        report.AddProviderElection("CacheStore", "redis", new[] { "memory", "redis", "custom" }, "Reference = adapter package");
        report.AddSetting("RedisConfiguration", Configuration.Read(cfg, CacheConstants.Configuration.Redis.Configuration, "auto") ?? "auto");
    }
}
```

## Orchestration Module Pattern

Runtime orchestration packages (`Koan.Orchestration.Aspire`, data connector orchestrators, etc.) must:

- Detect their execution mode via `KoanEnv` and register the matching dependency orchestration services.
- Contribute configuration providers or hosted services without demanding the host add them manually.
- Report the elected orchestration mode and networking strategy via `BootReport` so diagnostics explain how dependencies will be launched.
- Document tooling-only assemblies (`Koan.Orchestration.Cli`, `Koan.Orchestration.Generators`) as explicit exceptions—no runtime registration, but they must remain silent during composition.

## Sample Expectations

Samples should showcase the canonical Koan experience:

- Startup code should rely on `builder.Services.AddKoan()` (optionally fluent variants like `.AsWebApi()`) and avoid manual `AddControllers()`, `AddSwaggerGen()`, or ad-hoc routing when the referenced Koan modules already encapsulate that behaviour.
- Customisation (e.g., bespoke authorization policies) belongs in targeted extension points rather than re-adding MVC or Swagger from scratch.
- Compose additional services via Koan-provided helpers (`AddKoanSwagger`, `AddKoanObservability`) only when the auto-registrar cannot infer the intent.

---

## Audit Summary (October 2025)

| Scope | Status | Issue | Recommended Action |
| --- | --- | --- | --- |
| **Koan.Cache** | ✅ Fixed | Core cache pillar lacked a `KoanAutoRegistrar`; hosts had to call `services.AddKoanCache()` manually. | New `Koan.Cache.Initialization.KoanAutoRegistrar` now invokes `AddKoanCache()` and reports provider diagnostics. |
| **Koan.Cache.Adapter.Memory** | ✅ Fixed | Memory adapter depended on host code to call `AddKoanCacheAdapter("memory")`. | Auto-registrar added; also records tag index configuration in boot report. |
| **Koan.Cache.Adapter.Redis** | ✅ Fixed | Redis adapter required manual registration and logged via transient ServiceProvider. | Auto-registrar added; connection telemetry emitted without building a container during init. |
| **Data connectors (JSON, InMemory, Redis, Weaviate)** | ✅ Fixed | Registrars were building temporary `ServiceProvider` instances just for logging. | Removed premature `BuildServiceProvider` calls; logging now occurs via BootReport or scoped loggers. |
| **Samples – `S7.TechDocs`, `S8.Location.Api`, `KoanAspireIntegration`** | ⚠️ Pending | Manually call `AddControllers()`/Swagger despite referencing `Koan.Web` and `Koan.Web.Connector.Swagger`. Duplicates pipeline setup and diverges from guidance. | Replace manual MVC/Swagger wiring with module fluents (`AddKoan().AsWebApi()`, rely on auto-registrars) and relocate custom policies into Koan authorization helpers. |
| **Samples – authorization wiring** | ⚠️ Pending | `S7.TechDocs` performs direct `AddAuthorization`/policy wiring inside Program. Needs review once Koan.Web exposes a dedicated capabilities mapper. | Move policy definitions into module-specific auto-registrar or use `AddCapabilityAuthorization` only; review upcoming Koan.Web authorization guidance. |
| **Orchestration modules (CLI / Generators)** | ℹ️ Intentional | No `KoanAutoRegistrar` because assemblies are tooling/analyzers. | Documented here; no runtime action required. |

---

## Recommended Fixes

1. **Samples alignment** – Update affected samples to rely on Koan’s fluent registration (`AddKoan().AsWebApi()`, `AddKoanSwagger(...)` only when overriding defaults) and delete redundant MVC/Swagger wiring.
2. **Authorization defaults** – Extend Koan.Web to expose reusable policy builders so samples stop re-implementing authorization scaffolding in `Program.cs`.
3. **Adapter checklist automation** – Add validation in `docs-lint` to flag `KoanAutoRegistrar` implementations that reference `BuildServiceProvider()` or omit `report.AddModule`.
4. **Orchestration documentation** – Capture explicit guidance for tooling assemblies (CLI, analyzers) clarifying why they do not register runtime services, preventing future regressions.

---

## Developer Checklist (New Modules)

- [ ] Provide `KoanAutoRegistrar` that calls the pillar’s `AddKoan…` extension and binds options via `AddKoanOptions`.
- [ ] Avoid constructing a temporary `ServiceProvider`; defer logging to BootReport or per-request loggers.
- [ ] Register health contributors and discovery adapters with `TryAddEnumerable` so multiple packages can coexist.
- [ ] Document provider elections and configuration in `BootReport`.
- [ ] Update `/docs/toc.yml` and relevant pillar guides when adding new adapters or orchestration capabilities.

Follow this standard to keep Koan’s “reference = intent” promise intact across adapters, orchestration layers, and representative samples.