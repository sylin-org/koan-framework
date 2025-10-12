# Proposal: Koan Admin Surfaces & LaunchKit Function

**Status**: Misaligned
**Reality Check**: No `Koan.Console.Admin` or `Koan.Web.Admin` projects exist in `src/`, and no runtime surfaces implement these behaviors yet.
**Date**: 2025-10-11  
**Authors**: Framework Architecture Working Group  
**Related**: ARCH-0043 (Lightweight Parity Roadmap), DATA-0061 (Pagination & Streaming), WEB-0035 (EntityController Transformers)

---

## Contract

- **Inputs**: Koan hosts referencing the future `Koan.Console.Admin` and/or `Koan.Web.Admin` modules, configuration delivered via `Koan:Admin:*`, and existing capability discovery services.
- **Outputs**: Console and web surfaces that surface capability diagnostics, LaunchKit bundle exports, and discovery manifests gated by environment policy.
- **Error modes**: Missing module references (surfaces never activate), misconfigured prefixes, unauthorized access attempts, destructive actions requested without opt-in.
- **Success criteria**: Turnkey admin UI available in Development by default, explicit gating for higher environments, bundle exports reflecting live configuration, diagnostics mirroring adapter readiness.

### Edge Cases

- Development hosts running under the Koan CLI must suppress the console takeover unless explicitly requested.
- Reverse proxies that block dot-prefixed routes require prefix overrides without breaking discovery URLs.
- Production deployments must refuse activation when `AllowInProduction` is false even if modules are referenced.
- Destructive operations (fixture purge, queue reset) must double-gate on configuration and policy.
- Absent providers (e.g., vector adapter offline) should surface degraded status without crashing the admin surfaces.

---

## Executive Summary

Koan developers still lack a cohesive way to inspect runtime capabilities or produce LaunchKit bundles without stitching together logs, diagnostics, and scripts. The Koan repository does not yet contain any `Koan.Console.Admin` or `Koan.Web.Admin` implementation, and no sample wires an admin module. This proposal captures the **target design** so engineering can prioritize the missing work.

We intend to ship two surfaces:

- **Koan.Console.Admin** — a console takeover module that renders an interactive admin UI for capability inspection, adapter validation, and LaunchKit bundle preparation.
- **Koan.Web.Admin** — a controller-first dashboard that visualizes capabilities, runs diagnostics, and provides bundle downloads under a configurable namespace (`/.koan/admin` by default).

Activation remains development-first, with explicit gating for staging and production. Until the modules land, the admin experience remains unavailable; this proposal tracks the deliverables required to move it forward.

---

## Problem Statement

### DX & Visibility Gaps

- Developers currently stitch together telemetry, boot logs, and manual commands to verify which adapters are active.
- Ready-to-use configuration bundles (appsettings, Compose files, Aspire manifests) are hand-crafted per team, leading to drift.
- Adapter connectivity checks require ad hoc scripts, delaying feedback when infrastructure is misconfigured.

### Operational Gaps

- There is no canonical discovery manifest for Koan-powered services; downstream clients must guess at OpenAPI, health, or admin URLs.
- Admin surfaces risk colliding with application routes (`/admin`) or leaking capabilities when deployed carelessly.

---

## Goals

1. Deliver turnkey visibility into Koan runtime capabilities (data providers, messaging, web controllers, AI/vector modules) from console takeover and web surfaces.
2. Provide ready-made configuration bundles that reflect the current environment (appsettings templates, Docker Compose, Aspire fragments, OpenAPI clients).
3. Leverage existing auto-discovery and adapter validation pipelines to expose actionable diagnostics.
4. Keep the admin surface safe-by-default: enabled for Development, explicitly gated elsewhere, and isolated under a predictable namespace.
5. Offer a configurable prefix strategy so teams can adapt to proxy and platform constraints without code changes.

## Non-Goals

- Building a full graphical database administration tool.
- Supplying production support automation (e.g., destructive data scripts) beyond controlled, opt-in actions.
- Replacing existing health or telemetry infrastructure.

---

## Proposed Architecture

### Current State Audit

- No `Koan.Web.Admin` project exists under `src/` or `samples/`.
- No console takeover package or registrar implements the behaviors described here.
- Configuration sections such as `Koan:Admin` are not read by any shipping host.
- Samples like `S7.ContentPlatform` or `S13.DocMind` do not reference admin modules.

The remainder of this section outlines the modules we still need to implement.

### Module Composition (Target)

| Module               | Purpose                                                                                                                                                                                                                                               | Notes                                                                                                                                                                                                                                                                                      |
| -------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Koan.Console.Admin` | Console takeover UI paired with LaunchKit export, capability inspection, and diagnostics panels without exposing a command shell.                                                                                                                     | Requires a new assembly, registrar, and ANSI-safe UI composition. Must detect Koan CLI hosting to avoid takeover unless explicitly requested.                                                                                                                                            |
| `Koan.Web.Admin`     | ASP.NET Core controller area (likely under `src/Koan.Web.Admin`) serving dashboards, diagnostics APIs, and download endpoints. Ships with a bundled client in `wwwroot`.                                                                              | Depends on `Koan.Web` conventions, attribute routing, and the configurable prefix. Needs integration tests covering prefix overrides and policy enforcement.                                                                                                                               |
| Shared services      | `CapabilitiesSurveyor`, `LaunchKitGenerator`, `SchemaVerifier`, `ManifestPublisher`, `AdminAuthorizationFilter`.                                                                                                                                      | To be placed in a shared project (e.g., `Koan.Admin.Core`) so both surfaces stay in sync. These services do not exist today and must be implemented alongside telemetry hooks.                                                                                                             |

Both modules should consume a unified `KoanAdminOptions` configuration model that centralizes enabling flags, prefix resolution, and policy settings. An options type is not yet present in the repository.

### Activation & Guardrails (Target)

- Default activation when `KoanEnv.IsDevelopment` is true, with `Koan:Admin:Enabled` overriding for other environments.
- Production access requires explicit `AllowInProduction` plus an authorization policy;
- Destructive actions (fixtures purge, job requeue) require `Koan:Admin:DestructiveOps = true` and policy gates.
- Koan CLI detection must keep console takeover opt-in (`--admin-console`). Implementation detail: add host metadata so the registrar can check `KoanHostContext.IsCliHost`.

---

## Route Namespace & Discovery Manifests

### Default Namespace

- HTTP base path: `/.koan/admin`.
- Admin API path: `/.koan/admin/api`.
- Discovery manifest: `/.koan/manifest.json` (disabled in production by default).

### Configurable Prefix

`KoanAdminOptions.PathPrefix` controls the leading token. Allowed values:

| Value           | Resulting base path | Notes                                                                     |
| --------------- | ------------------- | ------------------------------------------------------------------------- |
| `"."` (default) | `/.koan/admin`      | Matches framework namespace; may need proxy tweak if dotfiles are hidden. |
| `"_"`           | `/_koan/admin`      | Proxy-friendly alternative.                                               |
| `"-"`           | `/-koan/admin`      | Works on most platforms with minimal adjustments.                         |
| `""`            | `/koan/admin`       | Falls back to plain prefix; collision risk handled by startup warnings.   |

If the prefix is changed, the manifest and both admin UIs reflect the new paths automatically.

### Manifest Contract

```json
{
  "version": "1.0",
  "openapi": "/swagger/v1/swagger.json",
  "health": "/health",
  "admin": "/.koan/admin",
  "modules": {
    "web": true,
    "data": true,
    "ai": false,
    "messaging": true
  }
}
```

- Roughly equivalent information can be published at `/.well-known/koan`, pointing back to the manifest.
- Sensitive details (connection strings, provider hostnames, set names) never appear in the manifest.

---

### Koan.Console.Admin Experience (Target)

Koan.Console.Admin currently exists only on paper. The implementation must:

1. Provide an `IKoanAutoRegistrar` that evaluates environment policy, Koan CLI hosting, and explicit configuration flags before enabling takeover.
2. Render an ANSI-safe UI offering parity with the web dashboard: overview, provider health, LaunchKit export, log stream, job monitor, and export hub.
3. Stream Koan host logs through the existing redaction pipeline before they reach the admin UI.
4. Cache discovery snapshots on disk (e.g., `.koan/admin/cache`) to speed up reloads while respecting opt-out options.

Design sketches from the original proposal remain valid; engineering still needs to translate them into a working console experience.

---

## Koan.Web.Admin Capabilities

### Dashboards & Tools

The web module ships with its own `IKoanAutoRegistrar` that wires controllers and serves the bundled client only when options permit (e.g., enabled environment, authorized policy).

1. **Overview** — Shows Koan version, active modules, environment snapshot, and warnings (e.g., admin enabled in production without policy).
2. **Providers Health** — Visualizes adapter status, capability flags (`QueryCaps`, `WriteCaps`), schema guard results, and vector index checks.
3. **Set Routing** — Graphs sets/partitions, associated controllers, and default pagination policies.
4. **Web Surface** — Lists `EntityController<T>` routes, transformers, pagination attributes, and GraphQL endpoints, highlighting potential misconfigurations.
5. **Messaging & Jobs** — Displays broker health, inbox/outbox monitors, scheduled flows, and allows read-only inspection by default.
6. **AI & Vector Sandbox** — Optional panel to test embed/chat operations using safe sample payloads, validating adapter connectivity.
7. **LaunchKit Downloads** — Downloadable bundles: `appsettings.*.json`, `docker-compose.*.yml`, `aspire.apphost.json`, OpenAPI-generated client SDKs. Each bundle is generated by the LaunchKit function from the live configuration (detected providers, connection hints, set routing) of the running host.

The web UI assets (SPA/TUI hybrid) are emitted into the package `wwwroot`, so consuming apps serve the dashboard and API from the same assembly without extra hosting steps.

### Security & Access Control

- Uses ASP.NET Core policies (`KoanAdminPolicy` by default). Projects can plug in custom policies or allowlists.
- Serves from the designated base path and uses `EntityController`-style controllers for consistency.
- Emits OTEL spans and structured logs for each admin action.
- Provides a runtime warning banner when running with destructive operations disabled or when `AllowMagicInProduction` gates are not satisfied.

---

### Configuration & Usage Examples (Target)

The following configuration patterns remain aspirational until the modules exist. They illustrate the expected contract so downstream work can align the options binder and registrar behavior.

#### Development Defaults

```jsonc
// appsettings.Development.json
{
  "Koan": {
    "Admin": {
      "Enabled": true,
      "PathPrefix": ".",
      "ExposeManifest": true,
      "Generate": {
        "ComposeProfiles": ["Local", "CI"],
        "OpenApiClients": ["csharp", "typescript"]
      },
      "Logging": {
        "IncludeCategories": ["Koan.*", "App.*"],
        "RedactKeys": ["password", "secret"]
      }
    }
  }
}
```

```csharp
// Initialization/KoanAdminAutoRegistrar.cs (to be implemented)
public sealed class KoanAdminAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "AdminSample";
    public string? ModuleVersion => typeof(KoanAdminAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.Configure<KoanAdminOptions>(options =>
        {
            options.PathPrefix = KoanEnv.IsDevelopment ? "." : "_";
            options.EnableConsoleUi = KoanEnv.IsDevelopment;
            options.EnableWeb = KoanEnv.IsDevelopment;
        });
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
        => report.AddModule(ModuleName, ModuleVersion);
}
```

Module registrars will own onboarding logic once the packages ship. Until then, the snippet above serves as scaffolding guidance.

#### Staging Example

```jsonc
// appsettings.Staging.json
{
  "Koan": {
    "Admin": {
      "Enabled": true,
      "PathPrefix": "_",
      "ExposeManifest": true,
      "AllowInProduction": false,
      "Authorization": {
        "Policy": "RequireKoanAdminRole",
        "AllowedNetworks": ["10.20.0.0/16"]
      }
    }
  }
}
```

#### Production Opt-In

```jsonc
{
  "Koan": {
    "Admin": {
      "Enabled": true,
      "AllowInProduction": true,
      "PathPrefix": "-",
      "ExposeManifest": false,
      "DestructiveOps": false,
      "AllowDotPrefixInProduction": false
    }
  }
}
```

---

## Use Cases & Workflows

1. **Local Onboarding** — Developers launch a console host with Koan.Console.Admin referenced; the takeover UI guides them through the LaunchKit Bundles panel to export Compose bundles, streams host logs in the Log Stream tab, and surfaces the Overview panel for immediate environment validation before they open the web dashboard at `http://localhost:5000/.koan/admin`.
2. **Preflight smoke checks** — Operators step through the Provider Health panel and Log Stream tab inside the console UI (either on-host or via the Koan CLI launching the takeover experience) to confirm data providers, vector stores, and messaging brokers respond before handing the stack to QA or demos; artifacts can be archived for audit trails.
3. **Staging Diagnostics** — Trusted operators visit `https://staging.example.com/_koan/admin` to confirm adapter health and download updated appsettings or Compose bundles aligned with staging credentials.
4. **Capability Showcases** — Customer success engineers enable read-only admin surfaces with a `-` prefix in demo environments to highlight Koan’s polyglot storage, AI integration, and flow orchestration.
5. **Policy Rollout** — Architects use the dashboard to verify that pagination attributes and set routing align with organizational standards, leveraging warnings and exported reports to drive corrective action.

---

## Implementation Plan

1. **Options & Routing** — Introduce `KoanAdminOptions` with validation, register prefix-aware route constants, extend module `IKoanAutoRegistrar` implementations, and add startup warnings for unsafe configurations.
2. **Shared Services** — Build capability surveyor, LaunchKit generator, schema verifier, manifest publisher, and authorization helpers in a shared assembly.
3. **Console Module** — Create the takeover UI atop shared services, including ANSI layout, log streaming, parity panels, and automation hooks.
4. **Web Module** — Ship controller area and bundled client, reusing shared services for data retrieval; add prefix-aware routing tests and policy enforcement checks.
5. **Documentation & ADR** — Publish reference docs, security checklist, and ADR `WEB-Admin-0001` once the initial implementation lands.
6. **Samples** — Update at least one sample (e.g., `samples/S7.ContentPlatform`) to include admin modules in Development mode, showcasing LaunchKit bundle preparation.
7. **Testing** — Add integration tests covering prefix overrides, discovery manifest content, adapter validation flows, and destructive-operation gating.

---

## Risks & Mitigations

| Risk                                           | Mitigation                                                                                                                                                                         |
| ---------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Reverse proxies blocking dot-prefixed paths.   | Provide prefix override, proxy configuration snippets, and startup warnings when using `"."` outside Development.                                                                  |
| Accidental production exposure without auth.   | Default disabled, require `AllowInProduction`, enforce policy/allowlist checks, log high-severity warning if criteria unmet.                                                       |
| Capability reports leaking internal structure. | Limit manifest to non-sensitive links; require role-based access for detailed dashboards and console/web exports beyond local machines.                                            |
| Drift between console and web surfaces.        | Shared services and options ensure both modules read identical data and respect the same prefix.                                                                                   |
| Log stream surfacing sensitive data.           | Flow logs through existing redaction filters, expose `Koan:Admin:Logging` allowlist configuration, and require elevated policy for downloading/exporting captured log transcripts. |

---

## Next Steps

1. Approve prefix policy and guardrails in ADR `WEB-Admin-0001`.
2. Implement `KoanAdminOptions` with prefix validation and environment gating.
3. Ship v0 of Koan.Console.Admin with status/check/generate panels and manifest publisher.
4. Ship web dashboard MVP with overview, provider health, and LaunchKit downloads.
5. Iterate on advanced features (fixtures, AI sandbox, job management) behind feature flags.

---

**Conclusion**

Koan.Admin aligns with the framework’s entity-first, capability-aware ethos by giving teams a coherent lens into their runtime, paired with opinionated tooling to spin up matching environments. A dot-prefixed default namespace (`/.koan/admin`) underscores that these surfaces are framework-owned while the configurable prefix keeps them adaptable. By coupling safe defaults with powerful diagnostics and the LaunchKit function, Koan.Admin shortens the path from prototype to production-ready deployment across diverse infrastructures.
