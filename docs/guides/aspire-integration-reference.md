---
type: GUIDE
domain: architecture
title: "Using Koan applications with .NET Aspire"
audience: [architects, developers]
status: current
last_updated: 2026-07-19
framework_version: source-first
validation:
  date_last_tested: 2026-07-19
  status: reviewed
  scope: R11-05 standard Aspire boundary
---

# Using Koan applications with .NET Aspire

Aspire is the application topology owner. Koan V1 does not require or publish an Aspire integration package.

In the AppHost, use standard Aspire resource integrations and project references:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");
var redis = builder.AddRedis("redis");

builder.AddProject<Projects.Api>("api")
    .WithReference(postgres)
    .WithReference(redis);

await builder.Build().RunAsync();
```

In the application project, reference the applicable Koan connectors and keep the normal host:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
```

Aspire injects connection strings and service endpoints. Koan's connector options and discovery adapters consume
those standard configuration values, report the selected mechanics, and perform connector-owned health checks.

## Ownership rules

- The AppHost owns resources, lifecycle, dashboard, references, and corrective topology failures.
- Koan owns provider election, connection discovery, health, and runtime facts.
- Connector packages do not reference Aspire hosting packages or contribute hidden AppHost resources.
- The application does not enable a Koan self-orchestration mode.

For Compose, Docker, Podman, Kubernetes, managed infrastructure, and Testcontainers, apply the same rule: the external
tool creates topology and supplies endpoints; Koan discovers and uses them.

The former automatic contributor discovery and self-container implementation is preserved only under
`shelved/orchestration-aspire/`. It is not a V1 product surface.
