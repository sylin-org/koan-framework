---
type: REF
domain: orchestration
title: "Orchestration — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-06-18
framework_version: v0.17.0
validation:
  date_last_tested: 2026-06-18
  status: verified
  scope: docs/reference/cards/orchestration.md
---

# Orchestration — pillar map

> One-screen map of the Orchestration pillar — self-describing service adapters, a DevHost CLI that renders compose, and zero-touch dependency containers in dev. Full detail: [aspire-integration.md](../../guides/aspire-integration.md).

**What it does** — Every backing service an adapter needs is declared *once*, on the adapter, with `[KoanService(...)]` (image, ports, env, volumes, endpoint defaults). Reference = Intent: adding the package contributes the service to the plan — no compose files to hand-write. From those descriptors three surfaces are driven: (1) the **DevHost CLI** (`Koan inspect/export/up/down/status/logs/doctor`) plans the stack and renders Docker/Podman compose; (2) in development the app **self-orchestrates** — `KoanEnv.OrchestrationMode` resolves to `SelfOrchestrating` and a hosted service spins up dependency containers on boot, so `start.bat` (the canonical sample entrypoint) just runs the app; (3) an optional **.NET Aspire** AppHost discovers modules that implement `IKoanAspireResources`. `OrchestrationMode` (Standalone / SelfOrchestrating / DockerCompose / Kubernetes / AspireAppHost) selects networking automatically (localhost vs service-name vs k8s-DNS vs aspire-managed).

## The one canonical pattern

Declare the service on the adapter — discovery, compose, and self-orchestration all read it. App code references the package and is done.

```csharp
[KoanService(ServiceKind.Database, shortCode: "postgres", name: "PostgreSQL",
    ContainerImage = "postgres", DefaultTag = "16", DefaultPorts = new[] { 5432 },
    Env     = new[] { "POSTGRES_USER=postgres", "POSTGRES_PASSWORD", "POSTGRES_DB=Koan" },
    Volumes = new[] { "./Data/postgres:/var/lib/postgresql/data" },
    AppEnv  = new[] { "Koan__Data__Postgres__ConnectionString={scheme}://{host}:{port}" },
    Scheme = "postgres", Host = "postgres", EndpointPort = 5432,
    LocalHost = "localhost", LocalPort = 5432)]
public sealed class PostgresAdapterFactory : IDataAdapterFactory { /* ... */ }
```

```pwsh
Koan inspect --json          # show the discovered plan
Koan export compose          # render .Koan/compose.yml (Docker/Podman)
Koan up --profile local      # plan + start the stack
```

## ≤5 attributes you'll use

| Attribute / option | What it does |
|---|---|
| `[KoanService(Kind, shortCode, name)]` | The unified service descriptor: identity + `ContainerImage`/`DefaultTag`/`DefaultPorts`/`Env`/`Volumes` defaults. |
| `AppEnv = [...]` | Env injected into the *app* to reach this service; tokens `{scheme}`/`{host}`/`{port}` resolve from the (container vs local) endpoint defaults. |
| `Scheme/Host/EndpointPort` + `LocalScheme/LocalHost/LocalPort` | Container-mode vs local-mode endpoint defaults — the source for token substitution. |
| `DeploymentKind = Container \| External \| InProcess` | How the service is provisioned for dev orchestration (default `Container`). |
| `[KoanApp(DefaultPublicPort=…, Capabilities=[…])]` | Marks the host project as the app node and seeds its public port / capability tags. |

`Profile` (`Local`/`Ci`/`Staging`/`Prod`) gates rendering — `up` is disabled for Staging/Prod (use `export compose` to emit artifacts).

## The escape hatch

For a full distributed-app graph, an Aspire AppHost discovers the same modules: the package's one `KoanModule` also implements `IKoanAspireResources`, and `builder.AddKoanDiscoveredResources()` wires every referenced module in `Priority` order.

```csharp
public sealed class PostgresDataModule : KoanModule, IKoanAspireResources
{
    public int Priority => 100; // infra before apps
    public void RegisterAspireResources(IDistributedApplicationBuilder b, IConfiguration cfg, IHostEnvironment env)
        => b.AddPostgres("postgres", port: 5432).WithDataVolume();
    public bool ShouldRegister(IConfiguration cfg, IHostEnvironment env) => env.IsDevelopment();
}
```

Per-project tweaks without touching the adapter: `.Koan/overrides.json` (image/env/volumes/ports, plus `Mode: Local`). Force the mode via the `ForceOrchestrationMode` config key.
