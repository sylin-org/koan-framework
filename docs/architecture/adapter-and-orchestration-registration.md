# Adapter and Orchestration Registration Standards

> **Contract**
>
> - **Inputs:** Koan modules that expose adapters or orchestration capabilities, plus downstream samples consuming them.
> - **Outputs:** A consistent self-registration standard, checklists for new modules, and an audit log of gaps with remediation guidance.
> - **Failure modes:** Modules requiring manual DI wiring, duplicated controller/configuration setup in samples, or adapters that misreport capabilities because their bootstrap never executes.
> - **Success criteria:** Referencing a Koan adapter/orchestration package wires the expected services without host intervention, diagnostics accurately describe provider election, and samples demonstrate the canonical `.AddKoan()` entry point.

## Edge Cases

1. Applications referencing an adapter package without also pulling in the base pillar (for example, cache adapter without `Koan.Cache`).
2. Containers that resolve configuration late; option binding must succeed even when `IConfiguration` is provided by the host after auto-registration executes.
3. Multiple adapters for the same capability referenced together (memory + redis cache, several data connectors) requiring deterministic provider election.
4. Samples that customize MVC or Swagger manually, bypassing Koan’s pipeline and creating divergent behaviour.
5. Tooling assemblies (CLI, analyzers) that should _not_ register runtime services but still need to document the exception explicitly.
6. Connectors that ship executable hosts inside `src/Connectors/**` (for example, inbox Redis worker) instead of a reusable module; treat them as samples or break them into module + host.

---

## Adapter Self-Registration Pattern

Every functional adapter package (data, cache, web, communication, storage) must provide exactly one domain-named `KoanModule` that:

- Registers only its adapter contracts through standard .NET DI. The functional pillar owns its own module and arrives transitively; applications retain one `AddKoan()` call.
- Binds options via `services.AddKoanOptions<T>(Constants.Configuration.Section)` so late-bound configuration still applies without building a temporary `ServiceProvider`.
- Registers discovery + orchestration bridges (`IServiceDiscoveryAdapter`, `IKoanOrchestrationEvaluator`) using `TryAddEnumerable` so multiple adapters can coexist. If the same module contributes Aspire resources, it also implements `IKoanAspireResources`.
- Uses `services.TryAdd`/`TryAddEnumerable` for extensibility and avoids `services.BuildServiceProvider()` inside `Register` (no premature container builds).
- Reports provenance through `Report(...)` and resolved runtime evidence through `ReportComposition(...)` so the host explains which provider won and how it will connect.
- Leaves diagnostics (health contributors, readiness monitors, capability registries) enabled by default to respect “reference = intent”.

### Reference Snippet

```csharp
public sealed class RedisDataModule : KoanModule, IKoanAspireResources
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<RedisOptions>(Infrastructure.Constants.Configuration.Keys.Section);
        services.AddSingleton<IConfigureOptions<RedisOptions>, RedisOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, RedisHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, RedisDiscoveryAdapter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, RedisOrchestrationEvaluator>());
        services.AddSingleton<IDataAdapterFactory, RedisAdapterFactory>();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Redis discovery is handled by RedisDiscoveryAdapter");
        module.AddSetting("ConnectionString", "auto (resolved by discovery)");
    }

    public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration configuration, IHostEnvironment environment)
    {
        // wire Aspire resources when this capability also participates in an AppHost
    }
}
```

Identity is derived from standard `PackageId`/assembly metadata. Do not add a custom module ID, descriptor,
activation attribute, or project-reference mode. Cross-module contracts belong in an isolated Core/Contracts/
Abstractions package so referencing vocabulary never activates the functional implementation.

#### Cache adapter convergence

- Cache adapters append `ICacheStore` with standard two-generic `TryAddEnumerable`; no Cache-specific registration helper or analyzer exists.
- Providers declare stable identity, Local/Remote placement, truthful `CacheCaps`, and optional `[ProviderPriority]`.
- One host-level `CacheTopology` compiles candidates, capabilities, pins, election receipts, and stable ties once.
- Redis adapters reference `Sylin.Koan.Redis`: one backend-owned endpoint/discovery/pool lifecycle. Cache and Data keep
  only their pillar semantics, and AppHost contributors reference the inert Aspire contract rather than its runtime.

## Orchestration Module Pattern

Runtime orchestration packages (`Koan.Orchestration.Aspire`, data connector orchestrators, etc.) must:

- Detect their execution mode via `KoanEnv` and register the matching dependency orchestration services.
- Contribute configuration providers or hosted services without demanding the host add them manually.
- Report the elected orchestration mode and networking strategy via `BootReport` so diagnostics explain how dependencies will be launched.
- Document tooling-only assemblies (`Koan.Orchestration.Cli`, `Koan.Orchestration.Generators`) as explicit exceptions—no runtime registration, but they must remain silent during composition.

## Sample Expectations

Samples should showcase the canonical Koan experience:

- Startup code should rely on `builder.Services.AddKoan()` and avoid manual `AddControllers()`, `AddSwaggerGen()`, or ad-hoc routing when the referenced Koan modules already encapsulate that behaviour. Fluent toggles (for example, `.WithRateLimit()`) remain available for opt-in behaviours.
- Customisation (e.g., bespoke authorization policies) belongs in targeted extension points rather than re-adding MVC or Swagger from scratch.
- Compose additional services via Koan-provided helpers (`AddKoanSwagger`, `AddKoanObservability`) only when the auto-registrar cannot infer the intent.

---

## Audit Summary (2025‑10‑06)

### Confirmed alignments

| Scope                               | Evidence                                                                                                                                                                                                          |
| ----------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Koan.Cache**                      | `CacheModule` owns the runtime; standard-DI candidates compile into one immutable Local/Remote topology with capability and receipt facts.                                                                         |
| **Cache adapters – Memory, SQLite & Redis** | Adapter modules append providers with standard DI and truthful capabilities. Redis consumes the shared backend without activating Data; SQLite and Redis have focused engine evidence.             |
| **Data connectors**                 | Every shipping data adapter exposes one module whose `Register`/`Report` methods bind options, register discovery and health contributors, and avoid temporary service providers.                                |
| **Orchestration host providers**    | Docker, Podman, and Compose expose domain-named modules that register providers, capture engine diagnostics, and participate in boot telemetry; modules with Aspire behavior implement `IKoanAspireResources`.     |
| **Service Inbox Redis module**      | `Koan.Service.Inbox.Connector.Redis` ships as a reusable module with options binding, hosted announcement service, and controller registration surfaced through its module.                                      |
| **Redis backend boundary**          | `Koan.Redis` owns endpoint/discovery/orchestration/pooling; Cache and Data consume it independently and share the default multiplexer when composed.                                                                  |
| **Aspire contributor boundary**     | `IKoanAspireResources` lives in inert `Koan.Orchestration.Aspire.Abstractions`; Redis/Postgres no longer activate the functional Aspire runtime for vocabulary.                                                       |
| **Sample conformance**              | `KoanAspireIntegration`, `S8.Location.Api`, and `S15.RedisInbox` sample all rely on `.AddKoan()` fluents with Koan-provided helpers for Swagger, authorization, and static file handling.  |
| **Authorization helper**            | `Koan.Web.Extensions.Authorization.AddKoanAuthorization` centralises policy registration and capability mapping, letting samples declare role policies without re-wiring MVC or capability infrastructure.        |

### Misalignment register

| Scope                                                                                               | Status      | Issue                                                                                                                                                   | Recommended Action                                                   |
| --------------------------------------------------------------------------------------------------- | ----------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------- |
| **Orchestration host providers** (`Connectors/Orchestration/Docker`, `Podman`, `Renderers/Compose`) | ✅ Resolved | Auto-registrars register the providers, publish engine diagnostics, and surface Aspire resources for zero-touch orchestration selection.                | Continue monitoring for new orchestration connectors to keep parity. |
| **~~Connector – Web GraphQL~~** (`Koan.Web.Connector.GraphQl`)                                          | 🗄️ Attic'd 2026-06 | ~~Auto-registration now invokes `AddKoanGraphQl()`, binds options, and emits boot notes for schema endpoints.~~ Connector cut from `dev` (recoverable at git tag `attic/koan-web-graphql`).                                             | n/a — sole consumer was archived sample S4.                          |
| **Connector – Service Inbox Redis** (`Koan.Service.Inbox.Connector.Redis`)                          | ✅ Resolved | Module exposes options, hosted announcement service, and controller registration via auto-registrar; executable host moved to `samples/S15.RedisInbox`. | Validate future inbox connectors follow the same pattern.            |
| **Cache Redis backend boundary**                                                                     | ✅ Resolved | Cache Redis references the shared Redis backend; Cache-only composition proves no Redis Data provider is activated.                                  | Keep future Redis consumers on the same backend owner. |
| **Samples – `KoanAspireIntegration`, `S8.Location.Api`**                                            | ✅ Resolved | Samples depend on `.AddKoan()` fluents, Koan-provided Swagger/authorization helpers, and no longer re-wire MVC manually.                                | Periodically audit additional samples to prevent regressions.        |

### Recommended fixes

1. **Expand diagnostics coverage** – Extend boot reporting for GraphQL and inbox modules with schema/capability metrics so telemetry stays rich as features grow.
2. **Broaden sample sweep** – Audit the maintained semantic sample portfolio to ensure future changes keep
   `.AddKoan()` alignment and leverage the new authorization helper where applicable.
3. **Author documentation updates** – Capture the new `AddKoanAuthorization` helper and orchestration registrar behaviour in `/docs/guides` so downstream teams adopt the conventions quickly.

---

## Developer Checklist (New Modules)

- [ ] Provide one domain-named `KoanModule` that registers only the module's services through standard DI and binds options via `AddKoanOptions`.
- [ ] Avoid constructing a temporary `ServiceProvider`; defer logging to BootReport or per-request loggers.
- [ ] Register health contributors and discovery adapters with `TryAddEnumerable` so multiple packages can coexist.
- [ ] Document provider elections and configuration in `BootReport`.
- [ ] Update `/docs/toc.yml` and relevant pillar guides when adding new adapters or orchestration capabilities.

Follow this standard to keep Koan’s “reference = intent” promise intact across adapters, orchestration layers, and representative samples.
