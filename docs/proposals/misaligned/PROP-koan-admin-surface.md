# Proposal: Koan Admin Surfaces & LaunchKit Function

**Status**: Active Implementation
**Reality Check**: `Koan.Admin` core services and `Koan.Web.Admin` now ship in `src/`; LaunchKit, manifest, and provenance dashboards are live in g1c1 and other samples. Console takeover remains future work.
**Date**: 2025-10-13  
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

Koan developers now have a first-party admin experience: `Koan.Admin` aggregates capability diagnostics and LaunchKit exports, while `Koan.Web.Admin` serves the dashboard and APIs under the configurable prefix (`Koan:Admin:PathPrefix`, default `.`). Samples such as `g1c1.GardenCoop` wire the modules today, delivering runtime manifests, provenance-rich settings, and bundle downloads.

Two workstreams remain:

- **Koan.Console.Admin** — still unimplemented; this proposal keeps the console takeover design on the roadmap.
- **Surface polish & guardrails** — we continue to refine provenance reporting, LaunchKit outputs, and operational affordances (policy gating, destructive action guards).

Activation stays development-first with explicit staging/production opt-in. This document now tracks shipped behaviors, gaps, and the decisions governing provenance, prefix routing, and future console parity.

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

### Current State Audit (2025-10-13)

- `Koan.Admin`, `Koan.Web.Admin`, and LaunchKit services ship in `src/` and are wired into g1c1 and other greenfield samples.
- BootReport provenance is emitted for every Koan.Admin setting; derivatives (routes, manifest endpoints) are produced from the normalized prefix.
- `Koan.Console.Admin` remains unimplemented; console takeover and CLI integration are still roadmap items.
- Generated LaunchKit bundles include AppSettings, Compose, Aspire, manifest, and OpenAPI guidance based on live configuration.

The rest of this section documents shipped architecture and the remaining console deliverable.

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

### Boot Report & Provenance Decision (2025-10-13)

- `Koan.Admin` is the single reader of `Koan:Admin:PathPrefix`; the normalized value is emitted once in **BootReport Settings** as `prefix` with provenance metadata.
- Derived routes (`route.root`, `route.api`, `route.launchkit`, `route.manifest`, `route.health`, `route.logs`) are **not** separate configuration reads. They now surface under **BootReport Tools** so operators see navigable URLs without mistaking them for independent settings.
- Koan.Web.Admin augments the BootReport by adding tool entries that reference the computed paths (mirroring the `/auth/...` tool exposure in Koan.Web.Auth).
- Provenance tags remain for all Koan.Admin feature toggles (LaunchKit, logging, destructive ops), ensuring downstream modules can attribute configuration sources while maintaining separation of concerns between core options and UI presentation.

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

1. **Options & Routing** — ✅ Landed. `KoanAdminOptions`, prefix normalization, and registrar warnings ship in `Koan.Admin` v0.2.
2. **Shared Services** — ✅ Landed. LaunchKit, manifest, feature manager, and route provider implementations back both surfaces.
3. **Console Module** — ⏳ Pending. Console takeover, CLI integration, and ANSI UI composition remain open.
4. **Web Module** — ✅ Landed. Controller area, SPA assets, LaunchKit downloads, and policy enforcement are available in `Koan.Web.Admin`.
5. **Boot Report & Provenance** — ✅ Landed. Settings emit canonical configuration sources; derived routes are exposed as Tool entries per the 2025-10-13 decision.
6. **Documentation & ADR** — 🔄 Ongoing. Update WEB-0061 and supporting guides as features stabilize; add console-specific ADR once work commences.
7. **Samples** — 🔄 Ongoing. g1c1 consumes Koan.Admin today; extend coverage across other samples as feature gaps close.
8. **Testing** — 🔄 Ongoing. Maintain integration coverage for prefix overrides, policy gating, and LaunchKit bundle generation; add console parity tests when it ships.

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

1. Finalize ADR updates (WEB-0061 refresh, console-specific ADR) to codify prefix policy, provenance tooling, and surface responsibilities.
2. Staff `Koan.Console.Admin` delivery: CLI opt-in, ANSI UI, parity dashboards, and LaunchKit integration.
3. Continue LaunchKit hardening (manifest schema docs, bundle validation) and expand provenance coverage to other modules that feed Koan.Admin manifests.
4. Extend samples and integration suites to cover staging/production opt-in scenarios, policy enforcement, and destructive-operation gating.
5. Iterate on advanced tooling (fixtures, AI sandbox, job management) behind guarded feature flags once console parity lands.

---

**Conclusion**

Koan.Admin aligns with the framework’s entity-first, capability-aware ethos by giving teams a coherent lens into their runtime, paired with opinionated tooling to spin up matching environments. A dot-prefixed default namespace (`/.koan/admin`) underscores that these surfaces are framework-owned while the configurable prefix keeps them adaptable. By coupling safe defaults with powerful diagnostics and the LaunchKit function, Koan.Admin shortens the path from prototype to production-ready deployment across diverse infrastructures.
