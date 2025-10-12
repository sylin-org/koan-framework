# Proposal: Koan Admin Surfaces & LaunchKit Function

**Status**: Completed  
**Date**: 2025-10-11  
**Authors**: Framework Architecture Working Group  
**Related**: ARCH-0043 (Lightweight Parity Roadmap), DATA-0061 (Pagination & Streaming), WEB-0035 (EntityController Transformers)

---

## Executive Summary

Koan developers need a cohesive way to inspect runtime capabilities, validate adapter wiring, and generate production-ready configuration bundles without hand assembling tooling. This proposal introduces **Koan.Admin**, composed of two entry points:

- **Koan.Console.Admin** — a console takeover module that renders an interactive admin UI for capability inspection, adapter validation, and the LaunchKit preparation function without exposing a shell prompt.
- **Koan.Web.Admin** — a controller-first dashboard that visualizes capabilities, runs diagnostics, and lets teams download configuration artifacts.

By default Koan.Admin activates only in non-production environments; production enablement requires explicit opt-in via `KoanEnv.AllowMagicInProduction` or targeted configuration. All HTTP surfaces live under a configurable system namespace that defaults to `/.koan/admin`, avoiding collisions while staying discoverable.

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

### Module Composition

| Module               | Purpose                                                                                                                                                                                                                                               | Deployment Scope                                                                                                                                                                                                                                                                           |
| -------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Koan.Console.Admin` | Console takeover UI (paired with the web dashboard) that renders capability panels, adapter validation flows, LaunchKit bundle preparation steps, fixture orchestration, a persistent log viewer, and report export without exposing a command shell. | Lights up only when a console/hosted app references the `Koan.Console.Admin` package at runtime (e.g., g1c1); Koan CLI hosts detect the module but suppress the takeover by default to keep CLI output clean. Automated flows should continue to use Koan.Compose or dedicated CI tooling. |
| `Koan.Web.Admin`     | ASP.NET Core controller area serving dashboards, diagnostics APIs, and download endpoints for generated artifacts. Ships with a pre-built admin client served from the module's `wwwroot`, ensuring the UI and API are delivered as a single bundle.  | Added when projects reference the package and call `services.AddKoanAdminWeb()`.                                                                                                                                                                                                           |
| Shared services      | `CapabilitiesSurveyor`, `LaunchKitGenerator`, `SchemaVerifier`, `ManifestPublisher`, `AdminAuthorizationFilter`.                                                                                                                                      | Registered by both modules to ensure consistent behavior.                                                                                                                                                                                                                                  |

The LaunchKit function is intentionally small in scope: it assembles application launch kits (Compose, appsettings, Aspire fragments, OpenAPI clients) based on the active host configuration so teams can export the same bundles from either surface.

Both modules read a unified `KoanAdminOptions` configuration model.

### Activation & Guardrails

- Default activation condition: `KoanEnv.IsDevelopment` is true.  
  `Koan:Admin:Enabled` (bool) can override the default.
- Production enablement requires either `KoanEnv.AllowMagicInProduction` or `Koan:Admin:AllowInProduction` coupled with explicit authorization policy.
- Destructive operations (`fixtures purge`, job requeue, etc.) require `Koan:Admin:DestructiveOps = true` and always respect policy + allowlist.
- When the Koan CLI hosts a process (e.g., `koan run`), module registrars detect the CLI shim and automatically suppress the console takeover to avoid output conflicts unless an explicit `--admin-console` flag is provided.

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

## Koan.Console.Admin Experience

Koan.Console.Admin is a runtime-only console takeover: applications must explicitly reference the package to enable it. The module’s `IKoanAutoRegistrar` evaluates environment state (development vs. production, Koan CLI hosting, explicit configuration) before turning on the takeover experience. When an app such as `g1c1` passes those checks, the Koan host replaces the default console output with a full-screen admin UI that launches immediately—no shell prompt, no command verbs to memorize. The global Koan CLI can launch the same UI when explicitly requested, but it suppresses the takeover by default and never exposes discrete commands; hosts without the package never see the takeover experience.

### Console UX Principles

- **Parity first**: every insight and action exposed in Koan.Web.Admin must surface in the console UI as a dedicated panel or modal. Feature releases ship both surfaces together, keeping layout and data contracts aligned.
- **Immersive terminal UI**: the takeover experience renders color-coded tables, sparkline charts, and status badges using ANSI-safe output. Layouts auto-detect terminal dimensions, with toggleable minimal mode for low-bandwidth remotes.
- **Live telemetry ribbon**: a dockable log viewport stays visible (bottom or side pane) to stream host application logs with severity coloring, quick filters, and pause/scroll controls.
- **Navigation without typing**: keyboard shortcuts (e.g., `1` for Overview, `2` for Providers, `F6` for LaunchKit bundles) shift between panels, while modal dialogs guide deeper flows. Breadcrumbs and quick previews reduce context switching.
- **Guided wizards**: LaunchKit bundle preparation, fixture orchestration, and schema verification run via wizard modals that validate inputs, preview artifacts, and support backtracking before committing changes.
- **Session continuity**: the module caches the latest discovery snapshot under `.koan/admin/cache`, allowing panels to render instantly on subsequent launches and enabling offline artifact generation where data permits.
- **Accessible by design**: high-contrast palettes meet WCAG ratios, focus cues support screen readers, and a text-only mode swaps visual grids for key-value lists.
- **Automation touchpoints**: export triggers provide JSON/YAML artifacts and machine-friendly exit codes (e.g., from `Esc` → `Export` → `Save JSON`), enabling CI agents to capture diagnostics without invoking a shell.

### Console Panels & Actions

The console UI presents a curated set of panels that mirror the web dashboard. Examples include:

| Panel                 | Purpose                                                                                                         | Key Interactions                                                                                               |
| --------------------- | --------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| **Overview**          | Summarizes modules, versions, admin URLs (respecting path prefix), and Koan environment flags.                  | Real-time status badges, frame-by-frame health sparklines, banner alerts for risky configurations.             |
| **Provider Health**   | Visualizes adapter validations for data providers, messaging brokers, and AI connectors.                        | Drill into failure modals, retry probes, view remediation playbooks.                                           |
| **Set Explorer**      | Displays available data sets/partitions, default routing, and pagination posture.                               | Toggle between tree and tabular views, flag sets missing pagination attributes.                                |
| **LaunchKit Bundles** | Prepares launch kits (Compose, appsettings, Aspire fragments, OpenAPI clients) aligned with detected providers. | Wizard-driven selections, diff previews, export queue with download tokens.                                    |
| **Fixture Ops**       | Manages deterministic fixture seeding and purging with safeguards.                                              | Dual-key confirmation for destructive actions, progress bars, dry-run previews.                                |
| **Job Monitor**       | Surfaces scheduled jobs/flows with live telemetry.                                                              | Pause/continue controls when authorized, historical run charts, queue depth indicators.                        |
| **Log Stream**        | Provides a dedicated tab/window for tailing host application logs alongside admin telemetry.                    | Severity-colored entries, structured property peek, filter/save/export controls, quick jump to related panels. |
| **Export Hub**        | Exports capability reports, dashboard snapshots, and manifest bundles.                                          | Choose JSON/YAML, send to clipboard or file, attach optional annotations.                                      |

The console takeover respects the configured route prefix, displaying exact admin URLs alongside QR codes or copy helpers so operators can jump into the web dashboard when available.

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

## Configuration & Usage Examples

### Development Defaults

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

The optional `Koan:Admin:Logging` section scopes which categories appear in the Log Stream and defines redaction keys applied before entries reach the console viewer or any export paths.

```csharp
// Initialization/KoanAdminAutoRegistrar.cs
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

Module registrars own the onboarding logic: the console/web packages register their own `IKoanAutoRegistrar` implementations to evaluate environment conditions, disable the console UI when hosted by the Koan CLI, and honor configuration overrides.

### Staging Example

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

### Production Opt-In

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
2. **Shared Services** — Implement capability surveyor, generator, schema verifier, manifest publisher, and authorization helpers.
3. **Console Module** — Build the takeover UI layer atop shared services, delivering rich ANSI-aware panels, dockable log streams, wizard flows, and export paths while maintaining machine-readable capture points for automation.
4. **Web Module** — Build controller area under the resolved prefix, bundle the admin client into the package `wwwroot` (single bundle delivery), compose Razor/Blazor/React (implementation TBD) views, and reuse shared services for data retrieval.
5. **Documentation** — Publish reference docs, LaunchKit usage notes, and security checklist. Register ADR `WEB-Admin-0001` to record guardrails.
6. **Samples** — Update at least one sample (e.g., `samples/S7.ContentPlatform`) to include admin modules in Development mode, showcasing LaunchKit bundle preparation.
7. **Testing** — Add integration tests covering prefix overrides, discovery manifest content, and adapter validation flows.

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
