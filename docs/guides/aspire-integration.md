---
type: GUIDE
domain: orchestration
title: "Aspire Integration"
audience: [developers]
status: current
last_updated: 2025-10-09
framework_version: v0.6.3
validation:
  date_last_tested: 2025-10-09
  status: verified
  scope: docs/guides/aspire-integration.md
---

# Aspire Integration

This guide summarizes practical steps to run Koan apps under .NET Aspire, consolidating the long-form content in ASPIRE-INTEGRATION.md.

## Contract

- Inputs: Web app with `builder.Services.AddKoan()`, Aspire AppHost, and referenced adapters.
- Outputs: Koan auto-discovers services via Aspire; service defaults and health endpoints are wired.
- Error modes: AppHost reference omissions, health dependencies not ready, discovery not configured.
- Success criteria: App runs under Aspire with Postgres/Redis wired and visible in the Aspire dashboard.

## Steps (short)

1. Create an Aspire AppHost and add service references (Postgres/Redis).
2. Reference the web project and call `builder.AddServiceDefaults()`.
3. In your web app, call `builder.Services.AddKoan();` and `app.MapDefaultEndpoints();`.
4. Run the AppHost; verify health and service discovery.

## Related

- Reference: Orchestration index
- Support: Troubleshooting hub
- Samples: KoanAspireIntegration/**