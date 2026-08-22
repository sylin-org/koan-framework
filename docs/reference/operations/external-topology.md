---
type: REFERENCE
domain: operations
title: "Keep deployment topology external"
audience: [developers, operators, architects, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v1.0.0
validation:
  date_last_tested: 2026-07-22
  status: verified
  scope: external infrastructure ownership and Koan connector boundary
---

# Keep deployment topology external

Deployment topology stays where it already lives. Whatever runs your services today — Docker,
Compose, Aspire, Kubernetes, a managed service, a test harness — keeps running them, unchanged.

Koan connectors begin at the application boundary. They discover configuration, resolve connections,
elect providers, report health, and expose redacted runtime facts: everything from the connection
string inward. Standing the service up on the other end belongs to whatever already does it.

With Aspire, author ordinary AppHost code and reference the normal Koan connector in the application:

```csharp
var builder = DistributedApplication.CreateBuilder(args);
var postgres = builder.AddPostgres("postgres");
builder.AddProject<Projects.App>("app").WithReference(postgres);
await builder.Build().RunAsync();
```

`[KoanService]` remains metadata for connector discovery and inspectable facts. It describes a
dependency; it is not a provisioning promise.
