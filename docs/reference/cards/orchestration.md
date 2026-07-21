---
type: REF
domain: orchestration
title: "External topology — V1 boundary"
audience: [developers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-19
  status: verified
  scope: R11-05 external-topology disposition
---

# External topology — V1 boundary

Koan V1 does not publish an Orchestration pillar, bespoke CLI, provider/exporter SPI, manifest generator, Compose
renderer, Aspire package, or in-application self-container runtime. Those experiments are preserved under
`shelved/orchestration-cli/` and `shelved/orchestration-aspire/` outside the active package and release graph.

Applications create infrastructure with standard Aspire, Compose, Docker, Podman, Kubernetes, managed services, or
test harnesses. Koan connectors own runtime discovery, connection resolution, health, and provider facts.

With Aspire, author ordinary AppHost code:

```csharp
var builder = DistributedApplication.CreateBuilder(args);
var postgres = builder.AddPostgres("postgres");
builder.AddProject<Projects.App>("app").WithReference(postgres);
await builder.Build().RunAsync();
```

The application references `Sylin.Koan.Data.Connector.Postgres` normally. Aspire injects the connection string and
service endpoints; Koan consumes them without an Aspire-specific package or contributor.

`[KoanService]` remains Core-owned metadata for connector discovery and inspectable runtime facts. It describes a
dependency but does not promise that Koan will provision it.
