# ARCH-0077 Orchestration layer: migrate to .NET Aspire

**Status**: Proposed, 2026-05-15
**Drivers**: Framework strategy, maintenance load, alignment with first-party .NET tooling
**Deciders**: Koan Framework author
**Inputs**: ARCH-0049 (`[KoanService]` unification), `Koan.Orchestration.Generators`, `Koan.Orchestration.Cli.Core`, current pain points around the source-generator pipeline
**Outputs**: Strategic intent to retire the Koan orchestration layer in favour of .NET Aspire; tactical interim measure (ARCH-0077A) narrowing the generator's blast radius

## Context

Koan ships an orchestration layer that:

- Defines `[KoanService]` / `[KoanApp]` attributes capturing each service's container image, default ports, env-var templates, URI patterns, and health-check metadata at the declaration site.
- Runs a Roslyn source generator (`Koan.Orchestration.Generators.OrchestrationManifestGenerator`) over every Koan project to aggregate scattered class-level attributes into an assembly-level `[OrchestrationServiceManifest]` plus a pre-rendered `__KoanOrchestrationManifest.Json` constant.
- Surfaces that metadata to a CLI (`Koan.Orchestration.Cli.Core`) which reads compiled DLLs via `MetadataLoadContext` (no runtime execution) and produces docker-compose / k8s plans.

This is a well-shaped solution to a real problem. It also occupies the **exact** conceptual space that Microsoft has invested in heavily with .NET Aspire (GA November 2024, shipping with .NET 8/9/10): declarative service topology, container-defaults-as-packages, automatic connection-string flow, dashboard for local dev, manifest publishing for deploy.

Three friction signals in the current Koan implementation:

1. **The orchestration generator is auto-injected as an analyzer into every project** in the framework via the root `Directory.Build.props`. Only the two generator projects themselves are excluded. The generator is only useful in ~25 projects (connectors + the lone Translation service + sample apps), but it loads — and must compile cleanly — into every Koan internal library. A self-inflicted RS1035 issue in the generator's source recently blocked consumers from packing `Koan.Web.Auth` even though that project has nothing to do with orchestration.
2. **The generator uses the legacy `ISourceGenerator` API.** Roslyn deprecates `GeneratorExecutionContext` (RS1035). The project declares `<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>` and then violates the enforced rule. Porting to `IIncrementalGenerator` is ~550 lines of restructuring on a code path that will likely be replaced wholesale.
3. **Aspire is the first-party answer** to this problem space. Continuing to invest in the Koan orchestration generator means continuing to maintain a parallel implementation of a moving Microsoft target, with no significant capability advantage.

## Decision

**Strategic direction: Koan's orchestration layer migrates to .NET Aspire.** Koan retires its bespoke `[KoanService]` aggregation + manifest generator + CLI planner and re-packages each connector's orchestration metadata as an Aspire hosting integration.

### Target architecture

Each Koan connector ships **two** complementary integration packages, mirroring Aspire's convention:

| Package | Role |
|---|---|
| `Koan.Aspire.Hosting.<Service>` (e.g. `Koan.Aspire.Hosting.Mongo`) | Aspire `IDistributedApplicationBuilder` extensions: `builder.AddKoanMongo("mongo")`. Owns the container image, default ports, volume conventions, health check, env-var template. This replaces the current `[KoanService]` attribute on `MongoAdapterFactory`. |
| `Koan.Data.Connector.<Service>` (existing) | Client-side DI wireup, Entity\<T\> integration, repository implementation. Reads the connection string Aspire injects via `Microsoft.Extensions.ServiceDiscovery`. Unchanged in shape; loses its `[KoanService]` attribute. |

Apps describe their topology in an AppHost project:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var mongo = builder.AddKoanMongo("mongo").AddDatabase("appdb");
var auth  = builder.AddProject<Projects.Auth_Svc>("auth").WithReference(mongo);
var emp   = builder.AddProject<Projects.App_Svc>("emp").WithReference(mongo).WithReference(auth);

builder.Build().Run();
```

`dotnet run` spins everything up with the Aspire dashboard. `dotnet aspire publish` emits the manifest that `azd`, `aspirate`, or a custom Koan publisher consumes for production deploys.

### What goes away

- `Koan.Orchestration.Generators` (the source generator project)
- The `__KoanOrchestrationManifest.Json` baked-in JSON constant
- `Koan.Orchestration.Cli.Core` planning logic that reads the manifest via `MetadataLoadContext`
- `[KoanService]`, `[ContainerDefaults]`, `[EndpointDefaults]`, `[AppEnvDefaults]`, `[HealthEndpointDefaults]` attributes on factory classes
- `[assembly: KoanApp(...)]` on app projects (the AppHost reference replaces it)
- The per-connector `DiscoveryAdapter` classes that re-implement connection-string sniffing — `Microsoft.Extensions.ServiceDiscovery` covers the generic case; provider-specific health checks stay but move to the Aspire hosting integration

### What stays (and grows)

- Koan's data-access stack (`Entity<T>` statics, the storage profile abstraction, the role attribution layer, the auth event-contributor pipeline from WEB-0065)
- Per-connector client libraries (`Koan.Data.Connector.Mongo`, etc.) — their Repository/AdapterFactory implementations
- The DI registration and IKoanAutoRegistrar pattern
- `Koan.Core.Registry.Generators` (already `IIncrementalGenerator`, harmless, unrelated concern)

### Migration phases

This ADR commits to direction, not timeline. A realistic sequencing:

1. **Now** (this ADR): document the intent. Apply **ARCH-0077A** — narrow the orchestration generator's auto-injection so unrelated projects can build cleanly while the generator continues to function for connector + sample projects that depend on it.
2. **Bridge release** (Koan 0.7.x or 0.8.0): ship `Koan.Aspire.Hosting.*` packages alongside the existing `[KoanService]`-driven connectors. Both wireup paths work; consumers opt into Aspire incrementally.
3. **Deprecation release** (Koan 0.9.0): `[KoanService]` and the Koan orchestration CLI marked obsolete. README and TECHNICAL.md guide users to the Aspire path. New connectors ship Aspire integrations only.
4. **Removal release** (Koan 1.0.0 or later): delete the orchestration generator, the CLI planner, and the legacy attributes. Connector libraries lose their `[KoanService]` declarations.

### ARCH-0077A: tactical scope narrowing (applied now)

Until phase 2 ships, the source generator continues to do its job. We narrow its analyzer injection so it only loads into projects that actually consume it:

- Root `Directory.Build.props` gates the orchestration generator behind `<KoanRequiresOrchestrationGenerator>true</KoanRequiresOrchestrationGenerator>`.
- `src/Connectors/Directory.Build.props` sets the property for every connector under that path.
- `samples/Directory.Build.props` sets the property for sample apps.
- The lone non-connector service that declares `[KoanService]` (`Koan.Service.Translation`) opts in via its own csproj.
- The generator's own csproj sets `<EnforceExtendedAnalyzerRules>false</EnforceExtendedAnalyzerRules>` so its honest declaration that it uses the legacy `ISourceGenerator` API stops being treated as a build error. Modernizing to `IIncrementalGenerator` is deliberately **not** undertaken since the whole pipeline is on the deprecation path.

Net effect: unrelated framework projects (Koan.Web.Auth, Koan.Web.Auth.Roles, Koan.Web.Extensions, Koan.Core, Koan.Data.Core, …) no longer load the orchestration generator at compile time and can no longer be blocked by it.

## Consequences

**Positive**

- Aligns Koan's orchestration surface with first-party Microsoft tooling. Less framework code to maintain; users get the Aspire dashboard, service discovery, OpenTelemetry hooks, and azd/aspirate integration for free.
- Removes a non-trivial maintenance burden (generator + planner + per-connector discovery adapters ≈ several thousand lines).
- Topology becomes plain C# in an AppHost — debuggable, testable, refactorable. No more "why did the generator emit this manifest?" troubleshooting.
- Per ARCH-0077A, unrelated projects stop paying the analyzer load cost and stop being held hostage by generator-project compile errors.

**Negative / Trade-offs**

- Loss of "Koan owns the full stack" framing — apps that adopt Koan also adopt Aspire's AppHost convention. Some teams may push back on the added dependency.
- Aspire is opinionated about local-dev shape (dashboard, containers, project references). Edge cases that today's Koan CLI handles (e.g. non-container deployment targets, custom planners) need Aspire-publisher equivalents or explicit fallbacks.
- The bridge release period (phase 2) has two parallel wireup paths. Doc burden + risk of inconsistency between them.
- Aspire moves at Microsoft's cadence. Koan loses control over the orchestration surface API.

**Reversibility**

ARCH-0077A is fully reversible (revert the Directory.Build.props change and the property opt-ins; the generator works exactly as before). The full Aspire migration is harder to back out of once connectors publish `Koan.Aspire.Hosting.*` packages and consumers depend on them — though the underlying client-side libraries don't change shape, so a worst-case reversal would mean re-introducing the `[KoanService]` attributes on the same factory classes.

## References

- ARCH-0049 — `[KoanService]` attribute unification (introduces the current single-attribute design)
- .NET Aspire docs: https://learn.microsoft.com/dotnet/aspire/
- Aspire hosting integration conventions: https://learn.microsoft.com/dotnet/aspire/community-toolkit/hosting-integrations
- `Microsoft.Extensions.ServiceDiscovery` — first-party service discovery the bridge will lean on
