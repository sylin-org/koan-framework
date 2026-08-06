---
type: REFERENCE
domain: operations
title: "Keep deployment topology external"
audience: [developers, operators, architects, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: verified
  scope: external infrastructure ownership and Koan connector boundary
---

# Keep deployment topology external

Koan does not publish a bespoke orchestration pillar, CLI, manifest generator, Compose renderer,
Aspire package, or in-process container runtime. Applications keep their existing Docker, Compose,
Aspire, Kubernetes, managed-service, and test-harness topology.

Koan connectors begin at the application boundary: they discover configuration, resolve connections,
elect providers, report health, and expose redacted runtime facts. They do not provision or replace
the service.

With Aspire, author ordinary AppHost code and reference the normal Koan connector in the application:

```csharp
var builder = DistributedApplication.CreateBuilder(args);
var postgres = builder.AddPostgres("postgres");
builder.AddProject<Projects.App>("app").WithReference(postgres);
await builder.Build().RunAsync();
```

`[KoanService]` remains metadata for connector discovery and inspectable facts. It describes a
dependency; it is not a provisioning promise.
