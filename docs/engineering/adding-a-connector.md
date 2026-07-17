# Adding a connector

Wire a new external system (database, vector store, message broker, AI provider, storage backend) into the Koan framework. The result is a NuGet-publishable adapter package that consumers enable by reference alone.

> Companion ADRs:
> - [ARCH-0049 — `[KoanService]` orchestration metadata](../decisions/) (referenced by the orchestration generator)
> - [ARCH-0079 — Integration tests as canon](../decisions/) (every connector ships at least one integration spec)
> - [ARCH-0080 — Shared transport ownership](../decisions/ARCH-0080-shared-transport-ownership.md) (if your connector needs Redis, Postgres, etc., consume — don't re-register)
> - [ARCH-0081 — Typed registration helpers](../decisions/ARCH-0081-typed-registration-helpers-and-analyzer.md) (use the helpers, not raw `TryAddEnumerable`)
> - [ARCH-0085 — Versioning](../decisions/ARCH-0085-versioning-compatibility-and-automation.md) (each package owns version intent)
>
> Companion workbooks:
> - [versioning.md](versioning.md) — what version your connector ships at and when it bumps
> - [nuget-publishing.md](nuget-publishing.md) — how it reaches nuget.org

---

## When to use this

You want to add a new connector to a pillar that already exists (Data, Data.Vector, Cache, Communication, Storage, AI, etc.). The pillar defines the contract; you're filling it in for a specific provider.

**Out of scope:** creating an entirely new *pillar* (a new abstraction layer with multiple adapters of its own). That's an ADR-level decision; a pillar needs its `*.Abstractions` package, a core implementation, and a place in the kernel manifest. Talk to the architect.

**Prerequisites:**

- PowerShell 7+, .NET 10 SDK, Docker (for integration tests)
- You know which **pillar** your connector belongs to (Data, Cache, etc.)
- You know which **abstraction interfaces** you need to implement (e.g., `IVectorAdapterFactory`, `IDataAdapterFactory`, `IMessageProvider`)
- You have a working example connection to the external system (you've talked to it manually)

---

## Mental model (30 seconds)

A Koan connector is **just** a few standard pieces:

1. **Adapter factory** — implements the pillar's factory interface, decorated with `[KoanService]` so orchestration knows how to spin up a dependency container locally. Decorated with `[ProviderPriority]` for tie-breaking when multiple adapters can handle the same provider name.
2. **Repository / client** — does the actual I/O. Owned per request via DI scope.
3. **Options + configurator** — strongly-typed config bound from `Koan:Data:<Provider>:*` (or equivalent).
4. **One domain-named `KoanModule`** — discovered at build time and activated by `AddKoan()`. Registers everything above.
5. **Autonomous discovery adapter** — answers "where is the service?" for local/container/Aspire environments. Optional but expected.
6. **Health contributor** — reports readiness.
7. **Orchestration evaluator** — decides whether to inject the dependency into compose/Aspire when the user runs `koan up`.
8. **Tests** — at minimum, one integration spec hitting a real container via Testcontainers (ARCH-0079).

The connector package publishes as `Sylin.Koan.<Pillar>.Connector.<Name>` and owns an independent
`version.json`; Git impact and the package graph determine when it mints.

Reference example to model after: **Weaviate** (`src/Connectors/Data/Vector/Weaviate/`) — clean, modern, all the standard pieces.

---

## Happy path

You're adding `Acme` as a new vector connector. Pillar: `Data.Vector`. Target package: `Sylin.Koan.Data.Vector.Connector.Acme`.

### Step 1 — Scaffold the csproj

```pwsh
mkdir src/Connectors/Data/Vector/Acme
```

```xml
<!-- src/Connectors/Data/Vector/Acme/Koan.Data.Vector.Connector.Acme.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <AssemblyName>Koan.Data.Vector.Connector.Acme</AssemblyName>
    <RootNamespace>Koan.Data.Vector.Connector.Acme</RootNamespace>
    <Description>Acme vector adapter for Koan: ANN search, filter pushdown, health checks.</Description>
    <PackageTags>$(CommonPackageTags);data;vector;acme;ann</PackageTags>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\..\Koan.Data.Abstractions\Koan.Data.Abstractions.csproj" />
    <ProjectReference Include="..\..\..\..\Koan.Data.Vector.Abstractions\Koan.Data.Vector.Abstractions.csproj" />
    <ProjectReference Include="..\..\..\..\Koan.Data.Core\Koan.Data.Core.csproj" />
    <ProjectReference Include="..\..\..\..\Koan.Core\Koan.Core.csproj" />
    <!-- Your connector's HTTP/driver dependencies here. -->
  </ItemGroup>
</Project>
```

The parent `src/Connectors/Directory.Build.props` imports the root and turns on `KoanRequiresOrchestrationGenerator` — you get the source generator that emits orchestration metadata, plus the `Sylin.*` PackageId prefix and SourceLink for free.

### Step 2 — Implement the adapter factory with `[KoanService]`

```csharp
// src/Connectors/Data/Vector/Acme/AcmeVectorAdapterFactory.cs
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;
using Koan.Core.Services;

namespace Koan.Data.Vector.Connector.Acme;

[ProviderPriority(10)]
[KoanService(
    ServiceKind.Vector,
    shortCode: "acme",
    name: "Acme",
    ContainerImage = "acme/acme",
    DefaultTag = "1.0",
    DefaultPorts = new[] { 8080 },
    Capabilities = new[] { "protocol=http", "vector-search=true" },
    AppEnv = new[] { "Koan__Data__Acme__Endpoint=http://{serviceId}:{port}" },
    HealthEndpoint = "/health",
    HealthIntervalSeconds = 5, HealthTimeoutSeconds = 2, HealthRetries = 12,
    Scheme = "http", Host = "acme", EndpointPort = 8080,
    UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 8080,
    LocalPattern = "http://{host}:{port}")]
public sealed class AcmeVectorAdapterFactory : IVectorAdapterFactory
{
    public string Provider => "acme";
    public IReadOnlyCollection<string> Aliases => ["acme-vector"];
    // ARCH-0103 §4.1 — the source parameter is the routed Moniker (Database-mode placement); honor it for per-source
    // physical isolation where the backend supports it, else accept it and document the deferral.
    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default") => ...
}
```

`Provider` is the one canonical ID. `Aliases` is the complete declarative set of additional names;
do not add a matching predicate or derive identity from the factory class name. Koan validates ID/alias
collisions when it compiles the host catalog. It derives normal project/package reference identities from
the factory assembly; declare `ReferenceIdentities` only when the shipped package identity genuinely differs.

A directly referenced connector participates in automatic selection. A connector that is merely present
transitively stays inert unless its pillar defines a typed layered-capability rule. `[ProviderPriority]`
orders only the candidate set already admitted by that pillar—it never overrides an explicit provider name,
lane capability, cache placement, or other typed guarantee. An explicit ID/alias resolves exactly or fails
with a correction; Koan does not substitute an unrelated provider.

The `[KoanService]` attribute feeds the orchestration generator: this is what makes `koan up` know to start an `acme/acme:1.0` container on port 8080. Model the exact field set after [`WeaviateVectorAdapterFactory.cs`](../../src/Connectors/Data/Vector/Weaviate/WeaviateVectorAdapterFactory.cs) — it's the canonical example.

### Step 3 — Implement Options + Configurator

```csharp
// AcmeOptions.cs
public sealed class AcmeOptions
{
    public string Endpoint { get; set; } = "auto";   // "auto" = autonomous discovery
    public string? ApiKey { get; set; }
    // ... provider-specific knobs
}

// AcmeOptionsConfigurator.cs — binds from configuration
public sealed class AcmeOptionsConfigurator(IConfiguration cfg) : IConfigureOptions<AcmeOptions>
{
    public void Configure(AcmeOptions options) { /* read Koan:Data:Acme:* */ }
}
```

### Step 4 — Repository + client + health contributor

Connector-specific. Model after the Weaviate ones for the I/O layer + the `IHealthContributor` implementation.

### Step 5 — Discovery adapter (optional but expected)

```csharp
// Discovery/AcmeDiscoveryAdapter.cs
public sealed class AcmeDiscoveryAdapter(IConfiguration cfg) : IServiceDiscoveryAdapter
{
    // Probes container DNS, then localhost, then Aspire env vars
    // Returns the first endpoint that answers a health check
}
```

If you skip this, users must always set `Endpoint` explicitly. With it, `Endpoint=auto` Just Works in dev/container/Aspire contexts.

### Step 6 — Orchestration evaluator

```csharp
// Orchestration/AcmeOrchestrationEvaluator.cs
public sealed class AcmeOrchestrationEvaluator(...) : IKoanOrchestrationEvaluator
{
    public string ServiceName => "acme";
    public int StartupPriority => 100;
    public bool IsServiceEnabled(IConfiguration cfg) => /* check explicit config */;
    public ServiceDependencyDescriptor CreateDependencyDescriptor(...)
        => /* describe the container to spin up */;
}
```

This is what makes `koan up` decide whether to inject your dependency into the generated compose file. Model after [`RedisOrchestrationEvaluator.cs`](../../src/Connectors/Data/Redis/Orchestration/RedisOrchestrationEvaluator.cs).

### Step 7 — the module

```csharp
// Initialization/AcmeVectorModule.cs
public sealed class AcmeVectorModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<AcmeOptions>();
        services.AddSingleton<IConfigureOptions<AcmeOptions>, AcmeOptionsConfigurator>();
        services.AddSingleton<IVectorAdapterFactory, AcmeVectorAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, AcmeHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanOrchestrationEvaluator, AcmeOrchestrationEvaluator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, AcmeDiscoveryAdapter>());
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Acme vector adapter");
        module.Capability("ANN search").Capability("Filter pushdown");
    }
}
```

The class name is business-facing, not conventional magic. It must be the assembly's single concrete `KoanModule`; identity is derived from the standard `PackageId`/assembly name. The source generator rejects multiple activation owners.

### Step 8 — Tests (ARCH-0079 mandate)

Scaffold the test project. Model after [`tests/Suites/Data/Connector.Weaviate/`](../../tests/Suites/Data/Connector.Weaviate/).

```pwsh
mkdir tests/Suites/Data/Connector.Acme/Koan.Data.Connector.Acme.Tests
```

Standard layout:

```
tests/Suites/Data/Connector.Acme/Koan.Data.Connector.Acme.Tests/
├── GlobalUsings.cs
├── Koan.Data.Connector.Acme.Tests.csproj
├── testsuite.yaml
├── Specs/
│   └── AcmeConnectorSpec.cs          ← round-trip + capability matrix
└── Support/
    └── AcmeConnectorFixture.cs       ← Testcontainers + DI bootstrap
```

The fixture:

```csharp
public sealed class AcmeConnectorFixture : IAsyncLifetime
{
    public IServiceProvider Services { get; private set; } = default!;
    public string ConnectionString { get; private set; } = "";
    public bool SkipTests { get; private set; }
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        var probe = await DockerEnvironment.Probe();
        if (!probe.Available) { SkipTests = true; SkipReason = probe.Message; return; }

        // Spin up the container via Testcontainers
        // Configure ConnectionString
        // Build the DI container via KoanIntegrationHost.Configure().ConfigureServices(s => s.AddKoan())
    }
}
```

The specs:

```csharp
public sealed class AcmeConnectorSpec(AcmeConnectorFixture fx) : IClassFixture<AcmeConnectorFixture>
{
    [Fact] public async Task Round_trip_vector_insert_and_search() { /* ... */ }
    [Fact] public async Task Distance_metric_cosine() { /* ... */ }
    [Fact] public async Task Filter_pushdown_metadata() { /* ... */ }
    // One spec per declared Capability in the [KoanService] attribute.
}
```

### Step 9 — Verify

```pwsh
# Builds + metadata + version inclusion
dotnet build src/Connectors/Data/Vector/Acme/Koan.Data.Vector.Connector.Acme.csproj -c Release
dotnet pack src/Connectors/Data/Vector/Acme/Koan.Data.Vector.Connector.Acme.csproj -c Release -o /tmp/test
unzip -p /tmp/test/Sylin.Koan.Data.Vector.Connector.Acme.*.nupkg '*.nuspec' | grep -E '<id>|<description>'
#   id should be `Sylin.Koan.Data.Vector.Connector.Acme`, description present.

dotnet run --project tools/Koan.Packaging -- inventory
#   Your new package should have one evaluated owner with complete metadata.

dotnet test tests/Suites/Data/Connector.Acme/Koan.Data.Connector.Acme.Tests/Koan.Data.Connector.Acme.Tests.csproj
#   At least one passing spec.
```

### Step 10 — Push or merge to `dev`

The next advancement of `dev` picks up your connector. The release workflow:

- asks NBGV for its version at the two Git endpoints;
- packs the changed identity and any missing current identities in dependency order;
- proves the package closure in an external app, then publishes through trusted identity.

Commit-message vocabulary does not calculate versions. Git height owns patch; an intentional
major/minor change is an explicit `version.json` edit. See [versioning.md](versioning.md) and
[nuget-publishing.md](nuget-publishing.md).

---

## Scenarios

| If you want to... | Approach |
|---|---|
| Add a new data store connector (SQL / NoSQL / document) | Same as above, pillar = `Data`, factory interface = `IDataAdapterFactory`. Model after `Koan.Data.Connector.Mongo`. |
| Add a new vector connector | This workbook. Model after Weaviate. |
| Add a new AI provider | Pillar = `AI`, model after `Koan.AI.Connector.Ollama`. |
| Add a new communication transport | Pillar = `Communication`, model after `Koan.Communication.Connector.RabbitMq`. |
| Add a new storage backend | Pillar = `Storage`, model after `Koan.Storage.Connector.Local` or `Koan.Storage.Connector.S3`. |
| Add a connector that shares transport with another | Don't re-register the shared transport (e.g. `IConnectionMultiplexer`). See [ARCH-0080](../decisions/ARCH-0080-shared-transport-ownership.md) for the consume-don't-register pattern. |

---

## Failure → recovery

### Symptom: `services.AddKoan()` doesn't pick up my connector

**Why it happens:** the capability package is absent from the application's reference manifest, or the implementation assembly does not contain exactly one constructible `KoanModule`.

**Recovery:**

```pwsh
# 1. Confirm the project reference / package reference is in place in the consuming project.
#    `dotnet list <consumer.csproj> reference` should show your connector.

# 2. Confirm the assembly has one module.
rg "class .*Module : KoanModule" src/Connectors/Data/Vector/Acme/
#    Expect: public sealed class AcmeVectorModule : KoanModule

# 3. Build the consumer in Release mode (Debug-only references can be skipped on Release runs).
dotnet build <consumer.csproj> -c Release

# 4. Verify your assembly actually ends up in the consumer's bin/Release/.
ls <consumer>/bin/Release/net10.0/ | grep Koan.Data.Vector.Connector.Acme
```

If the assembly is present but discovery still misses it, check the boot report (run a small console app that calls `services.AddKoan()` and inspect the resolved `IBootReport`). Your module should appear in the published modules list.

### Symptom: `koan up` doesn't include my service in the generated compose

**Why it happens:** the orchestration evaluator's `IsServiceEnabled(cfg)` is returning `false`, OR the source generator didn't emit the manifest entry (csproj missing `KoanRequiresOrchestrationGenerator=true` — but inherited from `src/Connectors/Directory.Build.props`, so this is rare).

**Recovery:**

```pwsh
# 1. Verify your orchestration evaluator is registered.
#    AcmeVectorModule.Register() should register the evaluator.

# 2. Verify IsServiceEnabled returns true under your test conditions.
#    Most evaluators return true when the connector's section is present in config.

# 3. Check the orchestration manifest output.
dotnet build <consumer>/<consumer>.csproj -c Release
ls <consumer>/obj/Release/net10.0/generated/Koan.Orchestration.Generators/
#    Look for entries describing your service.

# 4. Run `koan compose generate` and inspect the output.
```

### Symptom: pack succeeds but the package ID is `Koan.Data.Vector.Connector.Acme` not `Sylin.Koan.Data.Vector.Connector.Acme`

**Why it happens:** the `Sylin.` prefix comes from the root `Directory.Build.props`. The `src/Connectors/Directory.Build.props` imports the root explicitly; if your csproj somehow bypassed both (rare), the prefix never applies.

**Recovery:** confirm `src/Connectors/Directory.Build.props` still has `<Import Project="..\..\Directory.Build.props" />` near the top. If it does, your csproj inherits correctly — re-pack and re-check.

### Symptom: integration tests can't connect to the container

**Why it happens:** Docker isn't running, OR Testcontainers' Ryuk reaper is blocking, OR the container port mapping changed.

**Recovery:**

```pwsh
# 1. Verify Docker is up.
docker ps

# 2. Check the test output for Ryuk-related warnings.
$env:TESTCONTAINERS_RYUK_DISABLED = "true"
dotnet test <test-project>

# 3. Verify the container starts manually first.
docker run --rm -p 8080:8080 acme/acme:1.0
#    Then in another terminal: curl http://localhost:8080/health
```

If discovery in the connector picks the wrong endpoint (container DNS vs localhost), test fixtures should explicitly set `Endpoint` rather than relying on `"auto"`.

### Symptom: package verification flags missing description or tags

**Why it happens:** csproj was scaffolded without them.

**Recovery:** add to the csproj's first `<PropertyGroup>`:

```xml
<Description>One sentence describing what this connector does.</Description>
<PackageTags>$(CommonPackageTags);data;vector;<provider>;<key-capabilities></PackageTags>
```

Re-run the release compiler inventory, then package verification for the affected release set.

---

## Anti-patterns

- **Don't require applications to call `services.AddYourConnector()`** — the package's `KoanModule` should call its concern-owned registration internally. Reference = Intent means adding the package is sufficient.
- **Don't skip the discovery adapter** — without it, every consumer must set `Endpoint` explicitly. The whole "drop in a connector and it just works in compose/Aspire" experience depends on autonomous discovery.
- **Don't use raw `TryAddEnumerable(ServiceDescriptor.Singleton<I>(factory))`** — that's the indistinguishable-descriptor footgun KOAN0001 catches. Use the typed helpers (`AddCacheStore<T>`, etc.) where they exist for the pillar; for pillars without helpers yet, use the two-generic form: `Singleton<TService, TImpl>(...)`. See [ARCH-0081](../decisions/ARCH-0081-typed-registration-helpers-and-analyzer.md).
- **Don't re-register shared transports** — if your connector talks to Redis, you reference `Koan.Data.Connector.Redis` and consume its `IConnectionMultiplexer`. You do not register your own. See [ARCH-0080](../decisions/ARCH-0080-shared-transport-ownership.md).
- **Don't ship without an integration test** — ARCH-0079 makes integration tests canonical. A unit test against a mock is not a substitute. Even a single round-trip spec against a real container is enough to start.
- **Don't put your connector under `samples/` or `tests/`** — those folders are `IsPackable=false` by inheritance. Your connector lives under `src/Connectors/<Pillar>/<Name>/`.

---

## References

- [Weaviate connector](../../src/Connectors/Data/Vector/Weaviate/) — canonical example for the full pattern
- [Mongo connector](../../src/Connectors/Data/Mongo/) — example for data store pillar
- [Redis connector](../../src/Connectors/Data/Redis/) — example with shared-transport ownership
- [tests/Suites/Data/Connector.Weaviate/](../../tests/Suites/Data/Connector.Weaviate/) — test layout template
- [Koan.Packaging](../../tools/Koan.Packaging/README.md) — evaluated inventory and release proof
- [versioning.md](versioning.md) — when your connector bumps
- [nuget-publishing.md](nuget-publishing.md) — how it reaches nuget.org
- [ARCH-0049](../decisions/) — `[KoanService]` orchestration metadata
- [ARCH-0079](../decisions/) — integration tests as canon
- [ARCH-0080](../decisions/ARCH-0080-shared-transport-ownership.md) — shared transport ownership
- [ARCH-0081](../decisions/ARCH-0081-typed-registration-helpers-and-analyzer.md) — typed registration helpers
- [ARCH-0082](../decisions/ARCH-0082-versioning-strategy.md) — versioning
- [ARCH-0083](../decisions/ARCH-0083-operational-workbooks.md) — workbook standard this follows
