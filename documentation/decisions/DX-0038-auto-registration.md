---
id: DX-0038
slug: DX-0038-auto-registration
domain: DX
status: Accepted
date: 2025-08-18
title: Auto-registration and Bootstrap Reporting
---
 
# ADR-00xx: Auto-registration and Bootstrap Reporting

## Context
We want Koan modules to "just work" when referenced, with minimal host boilerplate and clear visibility at startup. We also want a uniform place for modules to describe their effective settings without leaking secrets.

## Decision
- "Reference = intent": Adding a project/package reference is sufficient to enable a module.
- Each assembly provides `/Initialization/KoanAutoRegistrar.cs` implementing `Koan.Core.IKoanAutoRegistrar`.
  - Initialize(IServiceCollection): idempotently registers services/options/startup filters.
  - Describe(BootReport, IConfiguration, IHostEnvironment): contributes a short, redacted summary to the boot report.
- Assemblies may also declare internal `IKoanInitializer` helpers to support staged boot or discovery flows (e.g., deferred wiring after reading configuration or performing network discovery). These remain internal implementation details invoked by the registrar and must be idempotent.
- The boot runtime aggregates all registrars, prints the report by default outside Production, and in Production when observability is enabled.
- Legacy scattered `IKoanInitializer` classes are removed in favor of the single registrar per assembly.

## Redaction policy
- Mark sensitive settings using the `isSecret` flag when calling `report.AddSetting(key, value, isSecret: true)`.
- The runtime replaces secret values with a de-identified representation (no raw secrets in logs).

## Consequences
- Clear, consistent startup behavior across modules.
- Less template boilerplate (no explicit Add/Use calls for core services like Swagger & Web).
- Improved discoverability via the boot report.
- A small, well-documented convention developers can follow for new modules.
