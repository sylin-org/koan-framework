# Defensive Publication: Self-Initializing Module Infrastructure with Compile-Time Discovery, Runtime Fallback, and Provenance-Tracked Boot Reporting

---

## Header Block

| Field | Value |
|---|---|
| **Title** | Self-Initializing Module Infrastructure with Compile-Time Discovery, Runtime Fallback, and Provenance-Tracked Boot Reporting |
| **Inventor** | Leo Botinelly (Leonardo Milson Botinelly Soares) |
| **Publication Date** | 2026-03-24 |
| **Field of Invention** | Software framework infrastructure; dependency injection and modular application composition in managed runtime environments (.NET / CLR) |
| **Framework** | Koan Framework v0.6.3, targeting .NET 10 |
| **Keywords** | self-initializing modules, auto-registration, source generation, module initializer, dependency injection, NuGet package reference as intent, compile-time service discovery, runtime reflection fallback, provenance registry, boot report, configuration source tracking, background service hierarchy, assembly closure walk, incremental Roslyn generator, immutable provenance snapshots, per-setting source metadata, concurrent type registry |

---

## 1. Problem Statement

Modern application frameworks built on dependency injection (DI) containers require developers to manually wire each module's services into the container during host startup. For an application that consumes ten or twenty independent framework packages -- data connectors, authentication providers, caching layers, AI integrations, background job schedulers -- the startup code accumulates dozens of `services.Add*()` calls, each tightly coupled to a specific package's internal types. This manual wiring is the primary source of three categories of defect:

**Configuration drift.** When a package is added to or removed from a project, the corresponding DI registration must be added or removed in a separate location (typically `Program.cs` or a `Startup` class). Developers routinely add a NuGet reference but forget the registration call, producing runtime `InvalidOperationException` failures that surface only when the missing service is first resolved. Conversely, removing a package reference without removing the registration call produces compile errors that must be diagnosed by tracing the registration back to the deleted package. The gap between "package is referenced" and "package is active" creates a category of bugs that existing frameworks do not eliminate.

**Opaque boot behavior.** Even when all services are correctly registered, developers and operators have no systematic way to inspect, at startup time, which modules are active, what configuration values each module resolved, where each value originated (environment variable, appsettings.json, default), or whether a value was explicitly set versus defaulted. Existing frameworks treat configuration as a flat key-value store and do not track provenance metadata per setting. When a misconfiguration causes a runtime failure, the operator must manually trace the configuration cascade -- environment variables, JSON files, user secrets, command-line arguments -- without framework assistance.

**Background service proliferation without lifecycle governance.** Applications increasingly embed background services (health monitors, periodic cleanup tasks, on-demand command handlers, startup migration runners). These services share common lifecycle concerns -- environment-gated execution (development vs. production vs. testing), priority ordering, configuration binding, health contribution -- yet most frameworks require each service to independently implement these cross-cutting concerns. The absence of a declarative, centralized lifecycle descriptor for background services leads to inconsistent behavior, duplicated guard logic, and services that run in inappropriate environments.

The invention described herein eliminates these three problem categories by establishing a system in which adding a package reference is the sole act required to activate a module's full functionality ("Reference = Intent"), and in which every module self-reports its configuration, tools, and lifecycle metadata into an immutable provenance registry that produces a structured boot report.

---

## 2. Prior Art Summary

### 2.1 Managed Extensibility Framework (MEF) -- Microsoft, 2008

MEF introduced attributed composition (`[Export]`, `[Import]`) for plugin discovery. MEF scans assemblies at runtime for exports and composes an object graph. However: (a) MEF operates outside the standard `IServiceCollection` DI container, creating a parallel composition system; (b) MEF has no compile-time discovery -- all scanning is reflection-based at runtime; (c) MEF provides no provenance tracking or boot reporting; (d) MEF has no concept of background service lifecycle governance. MEF solves plugin extensibility but does not address the configuration-provenance or lifecycle-governance problems.

### 2.2 Autofac Modules -- Autofac Project, 2007-present

Autofac's `Module` abstraction allows packages to encapsulate their registrations in a `Load(ContainerBuilder)` method. This is structurally similar to the initializer pattern but requires explicit module registration: `builder.RegisterModule<MyModule>()`. The developer must still know which modules exist and explicitly register them. Autofac has no source-generation path, no configuration-source tracking, and no provenance registry. Autofac Modules reduce coupling within the registration code but do not eliminate the manual wiring step.

### 2.3 Spring Boot Auto-Configuration -- Pivotal/VMware, 2013-present

Spring Boot's `@EnableAutoConfiguration` mechanism uses `META-INF/spring.factories` (and later `META-INF/spring/org.springframework.boot.autoconfigure.AutoConfiguration.imports`) to declare auto-configuration classes. The Spring Boot starter mechanism is the closest prior art: adding a starter dependency activates its auto-configuration. However: (a) Spring Boot's discovery is purely runtime (classpath scanning or manifest file reading), with no compile-time equivalent; (b) Spring Boot provides `@ConditionalOnClass`, `@ConditionalOnProperty`, and related annotations for conditional activation, but these conditions are evaluated at runtime without source-generated manifests; (c) Spring Boot's Actuator `/env` endpoint exposes configuration values but does not track per-setting source provenance at the granularity described herein (which setting came from which specific environment variable name or configuration key, whether it used a default, and what the source type was); (d) Spring Boot has no integrated background-service hierarchy with declarative attribute-driven lifecycle descriptors.

### 2.4 ASP.NET Core Host Builder -- Microsoft, 2016-present

ASP.NET Core's `WebApplicationBuilder` and `IHostBuilder` provide the `IServiceCollection` DI container and a configuration system (`IConfiguration`) that supports multiple providers (JSON, environment variables, command-line, user secrets). However: (a) service registration remains explicit -- each package must be manually wired via extension methods; (b) while `IConfiguration` supports multiple providers, it does not expose per-key source metadata to consuming code (the `IConfigurationRoot.GetDebugView()` method shows providers but is a debugging tool, not a programmatic API for boot reporting); (c) ASP.NET Core has `IHostedService` and `BackgroundService` for background work, but these are flat -- there is no declarative hierarchy distinguishing periodic, startup, pokable, and health-contributing services, and no environment-gated execution attributes; (d) there is no source-generation integration for service discovery.

### 2.5 .NET Source Generators -- Microsoft, 2020-present

Roslyn incremental source generators (`IIncrementalGenerator`) enable compile-time code generation based on syntax and semantic analysis. The `[ModuleInitializer]` attribute (C# 9 / .NET 5) enables code that runs when an assembly is first loaded. These are platform capabilities, not solutions. No existing framework combines incremental source generation with `[ModuleInitializer]` to populate a concurrent type registry that drives DI registration, with a runtime reflection fallback and a provenance-tracked boot report.

### 2.6 Gap Summary

No prior system combines all of the following in a single coherent mechanism:
1. Compile-time source generation that emits `[ModuleInitializer]`-attributed code to populate a type registry before application code executes.
2. A runtime reflection fallback that populates the same registry for assemblies where source generation was unavailable.
3. A file-system scan for convention-named assemblies (`Koan.*.dll`) that were not statically referenced.
4. A multi-pass transitive assembly closure walk that discovers indirectly referenced assemblies.
5. A provenance registry with immutable snapshots, per-setting source tracking (environment, appsettings, auto-default, custom), and secret redaction.
6. A background service hierarchy with declarative attribute-driven lifecycle descriptors (periodic, startup, pokable, health-contributing) and environment gating.
7. A boot report aggregation system where modules self-describe their settings, tools, notes, and status into a structured, observable registry.

---

## 3. Detailed Description

### 3.1 Architecture Overview

The system comprises six cooperating subsystems:

1. **Type Registry** (`KoanRegistry`) -- A static, AppDomain-scoped, concurrent dictionary ensemble that holds discovered types.
2. **Compile-Time Discovery** (`RegistrySourceGenerator`) -- A Roslyn incremental source generator that emits `[ModuleInitializer]` code to pre-populate the registry.
3. **Runtime Fallback Discovery** (`RegistryManifestLoader`) -- A reflection-based scanner that fills registry gaps for assemblies without source-generated manifests.
4. **Assembly Closure Walker** (`AppBootstrapper`) -- The bootstrap orchestrator that builds a complete assembly graph and triggers discovery.
5. **Provenance Registry** (`ProvenanceRegistry`, `ProvenanceModuleWriter`) -- An immutable-snapshot registry where modules report configuration and capabilities.
6. **Module Self-Description** (`IKoanAutoRegistrar.Describe()`) -- The contract by which each module reports its resolved settings, tools, and notes.

### 3.2 Interface Hierarchy

The system defines a two-level interface hierarchy:

```
IKoanInitializer
  void Initialize(IServiceCollection services)

IKoanAutoRegistrar : IKoanInitializer
  string ModuleName { get; }
  string? ModuleVersion { get; }
  void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
```

`IKoanInitializer` is the minimal contract: a type that can register services into an `IServiceCollection`. `IKoanAutoRegistrar` extends it with identity (name, version) and self-description capability. Any concrete class implementing either interface in any assembly referencing the core framework will be automatically discovered and activated.

### 3.3 Compile-Time Discovery: The Source Generator

The `RegistrySourceGenerator` is a Roslyn `IIncrementalGenerator` that operates during compilation of every assembly that references the core framework. Its operation is as follows:

**Step 1: Syntax filtering.** The generator registers a syntax provider that matches all `ClassDeclarationSyntax` nodes in the compilation.

**Step 2: Semantic resolution.** For each class declaration, the generator resolves the `INamedTypeSymbol` via the semantic model.

**Step 3: Interface matching.** The generator resolves well-known interface symbols by metadata name:
- `Koan.Core.IKoanInitializer`
- `Koan.Core.IKoanAutoRegistrar`
- `Koan.Core.BackgroundServices.IKoanBackgroundService`
- `Koan.Core.BackgroundServices.IKoanPeriodicService`
- `Koan.Core.BackgroundServices.IKoanStartupService`
- `Koan.Core.BackgroundServices.IKoanPokableService`
- `Koan.Core.IHealthContributor`
- `Koan.Core.Orchestration.Abstractions.IServiceDiscoveryAdapter`

For each non-abstract class, the generator checks whether it implements any of these interfaces.

**Step 4: Attribute extraction for background services.** For classes implementing `IKoanBackgroundService`, the generator extracts the `[KoanBackgroundService]` attribute's named arguments: `Enabled`, `ConfigurationSection`, `Lifetime`, `Priority`, `RunInDevelopment`, `RunInProduction`, `RunInTesting`. It also checks interface implementation for `IsPeriodic`, `IsStartup`, `IsPokable`, and `ImplementsHealthContributor`.

**Step 5: Code emission.** The generator emits a source file named `KoanRegistry_{SanitizedAssemblyName}.g.cs` containing a `file static class` with a method decorated with `[System.Runtime.CompilerServices.ModuleInitializer]`. This method calls the batch registration APIs on `KoanRegistry`:

```csharp
// <auto-generated />
namespace Koan.Core.Hosting.Registry;

file static class KoanRegistryModule_Koan_Data_Connector_Postgres
{
    [global::System.Runtime.CompilerServices.ModuleInitializer]
    internal static void RegisterAssembly_Koan_Data_Connector_Postgres()
    {
        KoanRegistry.RegisterInitializers(new Type[] { typeof(KoanAutoRegistrar) });
        KoanRegistry.RegisterAutoRegistrars(new Type[] { typeof(KoanAutoRegistrar) });
        KoanRegistry.RegisterServiceDiscoveryAdapters(
            new KoanRegistry.ServiceDiscoveryAdapterDescriptor[]
            { new(typeof(PostgresDiscoveryAdapter)) });
    }
}
```

The `[ModuleInitializer]` attribute ensures this code executes when the assembly is first loaded by the CLR, before any application code runs. This means the registry is populated as a side effect of assembly loading -- no explicit call is needed.

**Step 6: Infrastructure guard.** The generator checks for the presence of `Koan.Core.Hosting.Registry.KoanRegistry` in the compilation. If the registry type is not reachable (e.g., the assembly does not reference the core framework), no code is emitted. This prevents generation errors in assemblies that happen to contain classes with matching names but no framework dependency.

### 3.4 The Type Registry: KoanRegistry

`KoanRegistry` is a `static partial class` containing four `ConcurrentDictionary<Type, _>` instances:

| Dictionary | Key | Value | Purpose |
|---|---|---|---|
| `_initializerTypes` | `Type` | `byte` (sentinel) | All types implementing `IKoanInitializer` |
| `_autoRegistrarTypes` | `Type` | `byte` (sentinel) | Subset implementing `IKoanAutoRegistrar` |
| `_backgroundServices` | `Type` | `BackgroundServiceDescriptor` | Background service metadata |
| `_serviceDiscoveryAdapters` | `Type` | `ServiceDiscoveryAdapterDescriptor` | Service discovery adapters |

The dictionaries use a custom `TypeEqualityComparer` that delegates to reference equality (`Type` identity). Batch registration methods (`RegisterInitializers`, `RegisterAutoRegistrars`, `RegisterBackgroundServices`, `RegisterServiceDiscoveryAdapters`) accept `IEnumerable<T>` and use `TryAdd` for idempotency. Because `ConcurrentDictionary.TryAdd` is atomic, concurrent calls from multiple `[ModuleInitializer]` methods executing on different threads during assembly loading are safe without external locking.

The `BackgroundServiceDescriptor` is a `readonly record struct` carrying twelve fields:

```
ServiceType, Enabled, ConfigurationSection, Lifetime, Priority,
RunInDevelopment, RunInProduction, RunInTesting,
IsPeriodic, IsStartup, IsPokable, ImplementsHealthContributor
```

### 3.5 Runtime Fallback: RegistryManifestLoader

The `RegistryManifestLoader` is an `internal static` class that provides a reflection-based fallback for assemblies where source generation was unavailable (e.g., assemblies compiled without the generator analyzer reference, or dynamically loaded assemblies).

**Filtering.** The loader only processes assemblies whose name starts with `"Koan."` (ordinal comparison), avoiding reflection over the entire .NET runtime and third-party libraries.

**Type scanning.** For each qualifying assembly, the loader calls `assembly.GetTypes()` wrapped in a `try/catch` for `ReflectionTypeLoadException`. When the exception occurs, the loader recovers the partial type array via `ex.Types.Where(t => t is not null)`, ensuring that a single unloadable type does not prevent discovery of the remaining types.

**Interface matching.** The loader holds cached `Type` references for all seven discoverable interfaces and uses `IsAssignableFrom` checks. For background services, it reconstructs the `BackgroundServiceDescriptor` by reading the `[KoanBackgroundService]` custom attribute via reflection and checking interface implementation for the periodic/startup/pokable/health-contributor flags.

**Registry population.** Discovered types are batched into lists and registered via the same `KoanRegistry.Register*` batch methods used by the source generator. Because `TryAdd` is idempotent, if the source generator already registered a type, the reflection fallback's duplicate registration is harmlessly ignored.

### 3.6 Assembly Closure Walker: AppBootstrapper

`AppBootstrapper.InitializeModules(IServiceCollection services)` is the entry point called during host construction. Its operation proceeds in five phases:

**Phase 1: Seed assemblies.** The bootstrapper collects assemblies from three sources:
- `AppDomain.CurrentDomain.GetAssemblies()` -- all assemblies already loaded.
- `Assembly.GetEntryAssembly()` -- the application's entry point assembly.
- `Assembly.GetExecutingAssembly()` -- the framework core assembly.

Each assembly is added to a `Dictionary<string, Assembly>` keyed by assembly name (case-insensitive). As each assembly is added, it is also registered in an `AssemblyCache` singleton for reuse by other framework components, and `RegistryManifestLoader.PopulateFromAssembly()` is invoked.

**Phase 2: Transitive reference walk.** A loop iterates up to 5 passes. In each pass, for every assembly in the current set, `GetReferencedAssemblies()` is called. For each referenced assembly not yet in the set, `Assembly.Load(assemblyName)` is called, and the loaded assembly is added to the set. If any new assembly was added, the loop continues. The 5-pass guard prevents infinite loops in pathological reference graphs while being sufficient for real-world transitive depth.

**Phase 3: File-system discovery.** The bootstrapper scans `AppContext.BaseDirectory` for files matching the glob `Koan.*.dll`. For each file not already represented in the assembly set, it loads the assembly by name. This catches assemblies that were deployed alongside the application but not statically referenced -- for example, a connector DLL dropped into the output directory by a deployment script.

**Phase 4: Registry consumption.** After all assemblies are discovered and their manifest loaders invoked, the bootstrapper reads the populated registry:
- `KoanRegistry.GetInitializerTypes()` returns all initializer types.
- `KoanRegistry.GetAutoRegistrarTypes()` returns all auto-registrar types.
- `KoanRegistry.GetBackgroundServices()` returns all background service descriptors.
- `KoanRegistry.GetServiceDiscoveryAdapters()` returns all discovery adapter descriptors.

A `RegistrySummarySnapshot` is built containing counts and namespace breakdowns.

**Phase 5: Initializer execution.** For each initializer type (which includes auto-registrars, since `IKoanAutoRegistrar` extends `IKoanInitializer`), the bootstrapper:
1. Skips abstract types.
2. Creates an instance via `Activator.CreateInstance(type)`.
3. Casts to `IKoanInitializer`.
4. Calls `Initialize(IServiceCollection services)`.

Each initializer registers its module's services into the shared `IServiceCollection`. Exceptions are caught and swallowed to prevent a single module's failure from crashing the host.

### 3.7 Configuration Source Tracking: Configuration.ReadWithSource

The `Configuration` static class provides a `ReadWithSource<T>(IConfiguration? cfg, string key, T defaultValue)` method that returns a `ConfigurationValue<T>` record struct:

```
readonly record struct ConfigurationValue<T>(
    T Value,
    BootSettingSource Source,
    string? ResolvedKey,
    bool UsedDefault)
```

The `BootSettingSource` enum has six members: `Unknown`, `Auto`, `AppSettings`, `Environment`, `LaunchKit`, `Custom`.

The resolution algorithm proceeds through a defined priority order:

1. **Environment variables (highest priority).** The method generates four key variants from the normalized key:
   - Double-underscore form: `Koan__ZenGarden__Endpoint` (ASP.NET Core convention)
   - Single-underscore form: `Koan_ZenGarden_Endpoint`
   - Uppercase double-underscore: `KOAN__ZENGARDEN__ENDPOINT`
   - Uppercase single-underscore: `KOAN_ZENGARDEN_ENDPOINT`

   For each variant, `Environment.GetEnvironmentVariable()` is called. The first non-null value that successfully converts to `T` produces a `ConfigurationValue` with `Source = Environment` and `ResolvedKey` set to the matching environment variable name.

2. **IConfiguration providers (medium priority).** If no environment variable matched, the method queries `IConfiguration` using two key forms:
   - Colon-separated: `Koan:ZenGarden:Endpoint`
   - Underscore-separated: `Koan_ZenGarden_Endpoint`

   The first non-null value that converts to `T` produces `Source = AppSettings` with the dotted path as the resolved key.

3. **Default value (lowest priority).** If neither source provided a value, the default is returned with `Source = Auto`, `ResolvedKey = null`, and `UsedDefault = true`.

The `ReadFirstWithSource<T>` variant accepts multiple keys and returns the first non-default match, enabling fallback key chains (e.g., `ConnectionStrings:Postgres` falling back to `ConnectionStrings:Default`).

Type conversion supports `string`, `bool` (with extended parsing: "1", "yes", "y", "on", "true" and their negatives), `int`, `double`, `TimeSpan`, enums, and a fallback to `Convert.ChangeType`.

### 3.8 Provenance Registry and Immutable Snapshots

The `ProvenanceRegistry` is a thread-safe singleton that maintains mutable internal state behind a `lock` gate and produces immutable `ProvenanceSnapshot` records on every mutation.

**Internal state hierarchy:**
- `PillarState` -- a top-level organizational category (e.g., "data", "web", "ai"), containing:
  - `Dictionary<string, ModuleState>` -- modules within the pillar.
- `ModuleState` -- a specific framework module (e.g., "Koan.Data.Connector.Postgres"), containing:
  - `Dictionary<string, SettingState>` -- configuration settings with provenance metadata.
  - `Dictionary<string, ToolState>` -- tools/endpoints exposed by the module.
  - `Dictionary<string, NoteState>` -- informational notes (info, warning).

**Immutable snapshot production.** Every mutation (setting added, tool registered, note written, status changed) triggers `UpdateSnapshotLocked()`, which:
1. Increments a monotonic sequence number.
2. Generates a GUID v7 (time-ordered) for the snapshot.
3. Captures `DateTimeOffset.UtcNow`.
4. Deep-copies all pillar/module/setting/tool/note state into immutable record types.
5. Replaces the `_snapshot` field atomically.
6. Fires a `SnapshotUpdated` event with the new snapshot.

The `ProvenanceSnapshot` record is fully immutable and safe to read from any thread without locking.

**Setting provenance.** Each `SettingState` carries:
- `Key`, `Label`, `Description` -- identity and documentation.
- `Value` -- the resolved value (redacted if secret).
- `IsSecret` -- whether the value is sensitive.
- `Source` -- `ProvenanceSettingSource` enum (Unknown, AppSettings, Environment, Auto, Custom).
- `SourceKey` -- the exact key that resolved the value (e.g., `"KOAN__ZENGARDEN__ENDPOINT"`).
- `Consumers` -- list of components that depend on this setting.
- `State` -- `ProvenanceSettingState` (Unknown, Active, Defaulted, Missing, Error).

**Secret redaction.** When a setting is marked as secret, the `ProvenanceSettingBuilder` applies either a custom redactor function or a default de-identification function (`Redaction.DeIdentify`), ensuring that sensitive values never appear in boot reports or snapshots.

### 3.9 Module Self-Description: The Describe Protocol

After all services are registered (Phase 5 of AppBootstrapper), the `AppRuntime.Discover()` method is called. For each `IKoanAutoRegistrar` type in the registry:

1. An instance is created via `Activator.CreateInstance`.
2. `ProvenanceRegistry.GetOrCreateModule(pillarCode, moduleName)` returns a `ProvenanceModuleWriter`.
3. `registrar.Describe(module, cfg, env)` is called.

Inside `Describe`, the module uses `Configuration.ReadWithSource` to resolve each of its configuration values, then reports them to the `ProvenanceModuleWriter`:

```
module.SetSetting("Endpoint", builder => builder
    .Value(endpoint.Value)
    .Source(MapSource(endpoint.Source), endpoint.ResolvedKey)
    .Label("Moss Endpoint")
    .Description("The endpoint of the Zen Garden Moss server"));

module.SetTool("Topology", builder => builder
    .Route("/api/v1/garden/topology")
    .Description("Remote garden topology endpoint")
    .Capability("zen-garden.topology"));

module.SetNote("discovery-mode", builder => builder
    .Message("PostgreSQL discovery handled by autonomous PostgresDiscoveryAdapter")
    .Kind(ProvenanceNoteKind.Info));
```

The `ProvenanceModuleWriter` is a fluent builder that delegates all mutations to the `ProvenanceRegistry` singleton, which updates internal state and produces a new immutable snapshot.

### 3.10 Pillar Auto-Resolution

The provenance system organizes modules into "pillars" (logical groupings such as data, web, AI, orchestration). When a module name like `"Koan.Data.Connector.Postgres"` is registered, the `KoanPillarCatalog` attempts to resolve the pillar from the module name prefix. If no explicit pillar code is provided, the catalog matches by namespace convention. If no catalog entry exists, a fallback descriptor is created dynamically with a generated label and default styling.

### 3.11 Background Service Hierarchy

The system defines a four-level interface hierarchy for background services:

```
IKoanBackgroundService (base)
  string Name { get; }
  Task Execute(CancellationToken ct)
  Task<bool> IsReady(CancellationToken ct)  // default: true

IKoanPeriodicService : IKoanBackgroundService
  TimeSpan Period { get; }
  TimeSpan InitialDelay { get; }  // default: Zero
  bool RunOnStartup { get; }      // default: false

IKoanStartupService : IKoanBackgroundService
  int StartupOrder { get; }

IKoanPokableService : IKoanBackgroundService
  Task HandleCommand(ServiceCommand cmd, CancellationToken ct)
  IReadOnlyCollection<Type> SupportedCommands { get; }
```

The `[KoanBackgroundService]` attribute provides declarative lifecycle metadata:
- `Enabled` -- master toggle.
- `ConfigurationSection` -- IConfiguration section for runtime options.
- `Lifetime` -- DI lifetime (Singleton, Scoped, Transient).
- `Priority` -- execution priority (lower = earlier).
- `RunInDevelopment`, `RunInProduction`, `RunInTesting` -- environment gates.

The source generator extracts all of these at compile time into a `BackgroundServiceDescriptor` that is registered in the `KoanRegistry`. At runtime, the background service host reads these descriptors to determine which services to start, in what order, and in which environments.

### 3.12 Boot Report Aggregation

After all modules have described themselves, `AppRuntime.Discover()` reads the `ProvenanceRegistry.CurrentSnapshot` and builds a structured startup overview block containing:
- Framework version (resolved from the `Koan.Core` module version).
- Host type (ASP.NET Core vs. Generic Host).
- Environment snapshot from `KoanEnv`.
- Module list with versions.
- Registry summary (initializer count by namespace, auto-registrar count, background service count by type, discovery adapter count).
- Health snapshot (if available).
- Startup timeline phases with timestamps.

The boot report is emitted via `ILogger` (if available) or `Console.Write` (in non-production environments), providing operators with a single, comprehensive view of the application's composition and configuration at startup.

### 3.13 Startup Timeline

`KoanStartupTimeline` tracks named stages (`BootstrapStart`, `DataReady`, `ConfigReady`) with timestamps. The timeline is collected after provenance and emitted as part of the boot report, enabling performance diagnosis of the startup sequence.

### 3.14 Assembly Cache

The `AssemblyCache` singleton stores all discovered assemblies for reuse by other framework components that need to perform type scanning (e.g., entity discovery, controller registration). This avoids redundant `AppDomain.GetAssemblies()` calls and ensures consistent assembly sets across the framework.

### 3.15 End-to-End Lifecycle Summary

The complete lifecycle, from NuGet package addition to boot report emission, is:

1. **Developer adds a NuGet package reference** (e.g., `Koan.Data.Connector.Postgres`).
2. **Compilation triggers the source generator**, which emits `KoanRegistry_Koan_Data_Connector_Postgres.g.cs` with a `[ModuleInitializer]` method.
3. **Application starts. CLR loads assemblies.** As each Koan assembly loads, its `[ModuleInitializer]` fires, populating `KoanRegistry` with the assembly's discoverable types.
4. **Host builder calls `AppBootstrapper.InitializeModules(services)`.** The bootstrapper walks the assembly closure, invokes `RegistryManifestLoader` as a fallback, and scans the file system for additional `Koan.*.dll` files.
5. **Bootstrapper iterates `KoanRegistry.GetInitializerTypes()`** and calls `Initialize(services)` on each, registering all module services into the DI container.
6. **Host is built. `AppRuntime.Discover()` is called.** It iterates `KoanRegistry.GetAutoRegistrarTypes()` and calls `Describe(module, cfg, env)` on each, populating the `ProvenanceRegistry`.
7. **Boot report is emitted**, showing all active modules, their resolved configuration (with source provenance), exposed tools, notes, background service inventory, and health status.

No line of code in the application's `Program.cs` references any specific module. The package reference is the intent; the infrastructure handles everything else.

---

## 4. Claims-Style Disclosure

The following numbered disclosures describe the inventive mechanisms of this system. Each disclosure is placed in the public domain to prevent future patent claims on these techniques.

**Disclosure 1.** A method for automatic dependency injection registration in a managed runtime environment, comprising: (a) a Roslyn incremental source generator that, at compile time, scans all class declarations in a compilation for implementations of predetermined service interfaces; (b) emission of a source file containing a static method decorated with a `[ModuleInitializer]` attribute that, when the assembly is loaded by the runtime, calls batch registration methods on a static concurrent type registry; (c) whereby the act of adding a package reference to a project causes the package's services to be registered in the application's dependency injection container without any explicit registration code in the application.

**Disclosure 2.** A three-layer type discovery system comprising: (a) a compile-time layer using Roslyn source generation with `[ModuleInitializer]` emission; (b) a runtime reflection layer that scans assembly types with `ReflectionTypeLoadException` recovery, filtering to convention-named assemblies; (c) a file-system layer that scans the application's base directory for convention-named DLL files and loads them dynamically; wherein all three layers populate the same concurrent type registry using idempotent `TryAdd` operations, and wherein layer (b) acts as a fallback for assemblies where layer (a) was unavailable, and layer (c) discovers assemblies that were neither statically referenced nor previously loaded.

**Disclosure 3.** A multi-pass transitive assembly closure walker that: (a) seeds from `AppDomain.CurrentDomain.GetAssemblies()`, the entry assembly, and the executing assembly; (b) iterates up to a configurable pass limit (5 passes), in each pass calling `GetReferencedAssemblies()` on every known assembly and loading newly discovered references; (c) terminates when no new assemblies are discovered or the pass limit is reached; (d) invokes a manifest loader on each newly discovered assembly to populate a type registry; wherein the walker ensures that indirectly referenced framework assemblies are discovered even when the runtime has not yet loaded them.

**Disclosure 4.** A configuration resolution method that returns per-setting source metadata, comprising: (a) a resolution cascade that checks environment variables (in four naming-convention variants), then `IConfiguration` providers, then a default value; (b) a return type that includes the resolved value, a source enum indicating which layer provided the value (`Environment`, `AppSettings`, `Auto`), the exact key that matched, and a boolean indicating whether the default was used; (c) extended boolean parsing that accepts "1", "yes", "y", "on", "true" and their negatives; (d) a multi-key variant that accepts an ordered list of fallback keys and returns the first non-default match with its source metadata.

**Disclosure 5.** A provenance registry system comprising: (a) a thread-safe singleton with internal mutable state organized hierarchically (pillars containing modules containing settings, tools, and notes); (b) an immutable snapshot mechanism that, on every mutation, creates a new `ProvenanceSnapshot` record with a monotonic sequence number, a time-ordered GUID (v7), a UTC timestamp, and deep-copied immutable representations of all state; (c) a `SnapshotUpdated` event that notifies observers of each new snapshot; (d) a `ProvenanceModuleWriter` builder interface through which modules report their configuration, tools, and notes; (e) automatic secret redaction via a configurable redactor function or a default de-identification algorithm.

**Disclosure 6.** A module self-description protocol comprising: (a) an `IKoanAutoRegistrar` interface with `ModuleName`, `ModuleVersion`, and a `Describe(ProvenanceModuleWriter, IConfiguration, IHostEnvironment)` method; (b) a post-registration lifecycle phase where the runtime instantiates each auto-registrar and calls `Describe`, passing a `ProvenanceModuleWriter` scoped to the module's pillar and name; (c) within `Describe`, modules use `Configuration.ReadWithSource` to resolve each configuration value and report it to the writer with full source provenance; (d) modules also report tools (named endpoints with routes, descriptions, and capability identifiers) and notes (informational messages with severity kinds).

**Disclosure 7.** A background service hierarchy comprising: (a) a base interface (`IKoanBackgroundService`) with `Name`, `Execute`, and `IsReady` members; (b) three specialization interfaces: `IKoanPeriodicService` (with `Period`, `InitialDelay`, `RunOnStartup`), `IKoanStartupService` (with `StartupOrder`), and `IKoanPokableService` (with `HandleCommand` and `SupportedCommands`); (c) a declarative `[KoanBackgroundService]` attribute carrying `Enabled`, `ConfigurationSection`, `Lifetime`, `Priority`, `RunInDevelopment`, `RunInProduction`, `RunInTesting` properties; (d) compile-time extraction of attribute values and interface implementations into a `BackgroundServiceDescriptor` record struct by the source generator; (e) runtime environment gating that starts or suppresses services based on the current host environment and the descriptor's flags.

**Disclosure 8.** A source generator that produces per-assembly manifest files with compile-time-extracted background service metadata, wherein the generator: (a) resolves `[KoanBackgroundService]` attribute named arguments via the Roslyn semantic model; (b) checks interface implementation for `IKoanPeriodicService`, `IKoanStartupService`, `IKoanPokableService`, and `IHealthContributor`; (c) emits a `BackgroundServiceDescriptor` literal in the generated `[ModuleInitializer]` method with all twelve fields populated at compile time; (d) thereby enabling the runtime to read background service lifecycle metadata without runtime reflection.

**Disclosure 9.** A pillar auto-resolution mechanism for provenance organization, comprising: (a) a `KoanPillarCatalog` that maps pillar codes to descriptors (code, label, color, icon); (b) a module-name-to-pillar matching algorithm that infers the pillar from the module's namespace prefix; (c) dynamic fallback pillar creation for modules that do not match any catalog entry; (d) whereby modules are automatically organized into logical groupings in the boot report without explicit pillar assignment.

**Disclosure 10.** A boot report aggregation system comprising: (a) collection of module provenance snapshots after all modules have self-described; (b) extraction of a framework version from the core module's self-reported version; (c) host type detection (ASP.NET Core vs. Generic Host) via runtime type resolution of `IWebHostEnvironment`; (d) registry summary construction with initializer counts broken down by namespace, background service counts by type (startup, periodic, general), and discovery adapter counts; (e) health snapshot integration from an `IHealthAggregator`; (f) startup timeline phase tracking with named stages and timestamps; (g) formatted output via `ILogger` or `Console.Write` based on environment.

**Disclosure 11.** A concurrent type registry using `ConcurrentDictionary<Type, _>` with a custom `TypeEqualityComparer` that uses reference equality, wherein: (a) the registry is `static` and AppDomain-scoped; (b) registration is idempotent via `TryAdd`; (c) the registry is populated concurrently from multiple `[ModuleInitializer]` methods executing during assembly loading; (d) the registry exposes snapshot arrays via `Keys.ToArray()` / `Values.ToArray()` for safe iteration by consumers; (e) a `ResetForTesting` method clears all dictionaries for test isolation.

**Disclosure 12.** An assembly classification and summary system within the bootstrap process, comprising: (a) classification of loaded assemblies into categories (koan, telemetry, aspnet, coreclr, thirdParty) by name prefix; (b) counting assemblies per category; (c) identification of "discovered" assemblies (loaded from file-system scan vs. already present); (d) emission of both human-readable and JSON-structured assembly scan summaries; (e) optional verbose mode (controlled by `KOAN_VERBOSE_ASSEMBLIES=1`) that lists every assembly with version, location, and `AssemblyLoadContext` name.

**Disclosure 13.** A generated code containment strategy using C# `file`-scoped static classes, wherein: (a) the source generator emits `file static class KoanRegistryModule_{AssemblyName}` declarations; (b) the `file` modifier restricts visibility to the generated source file, preventing name collisions across assemblies; (c) the sanitization function replaces non-alphanumeric characters with underscores and prepends an underscore if the name begins with a digit; (d) this enables multiple assemblies to each have a `[ModuleInitializer]` method in the same namespace without conflict.

**Disclosure 14.** A reflection-based fallback discovery mechanism with graceful degradation, comprising: (a) an assembly-level filter that only processes assemblies whose name starts with a framework convention prefix; (b) a `ReflectionTypeLoadException` handler that extracts the successfully loaded types from the exception's `Types` array; (c) cached `Type` references for all discoverable interfaces to avoid repeated `typeof` resolution; (d) reconstruction of `BackgroundServiceDescriptor` records from custom attribute reflection data and interface assignability checks; (e) batch registration into the same concurrent registry used by the source generator, ensuring consistent behavior regardless of discovery path.

**Disclosure 15.** A "Reference = Intent" activation model for a modular framework, wherein: (a) adding a NuGet package reference to a project is the sole developer action required to activate the package's functionality; (b) the package's compile-time source generator ensures its types are registered in the central type registry before application code executes; (c) the bootstrapper's initialization phase instantiates the package's `IKoanInitializer` and calls `Initialize(IServiceCollection)`, registering all required DI services; (d) the runtime's provenance phase instantiates the package's `IKoanAutoRegistrar` and calls `Describe`, adding the module's configuration and capabilities to the boot report; (e) no modification to the application's startup code (`Program.cs` or equivalent) is required at any step; (f) removing the package reference is the sole action required to deactivate the module, as the source generator and manifest loader will no longer find the module's types.

---

## 5. Implementation Evidence

The following files in the Koan Framework v0.6.3 codebase constitute the reference implementation:

### Core Interfaces
| File | Type |
|---|---|
| `src/Koan.Core/IKoanInitializer.cs` | `IKoanInitializer` interface |
| `src/Koan.Core/IKoanAutoRegistrar.cs` | `IKoanAutoRegistrar` interface |
| `src/Koan.Core/BackgroundServices/IKoanBackgroundService.cs` | `IKoanBackgroundService`, `IKoanPeriodicService`, `IKoanStartupService`, `IKoanPokableService` interfaces |

### Type Registry
| File | Type |
|---|---|
| `src/Koan.Core/Hosting/Registry/KoanRegistry.cs` | `KoanRegistry` static class with `ConcurrentDictionary` fields, `RegisterInitializers()`, `RegisterAutoRegistrars()`, `RegisterBackgroundServices()`, `RegisterServiceDiscoveryAdapters()`, `BackgroundServiceDescriptor`, `ServiceDiscoveryAdapterDescriptor` |

### Compile-Time Discovery
| File | Type |
|---|---|
| `src/Koan.Core.Registry.Generators/RegistrySourceGenerator.cs` | `RegistrySourceGenerator : IIncrementalGenerator` with `RegistryModel`, `BackgroundServiceInfo`, `RegistryEmitter` |

### Generated Output Example
| File | Description |
|---|---|
| `src/Koan.Core/artifacts/generated/.../KoanRegistry_Koan_Core.g.cs` | Generated `[ModuleInitializer]` method for Koan.Core assembly |

### Runtime Fallback
| File | Type |
|---|---|
| `src/Koan.Core/Hosting/Registry/RegistryManifestLoader.cs` | `RegistryManifestLoader` internal static class with `PopulateFromAssembly()` |

### Assembly Closure Walker
| File | Type |
|---|---|
| `src/Koan.Core/Hosting/Bootstrap/AppBootstrapper.cs` | `AppBootstrapper` static class with `InitializeModules()`, `RegistrySummarySnapshot` |

### Configuration Source Tracking
| File | Type |
|---|---|
| `src/Koan.Core/Configuration.cs` | `Configuration` static class with `ReadWithSource<T>()`, `ReadFirstWithSource<T>()`, `ConfigurationValue<T>` record struct |
| `src/Koan.Core/Hosting/Bootstrap/BootSettingSource.cs` (enum defined in hosting) | `BootSettingSource` enum: `Unknown`, `Auto`, `AppSettings`, `Environment`, `LaunchKit`, `Custom` |

### Provenance Registry
| File | Type |
|---|---|
| `src/Koan.Core/Provenance/ProvenanceRegistry.cs` | `ProvenanceRegistry` sealed class with `PillarState`, `ModuleState`, `SettingState`, `ToolState`, `NoteState`, `UpdateSnapshotLocked()` |
| `src/Koan.Core/Provenance/ProvenanceModuleWriter.cs` | `ProvenanceModuleWriter`, `ProvenanceSettingBuilder`, `ProvenanceToolBuilder`, `ProvenanceNoteBuilder` |

### Runtime Boot Report
| File | Type |
|---|---|
| `src/Koan.Core/Hosting/Runtime/AppRuntime.cs` | `AppRuntime : IAppRuntime` with `Discover()`, `CollectProvenance()` |

### Concrete Auto-Registrar Implementations (representative sample)
| File | Module |
|---|---|
| `src/Koan.ZenGarden/Initialization/KoanAutoRegistrar.cs` | ZenGarden (service mesh) |
| `src/Connectors/Data/Postgres/Initialization/KoanAutoRegistrar.cs` | PostgreSQL connector |
| `src/Connectors/Data/Mongo/Initialization/KoanAutoRegistrar.cs` | MongoDB connector |
| `src/Connectors/AI/Ollama/Initialization/KoanAutoRegistrar.cs` | Ollama AI connector |
| `src/Koan.AI/Initialization/KoanAutoRegistrar.cs` | AI core module |
| `src/Koan.Web/Initialization/KoanAutoRegistrar.cs` | Web framework module |
| `src/Koan.Cache/Initialization/KoanAutoRegistrar.cs` | Cache abstraction module |
| `src/Koan.Data.Core/Initialization/KoanAutoRegistrar.cs` | Data core module |

The codebase contains **70+ concrete `KoanAutoRegistrar` implementations** across data connectors, AI integrators, web modules, caching layers, orchestration adapters, authentication providers, storage connectors, and background service managers, demonstrating the breadth of the "Reference = Intent" activation model.

---

## 6. Publication Notice

This document is a **defensive publication** intended to establish prior art and to dedicate the described inventions to the public domain.

The inventor, Leo Botinelly (Leonardo Milson Botinelly Soares), hereby dedicates all inventions, methods, systems, and techniques described in this document to the public. No patent rights are sought or reserved for any mechanism disclosed herein.

Any person or entity is free to implement, modify, extend, or commercialize the techniques described in this document without royalty, license, or attribution obligation.

This publication is intended to prevent any third party from obtaining patent protection on the mechanisms described herein, by establishing their prior art status as of the publication date of 2026-03-24.

The reference implementation exists in the Koan Framework (v0.6.3), an open-source .NET framework.

---

## 7. Antagonist Review Log

### Pass 1

**Antagonist (Hostile Patent Attorney):**

I have five objections to this publication.

**Objection 1.1 -- Abstraction gap in assembly loading trigger.** The publication states that `[ModuleInitializer]` fires "when the assembly is first loaded by the CLR." This is imprecise. A PHOSITA might not know when assembly loading occurs relative to `AppBootstrapper.InitializeModules()`. If the assembly is loaded lazily (JIT-triggered), the module initializer may not have fired by the time `GetInitializerTypes()` is called. You need to explain the ordering guarantee -- or its absence -- and how the reflection fallback covers the gap.

**Objection 1.2 -- Missing idempotency mechanism for `Initialize()`.** The publication says initializers are called for "every ServiceCollection" and must handle their own idempotency. But it does not describe how idempotency is achieved. `TryAddSingleton`, `TryAddEnumerable`, or explicit guards? A competitor could claim a novel idempotency mechanism.

**Objection 1.3 -- Scope hole on "convention-named assemblies."** The file-system scan uses `Koan.*.dll`. What prevents a malicious or accidental DLL matching this pattern from being loaded and having its initializer executed? The publication should disclose whether there is a trust boundary or assembly validation mechanism, or explicitly state that this is by design for extensibility.

**Objection 1.4 -- Prior art weakness on Spring Boot.** You dismiss Spring Boot's `META-INF/spring.factories` as "purely runtime." However, Spring Boot 3.x introduced AOT (Ahead of Time) compilation with `BeanFactoryInitializationAotContribution`, which generates code at compile time. You need to distinguish this from your source generator approach more precisely.

**Objection 1.5 -- Terminology drift: "provenance" vs. "lineage."** The term "provenance" is used throughout but is never formally defined in the context of this document. In data engineering, "provenance" and "lineage" have distinct meanings. A patent examiner could argue the term is ambiguous.

**Author Response:**

**Response 1.1.** Acknowledged. The ordering guarantee is as follows: `[ModuleInitializer]` methods execute when the assembly's module is loaded into the AppDomain, which occurs before any type from that assembly is used. However, if an assembly has not yet been loaded at all (e.g., no type from it has been referenced), its module initializer will not have fired. This is precisely why the system has three layers: (a) the source-generated `[ModuleInitializer]` fires for assemblies that are loaded as a side effect of the application's type dependencies; (b) `AppBootstrapper` explicitly loads referenced assemblies via `Assembly.Load()` in its transitive walk, which triggers their module initializers; (c) `RegistryManifestLoader.PopulateFromAssembly()` is called on every assembly immediately after it is added to the set, providing the reflection fallback. The three-layer design means that by the time `GetInitializerTypes()` is called at the end of Phase 4, every discoverable assembly has been loaded and either its source-generated initializer or the reflection fallback has populated the registry. I will add this ordering explanation to Section 3.6.

**Response 1.2.** Acknowledged. Idempotency is achieved through the standard .NET DI pattern: modules use `services.TryAddSingleton<T>()`, `services.TryAddEnumerable(ServiceDescriptor.Singleton<TInterface, TImpl>())`, and similar `TryAdd*` methods that are no-ops if a service of the same type is already registered. Additionally, the comment in `AppBootstrapper` line 107-109 states: "Initializers are responsible for their own idempotency (AppDomain-scoped guards for static state)." The Postgres auto-registrar source code shows explicit use of `TryAddSingleton`, `TryAddEnumerable`, and `TryAddEnumerable(ServiceDescriptor.Singleton<...>)`. This is not a novel mechanism -- it relies on the existing `IServiceCollection.TryAdd*` family. I will clarify this in Section 3.6.

**Response 1.3.** Acknowledged. There is no cryptographic or trust-boundary validation of convention-named assemblies. The file-system scan is intentionally permissive, following the same trust model as the .NET runtime itself (if a DLL is in the application's base directory, it is trusted). This is a deliberate design choice enabling deployment-time extensibility (dropping a connector DLL into the directory activates it). I will add an explicit statement that this is by design and that the trust boundary is the application's deployment directory.

**Response 1.4.** Acknowledged. Spring Boot 3.x AOT compilation generates bean registration code at compile time via `BeanFactoryInitializationAotContribution` and the `spring-aot-maven-plugin`. However, this mechanism: (a) requires an explicit AOT compilation step separate from normal compilation; (b) generates code that replaces the runtime classpath scanner, not supplements it -- there is no fallback to runtime discovery if AOT is not used; (c) does not use a language-level module initializer to populate a registry before application code runs -- the generated code is invoked by the Spring application context during its initialization phase; (d) does not produce per-setting source provenance metadata. The Koan system's source generation is integrated into the normal compilation pipeline (no separate step), supplements rather than replaces runtime discovery, uses `[ModuleInitializer]` for pre-application-code execution, and produces provenance metadata. I will sharpen the Spring Boot AOT distinction in Section 2.3.

**Response 1.5.** Acknowledged. I will add a formal definition. In this document, "provenance" means "the metadata describing the origin, resolution path, and source authority of a configuration value or module registration, including which configuration layer provided the value and the exact key used."

---

### Pass 2

**Antagonist:**

**Objection 2.1 -- Reproducibility gap: `BootSettingSource.LaunchKit` and `Custom` are undescribed.** The enum has six members but only `Auto`, `AppSettings`, and `Environment` are explained in the resolution cascade. What are `LaunchKit` and `Custom`? A PHOSITA could not reproduce them.

**Objection 2.2 -- Missing disclosure on `EmbeddingRegistry` and `EmbeddingAttribute`.** The source generator code clearly handles `[Embedding]` attribute scanning and `EmbeddingRegistry.RegisterTypes()`. This is an additional discovery mechanism not covered in any disclosure.

**Objection 2.3 -- The `AssemblyCache` is mentioned but not described sufficiently.** How is it populated? Is it a `ConcurrentDictionary`? What API does it expose?

**Objection 2.4 -- Edge case: What happens if `Activator.CreateInstance` fails because the registrar has constructor dependencies?** The system assumes parameterless constructors. This constraint is not stated.

**Author Response:**

**Response 2.1.** `LaunchKit` is a source type for values injected by the Koan LaunchKit development tool (a local development orchestrator that sets configuration values programmatically). `Custom` is a source type for values set by module code that computes a value rather than reading it from configuration (e.g., a connection string resolved via service discovery). Both are assigned by module code within `Describe()`, not by `ReadWithSource`. I will add these definitions to Section 3.7.

**Response 2.2.** Acknowledged. The source generator also scans for classes annotated with `[Embedding]` (from `Koan.Data.AI.Attributes.EmbeddingAttribute`) and emits calls to `EmbeddingRegistry.RegisterTypes(new Type[] { ... })`. This registers entity types that participate in the AI embedding pipeline. The mechanism follows the same pattern -- compile-time discovery of attributed types with `[ModuleInitializer]` registration -- but targets a domain-specific registry rather than the general service registry. I will add this as a note in Section 3.3 and as an additional disclosure.

**Response 2.3.** `AssemblyCache` is a singleton with an internal collection that stores `Assembly` references. It is populated by `AppBootstrapper.AddAsm()` via `cache.AddAssembly(a)` for every assembly discovered in all three phases. Other framework components (entity scanners, controller discovery) use `AssemblyCache.Instance` to enumerate known assemblies without re-performing the closure walk. The specific collection type is an implementation detail; the key behavior is that it is populated during bootstrap and provides a stable, consistent set of assemblies to all consumers. I will expand the description in Section 3.14.

**Response 2.4.** Acknowledged. The system requires that all `IKoanInitializer` and `IKoanAutoRegistrar` implementations have public parameterless constructors. `Activator.CreateInstance(type)` is used without constructor arguments. The source generator does not enforce this at compile time (it could emit a diagnostic but currently does not for this case). If a constructor fails, the exception is caught and the module is skipped with best-effort semantics. This constraint is fundamental to the design: initializers run before the DI container is built, so they cannot receive injected dependencies. I will add this as an explicit constraint in Sections 3.2 and 3.6.

---

### Pass 3

**Antagonist:**

**Objection 3.1 -- Section 101 exposure: Is "adding a package reference triggers functionality" an abstract idea?** Under Alice Corp. v. CLS Bank, a method claim that amounts to "detect presence of a library and activate it" could be characterized as an abstract idea. You need to ensure the technical implementation detail is sufficient. The three-layer discovery with source generation and `[ModuleInitializer]` is the technical substance. Make sure the disclosures emphasize the specific technical mechanism, not the high-level concept.

**Objection 3.2 -- The `file` keyword for generated classes is a C# 11 language feature, not an invention.** Disclosure 13 claims the "file-scoped static class" containment strategy. Is this novel or just using a language feature?

**Objection 3.3 -- The immutable snapshot pattern (Disclosure 5) is a well-known pattern. Is there anything novel beyond applying it to provenance data?**

**Author Response:**

**Response 3.1.** Agreed. This is a defensive publication, not a patent application, so Section 101 concerns work in our favor -- the more abstract the concept appears, the more effective it is at blocking patent claims. However, the disclosures correctly emphasize the specific technical mechanisms: the Roslyn incremental generator, the `[ModuleInitializer]` emission, the three-layer registry population with concurrent dictionaries, the per-setting source tracking with four environment variable naming variants, and the immutable snapshot provenance system. These are the technical details that prevent a future patent applicant from claiming the mechanisms as novel. The high-level "Reference = Intent" concept and the specific technical implementation are both disclosed.

**Response 3.2.** Acknowledged. Disclosure 13 does not claim the `file` keyword as an invention. It discloses the specific application of `file`-scoped classes to generated module initializer code to prevent cross-assembly name collisions in a source-generated registry population system. The purpose of this disclosure is to prevent a future claim on "a method of using file-scoped classes in source-generated module initializers to avoid naming conflicts." The language feature itself is prior art; its specific application in this context is the disclosure target. The disclosure text accurately describes the technique without claiming the language feature.

**Response 3.3.** The immutable snapshot pattern itself is prior art (event sourcing, MVCC databases, etc.). The novelty disclosed is the combination: (a) an immutable snapshot registry specifically for module provenance (settings with source metadata, tools with capability identifiers, notes with severity); (b) populated by a module self-description protocol that uses a builder interface (`ProvenanceModuleWriter`) with fluent setting/tool/note builders; (c) integrated with the compile-time/runtime discovery system so that every auto-discovered module automatically participates in provenance reporting; (d) with secret redaction built into the builder pipeline. The disclosure prevents a patent claim on this specific combination, not on immutable snapshots generally. The disclosure text is appropriate as written.

---

### Pass 4

**Antagonist:**

**Objection 4.1 -- Missing edge case: What happens when two assemblies register the same `IKoanInitializer` type?** For example, if a type is in a shared assembly referenced by two different packages. Does the `ConcurrentDictionary<Type, byte>` with `TryAdd` handle this? Yes, trivially -- `TryAdd` is idempotent on the same `Type` reference. But what about two different types with the same fully qualified name loaded from different `AssemblyLoadContext` instances? They would have different `Type` references and both be registered. Is this a concern?

**Objection 4.2 -- The publication does not describe the `KoanStartupStage` enum values or the `KoanStartupTimeline` implementation.** These are mentioned but not detailed enough for reproduction.

**Objection 4.3 -- The `Normalize` method in `Configuration.cs` has an ambient service provider resolution via `AppHost.Current`. This is an important detail -- it means `ReadWithSource` can resolve configuration even when no `IConfiguration` is passed. This implicit resolution should be disclosed.**

**Author Response:**

**Response 4.1.** In the standard .NET deployment model, assemblies with the same name are loaded once. Multiple `AssemblyLoadContext` instances would create distinct `Type` references for the same fully qualified type name, and both would be registered. This is consistent with .NET's type identity model (`Type` identity is per-ALC). In practice, Koan applications use the default ALC exclusively, and the `AssemblyCache` deduplicates by assembly name (case-insensitive) in the closure walker. The edge case of multiple ALCs is relevant only for plugin isolation scenarios, which are outside the current scope. The `TypeEqualityComparer` using reference equality is the correct behavior -- two types from different ALCs are genuinely different types. I will add a brief note about this in Section 3.4.

**Response 4.2.** `KoanStartupTimeline` is a static class that records named stages with `DateTimeOffset.UtcNow` timestamps. `KoanStartupStage` is an enum with values including `BootstrapStart`, `DataReady`, and `ConfigReady`. The `Mark(stage)` method records the timestamp; `GetSummary()` returns a snapshot of all recorded stages. The implementation is a simple timestamp recorder, not architecturally novel. The disclosure in Section 3.13 is sufficient for a PHOSITA to reproduce.

**Response 4.3.** Acknowledged. The `Configuration.ReadWithSource` method includes an ambient resolution step: if the `cfg` parameter is null, it attempts to resolve `IConfiguration` from `AppHost.Current` (a static service provider reference). This enables configuration reads outside the DI-scoped context (e.g., in static methods or early bootstrap code). The resolution is best-effort (caught exceptions result in null configuration). I will add this to Section 3.7.

---

### Pass 5

**Antagonist:**

I have reviewed the author's responses to all prior objections. The document has been revised to address:

- Ordering guarantees between source-generated module initializers and the bootstrap process (1.1)
- Idempotency via `TryAdd*` DI methods (1.2)
- Intentional trust model for file-system assembly discovery (1.3)
- Sharpened Spring Boot AOT distinction (1.4)
- Formal definition of "provenance" (1.5)
- `LaunchKit` and `Custom` source types (2.1)
- `EmbeddingRegistry` discovery mechanism (2.2)
- `AssemblyCache` description (2.3)
- Parameterless constructor constraint (2.4)
- Section 101 analysis confirming defensive posture (3.1)
- `file`-scoped class disclosure scope (3.2)
- Immutable snapshot combination novelty (3.3)
- Multi-ALC type identity edge case (4.1)
- Startup timeline sufficiency (4.2)
- Ambient `IConfiguration` resolution (4.3)

**Remaining minor items** (non-blocking):
- The `KoanPillarCatalog` registration mechanism and its thread-safety are not detailed, but this is an organizational concern, not an inventive mechanism. Acceptable.
- The `Redaction.DeIdentify` algorithm is not described. It is a utility, not part of the core invention. Acceptable.
- The `ProvenanceSnapshotUpdatedEventArgs` event pattern is standard .NET eventing. Not inventive. Acceptable.

**Verdict: CLEARED.** The publication is sufficiently detailed, precise, and comprehensive to establish prior art for all fifteen disclosed mechanisms. The prior art analysis adequately distinguishes the invention from MEF, Autofac, Spring Boot (including AOT), and ASP.NET Core. The technical detail is sufficient for a PHOSITA in .NET framework development to reproduce the system. The terminology is defined. Edge cases are addressed. The defensive posture is sound.

---

*End of Antagonist Review Log*

---

## Appendix A: Revision Notes from Antagonist Review

The following clarifications were incorporated into the main document text based on the antagonist review. They are restated here for completeness.

**A.1 Ordering guarantee (from Objection 1.1).** The three-layer discovery system provides a complete ordering guarantee: by the time `AppBootstrapper` calls `GetInitializerTypes()`, every discoverable assembly has been explicitly loaded (via the transitive closure walk or file-system scan), which triggers either its `[ModuleInitializer]` (if source-generated) or the `RegistryManifestLoader` fallback (called by `AppBootstrapper` immediately after loading each assembly). The registry is fully populated before initializer execution begins.

**A.2 Idempotency mechanism (from Objection 1.2).** `IKoanInitializer.Initialize()` implementations achieve idempotency via the standard `IServiceCollection.TryAdd*()` family of methods (`TryAddSingleton`, `TryAddEnumerable`, etc.), which are no-ops when a matching service descriptor already exists. This is not a novel mechanism but a deliberate application of existing .NET DI infrastructure.

**A.3 Trust boundary for file-system discovery (from Objection 1.3).** The file-system scan for `Koan.*.dll` files in `AppContext.BaseDirectory` operates within the same trust boundary as the .NET runtime itself. Any assembly present in the application's deployment directory is trusted. This is by design, enabling deployment-time extensibility where dropping a connector DLL activates its functionality.

**A.4 Spring Boot AOT distinction (from Objection 1.4).** Spring Boot 3.x AOT compilation generates bean registration code via `BeanFactoryInitializationAotContribution`. Key differences from the Koan approach: (a) Spring AOT requires a separate compilation step; Koan's source generator runs during normal compilation. (b) Spring AOT replaces runtime discovery; Koan supplements it with a reflection fallback. (c) Spring AOT's generated code executes during application context initialization; Koan's executes at assembly load time via `[ModuleInitializer]`. (d) Spring does not produce per-setting source provenance metadata.

**A.5 Definition of "provenance" (from Objection 1.5).** In this document, "provenance" means the metadata describing the origin, resolution path, and source authority of a configuration value or module registration, including which configuration layer provided the value (environment variable, appsettings file, auto-default, custom computation), the exact key or variable name used, and whether the value was explicitly configured or defaulted.

**A.6 LaunchKit and Custom source types (from Objection 2.1).** `BootSettingSource.LaunchKit` designates values injected by the Koan LaunchKit development orchestrator. `BootSettingSource.Custom` designates values computed by module code (e.g., a connection string resolved via service discovery rather than read from configuration). Both are assigned by module-level code within `Describe()` implementations, not by the `ReadWithSource` cascade.

**A.7 EmbeddingRegistry discovery (from Objection 2.2).** The source generator additionally scans for classes annotated with `[Embedding]` (from `Koan.Data.AI.Attributes.EmbeddingAttribute`) and emits `EmbeddingRegistry.RegisterTypes(new Type[] { ... })` calls in the same `[ModuleInitializer]` method. This domain-specific registry follows the same compile-time discovery pattern as the general service registry.

**A.8 Parameterless constructor constraint (from Objection 2.4).** All `IKoanInitializer` and `IKoanAutoRegistrar` implementations must have public parameterless constructors. They are instantiated via `Activator.CreateInstance(type)` before the DI container exists (for `Initialize`) and after the container exists but outside its scope (for `Describe`). Constructor failures are caught and the module is skipped with best-effort semantics.

**A.9 Ambient IConfiguration resolution (from Objection 4.3).** `Configuration.ReadWithSource` includes an ambient resolution path: when the `cfg` parameter is null, it attempts to resolve `IConfiguration` from a static `AppHost.Current` service provider reference. This enables configuration reads in contexts outside DI scope. The resolution is best-effort with exception suppression.

**A.10 Multi-AssemblyLoadContext type identity (from Objection 4.1).** The `KoanRegistry` uses reference equality for `Type` keys. Types loaded from different `AssemblyLoadContext` instances have distinct `Type` references and would be registered independently. This is consistent with .NET's type identity model. In practice, Koan applications use the default ALC, and the assembly closure walker deduplicates by assembly name.
