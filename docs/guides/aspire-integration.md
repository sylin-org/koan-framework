---
type: GUIDE
domain: architecture
title: "Run a Koan application with Aspire"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: source-first
validation:
  date_last_tested: 2026-07-19
  status: reviewed
  scope: R11-05 standard Aspire boundary
related_guides:
  - entity-capabilities-howto.md
  - data-modeling.md
  - authentication-setup.md
  - building-apis.md
---

# Run a Koan application with Aspire

Aspire owns application topology. Koan connectors consume the connection strings and service endpoints Aspire injects;
no Koan Aspire package is required. See the [reference](aspire-integration-reference.md) for the ownership boundary.

## Contract

- Inputs: Web app with `builder.Services.AddKoan()`, Aspire AppHost, and referenced adapters.
- Outputs: standard Aspire references inject endpoints; Koan connectors discover and use them.
- Error modes: AppHost reference omissions, health dependencies not ready, discovery not configured.
- Success criteria: App runs under Aspire with Postgres/Redis wired and visible in the Aspire dashboard.

## Steps (short)

1. Create an Aspire AppHost and add standard Postgres/Redis resources.
2. Reference the application with `AddProject` and connect resources with `WithReference`.
3. In the application, reference the matching Koan connectors and call `builder.Services.AddKoan()` normally.
4. Run the AppHost; verify health and service discovery.

## Related

- Detailed boundary: [Using Koan applications with .NET Aspire](aspire-integration-reference.md)
