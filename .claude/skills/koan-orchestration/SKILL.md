---
name: koan-orchestration
description: Self-describing service descriptors via [KoanService], Reference = Intent dependency containers, the Koan DevHost CLI (inspect/export/up), OrchestrationMode self-orchestration, and the IKoanAspireRegistrar escape hatch. Trigger on [KoanService]/[KoanApp], ServiceKind, DeploymentKind, "Koan export compose", "Koan up", docker-compose generation, self-orchestration, dependency containers on boot, start.bat, OrchestrationMode, Aspire AppHost.
pillar: orchestration
card: docs/reference/cards/orchestration.md
status: current
last_validated: 2026-06-18
---

# Koan Orchestration

## Trigger this skill when you see

- `[KoanService(ServiceKind.*, ...)]` on an adapter/factory class, or `[KoanApp(...)]` on the host project
- `ServiceKind` / `DeploymentKind` enums; `ContainerImage` / `DefaultPorts` / `Env` / `Volumes` / `AppEnv` descriptor fields
- The DevHost CLI — `Koan inspect`, `Koan export compose`, `Koan up`, `Koan down`, `Koan status`, `Koan doctor`
- `KoanEnv.OrchestrationMode`, `OrchestrationMode.SelfOrchestrating` / `DockerCompose` / `Kubernetes` / `AspireAppHost` / `Standalone`
- `IKoanAspireRegistrar` / `RegisterAspireResources` / `AddKoanDiscoveredResources()` on an Aspire AppHost
- References to `Koan.Orchestration.Abstractions` / `Koan.Orchestration.Cli` / `Koan.Orchestration.Aspire`
- `start.bat`, hand-written `docker-compose.yml`, "dependency containers on boot", "render compose", "self-orchestration", `.Koan/overrides.json`, `ForceOrchestrationMode`

## Core principle

**Reference = Intent, declared once on the adapter.** Every backing service an adapter needs is described *in one place* with `[KoanService(...)]` — image, tag, ports, env, volumes, and the (container-vs-local) endpoint defaults. Adding the package contributes that service to the plan; there are **no** compose files to hand-write. From those descriptors three surfaces are driven off the same source: the **DevHost CLI** plans/renders compose, the app **self-orchestrates** in dev (`KoanEnv.OrchestrationMode` resolves to `SelfOrchestrating` and a hosted service boots the declared dependency containers — so `start.bat` just runs the app), and an optional **Aspire AppHost** discovers the same modules. `OrchestrationMode` selects networking automatically (localhost vs service-name vs k8s-DNS vs aspire-managed).

<!-- validate -->
```csharp
using Koan.Core;                       // OrchestrationMode, KoanEnv
using Koan.Orchestration;              // ServiceKind, DeploymentKind
using Koan.Orchestration.Attributes;   // KoanServiceAttribute, KoanAppAttribute

// Declared ONCE on the adapter. Discovery, compose rendering, and self-orchestration
// all read this single descriptor.
[KoanService(ServiceKind.Database, shortCode: "postgres", name: "PostgreSQL",
    ContainerImage = "postgres", DefaultTag = "16", DefaultPorts = new[] { 5432 },
    Env     = new[] { "POSTGRES_USER=postgres", "POSTGRES_PASSWORD", "POSTGRES_DB=Koan" },
    Volumes = new[] { "./Data/postgres:/var/lib/postgresql/data" },
    AppEnv  = new[] { "Koan__Data__Postgres__ConnectionString={scheme}://{host}:{port}" },
    DeploymentKind = DeploymentKind.Container,
    Scheme = "postgres", Host = "postgres", EndpointPort = 5432,        // container-mode endpoint
    LocalScheme = "postgres", LocalHost = "localhost", LocalPort = 5432)] // dev-mode endpoint
public sealed class PostgresServiceDescriptor { }   // the attribute IS the contribution

// The host project announces itself as the app node.
[KoanApp(DefaultPublicPort = 5084, Capabilities = new[] { "http", "swagger" })]
public sealed class AppMarker { }

// Networking is mode-driven - read it, never hard-code "localhost" vs a service name.
public sealed class TopologyReporter
{
    public string Describe()
    {
        OrchestrationMode mode = KoanEnv.OrchestrationMode;   // detected at boot
        return mode switch
        {
            OrchestrationMode.SelfOrchestrating => "dev: app boots dependency containers; reach via localhost",
            OrchestrationMode.DockerCompose     => "compose: reach peers by service name",
            OrchestrationMode.Kubernetes        => "k8s: reach peers by cluster DNS",
            OrchestrationMode.AspireAppHost      => "aspire manages the lifecycle",
            _                                    => "standalone: external/managed endpoints (prod)",
        };
    }
}
```

Drive the same descriptors from the DevHost CLI — no compose authored by hand:

```pwsh
Koan inspect --json          # show the discovered plan (services, ports, env)
Koan export compose          # render .Koan/compose.yml (Docker or Podman)
Koan up --profile local      # plan + start the stack
Koan doctor                  # diagnose engine / port / descriptor issues
```

## Reference = Intent activation

| Add this | Effect |
|---|---|
| A package carrying a `[KoanService(...)]`-decorated adapter (e.g. a data/messaging/cache connector) | Contributes that service to the plan — compose render + dev self-orchestration pick it up automatically. |
| `[KoanApp(DefaultPublicPort=…, Capabilities=[…])]` on the host project | Marks the app node and seeds its public port + capability tags. |
| `Koan.Orchestration.Cli` (the `Koan` DevHost CLI) | `inspect` / `export` / `up` / `down` / `status` / `logs` / `doctor` over the discovered plan. |
| `Koan.Orchestration.Aspire` + `IKoanAspireRegistrar` | An Aspire AppHost discovers the same modules for a full distributed-app graph (the escape hatch below). |
| (dev environment, containers detected) | `KoanEnv.OrchestrationMode` ⇒ `SelfOrchestrating`; a hosted service boots declared dependency containers on app start — `start.bat` is just `dotnet run`. |

`Profile` (`Local`/`Ci`/`Staging`/`Prod`) gates rendering — `up` is disabled for Staging/Prod; use `export compose` to emit artifacts a real deployment owns.

## ≤5 descriptor fields you'll set

| Field | What it does |
|---|---|
| `ContainerImage` / `DefaultTag` / `DefaultPorts` | The dev container image + tag + exposed ports for this service. |
| `Env` / `Volumes` | Container-side env (`"KEY=value"` or bare `"KEY"` to pass through) and volume mounts. |
| `AppEnv = [...]` | Env injected into the **app** to reach this service; tokens `{scheme}`/`{host}`/`{port}` resolve from the active (container vs local) endpoint defaults. |
| `Scheme`/`Host`/`EndpointPort` + `LocalScheme`/`LocalHost`/`LocalPort` | Container-mode vs local-mode endpoint defaults — the source for token substitution. |
| `DeploymentKind = Container \| External \| InProcess` | How the service is provisioned for dev orchestration (default `Container`). |

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| A hand-written `docker-compose.yml` enumerating the app's backing services | `[KoanService(...)]` on each adapter + `Koan export compose` — compose is *rendered* from descriptors, not authored. |
| `docker compose up` / `docker compose build` in a sample's `start.bat` | `start.bat` runs the app; dev self-orchestration boots the declared containers (CLAUDE.md: never `docker compose up`/`build` when `start.bat` exists). |
| `"localhost"` / a hard-coded service hostname in connection logic | Read `KoanEnv.OrchestrationMode` (or use the `AppEnv` `{host}` token) — networking is mode-driven, not literal. |
| A bespoke `IHostedService` that `docker run`s dependency containers on boot | Reference the adapter package; `SelfOrchestrating` mode already spins up `[KoanService]`-declared containers. |
| Per-project image/port edits made by forking the adapter | `.Koan/overrides.json` (image/env/volumes/ports + `Mode: Local`) — overrides without touching the adapter. |
| Manually `builder.AddContainer(...)` for every service in an Aspire AppHost | `builder.AddKoanDiscoveredResources()` — wires every referenced module via `IKoanAspireRegistrar` in `Priority` order. |
| Running `Koan up` against a Staging/Prod profile | `Koan export compose` — `up` is gated to `Local`/`Ci`; prod owns the rendered artifact. |

## Escape hatches

- **Aspire AppHost (full distributed graph)**: a module implements `IKoanAspireRegistrar` *alongside* its `IKoanAutoRegistrar` (`Koan.Orchestration.Aspire`):

  ```csharp
  public sealed class KoanAutoRegistrar : IKoanAutoRegistrar, IKoanAspireRegistrar
  {
      public int Priority => 100; // infra before apps (default 1000)
      public bool ShouldRegister(IConfiguration cfg, IHostEnvironment env) => env.IsDevelopment();
      public void RegisterAspireResources(IDistributedApplicationBuilder b, IConfiguration cfg, IHostEnvironment env)
          => b.AddPostgres("postgres", port: 5432).WithDataVolume();
  }
  ```

  The AppHost calls `builder.AddKoanDiscoveredResources()` to wire every referenced module in `Priority` order. (Kept out of the canonical block above because it pulls `Aspire.Hosting`.)
- **Per-project overrides**: `.Koan/overrides.json` adjusts image / tag / env / volumes / ports per project, and `Mode: Local` pins networking — no adapter edit.
- **Force the mode**: set the `ForceOrchestrationMode` config key to override boot-time detection (e.g. pin `Standalone` against pre-existing infra, or `DockerCompose` in CI).
- **Manual provisioning**: `DeploymentKind.External` (you run it / it's managed) or `DeploymentKind.InProcess` opts a service out of dev container orchestration while keeping its endpoint + `AppEnv` wiring.

## See also

- [Reference card: orchestration.md](../../../docs/reference/cards/orchestration.md) — one-screen pillar map
- [Aspire integration guide](../../../docs/guides/aspire-integration.md) — the authoritative walkthrough (modes, CLI, AppHost)
- [`samples/S1.Web`](../../../samples/S1.Web/README.md) — `start.bat` only runs the app; self-orchestration boots the declared dependency containers
- [ARCH-0055 — Koan/Aspire integration](../../../docs/decisions/ARCH-0055-koan-aspire-integration-approval.md)
</skill_md>
</invoke>
