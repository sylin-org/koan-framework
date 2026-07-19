---
name: koan-orchestration
description: V1 deployment ownership — use standard .NET, Aspire, Compose, Docker, Podman, or Kubernetes topology while Koan connectors consume ordinary configuration and injected service endpoints
pillar: orchestration
card: docs/reference/cards/orchestration.md
status: shelved
last_validated: 2026-07-19
---

# Koan Orchestration — V1 Boundary

## Trigger this skill when you see

- `Koan.Orchestration.Cli`, `Koan.Orchestration.Aspire`, `Koan export`, or `Koan up`
- Aspire AppHost resources, Docker Compose, Podman, Kubernetes, or deployment topology
- `KoanEnv.OrchestrationMode` and environment-dependent endpoint discovery
- a request to generate or infer infrastructure from application package references

## Core principle

**The application and standard platform tooling own topology.** The bespoke Koan CLI and Aspire bridge are shelved
beyond V1 under `shelved/` and are absent from `Koan.sln`, the active package graph, and the public release wave. Do not
restore them to satisfy an application request. Use ordinary .NET/Aspire/Compose/Docker/Podman/Kubernetes concepts;
the Koan application references its connectors and consumes standard connection strings or service endpoints.

For Aspire, author the graph directly in the AppHost with the applicable standard hosting integration and
`WithReference`. For Compose or Kubernetes, author and operate the platform-native artifact. Koan does not infer,
render, start, or own that topology in V1.

```csharp
// AppHost project: standard Aspire owns topology and injects the reference.
var builder = DistributedApplication.CreateBuilder(args);
var redis = builder.AddRedis("redis");
builder.AddProject<Projects.MyApp>("app").WithReference(redis);
builder.Build().Run();
```

The application remains ordinary Koan:

```csharp
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
app.Run();
```

## V1 ownership map

| Concern | Owner |
|---|---|
| Resource graph and lifecycle | Aspire AppHost, Compose, Docker/Podman, Kubernetes, or another standard platform |
| Project/resource references | The application/AppHost |
| Endpoint injection | Standard connection strings, environment variables, and platform service discovery |
| Connector activation | The Koan application package graph (Reference = Intent) |
| Connector election, health, and correction | The referenced Koan connector |

`KoanEnv.OrchestrationMode` remains an observation used by endpoint discovery; it is not a provisioning API. Its active
values describe externally owned environments: `Standalone`, `DockerCompose`, `Kubernetes`, and `AspireAppHost`.

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| Adding a shelved Koan CLI/Aspire project back to `Koan.sln` | Keep it shelved; express the topology with the standard owner. |
| `Koan up`, `Koan export compose`, or `AddKoanDiscoveredResources()` in V1 guidance | Replace it with ordinary Aspire, Compose, Docker/Podman, or Kubernetes workflow. |
| A connector trying to create its own infrastructure | Let it discover/configure its endpoint and report an actionable failure; provisioning stays outside the app. |
| A generic framework graph that guesses app-specific resource intent | Put the resource and `WithReference` in the AppHost/application where the business topology is known. |

## Revisit gate

The shelved source is orientation material, not a supported package promise. Re-entry requires a fresh business
contract, a demonstrated advantage over standard tooling, reconciled discovery semantics, focused tests, and explicit
release-scope approval. Moving the source back is not itself a product decision.

## See also

- [R11-05 package-family graduation](../../../docs/initiatives/koan-v1/work-items/r11/R11-05-package-family-graduation.md)
- [GoldenJourney](../../../samples/GoldenJourney/README.md)
