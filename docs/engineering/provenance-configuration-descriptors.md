---
type: GUIDE
domain: engineering
title: "Descriptor-Driven Configuration Provenance"
audience: [developers, maintainers, ai-agents]
status: draft
last_updated: 2025-10-15
framework_version: v0.2.18+
validation:
    date_last_tested: 2025-10-16
  status: manual
  scope: docs/engineering/provenance-configuration-descriptors.md
---

# Descriptor-Driven Configuration Provenance

## Contract

- **Scope**: Standardize how Koan modules surface configuration facts and runtime discoveries through provenance descriptors instead of raw strings.
- **Inputs**: Module-specific configuration constants, `Configuration.ReadWithSource` results, provenance descriptor catalogs, environment state (`KoanEnv`), runtime discovery metadata.
- **Outputs**: Consistent `module.AddSetting` calls that reuse `ProvenanceItem` descriptors, include publication mode, honor sanitization, and track defaults for diagnostics surfaces.
- **Failure modes**: Raw string keys in registrars, missing consumer hints, incorrect publication modes, secrets emitted without sanitization, duplicated descriptor definitions.
- **Success criteria**: Every registrar consuming configuration emits provenance through shared descriptor catalogs, uses `ProvenancePublicationModeExtensions`, and keeps runtime discoveries discoverable via the `Discovery` mode.

## Why descriptors

- Descriptor catalogs keep the label, description, consumers, and sanitization rules in one place. Modules stop duplicating copy across registrars, Admin, and CLI.
- `ProvenanceItem` enforces default handling and value formatting; the registrar stays focused on reading configuration and selecting the right publication mode.
- Publication modes (`Auto`, `Settings`, `Environment`, `LaunchKit`, `Custom`, `Discovery`) tell downstream tooling where the value came from and whether the default path was taken.

## Implementation steps

1. **Author a catalog** under `Infrastructure/` (or module equivalent) with `static readonly ProvenanceItem` entries. Co-locate secret flags, default consumers, and documentation links.
2. **Read configuration with source tracking** using `Koan.Core.Configuration.ReadWithSource`. This avoids guessing provenance; the returned struct includes the resolved key, source, and whether the default was used.
3. **Select a publication mode** with `ProvenancePublicationModeExtensions`:
   - `FromConfigurationValue(value)` when you have a `ConfigurationValue<T>`.
   - `FromBootSource(source, usedDefault)` for environment-injected values like `KoanEnv`.
   - `ProvenancePublicationMode.Discovery` for runtime discoveries (e.g., auto-registered services).
4. **Publish the setting** with the descriptor overload. The descriptor key becomes the canonical configuration path in provenance output and the descriptor supplies the label/description automatically:

   ```csharp
   var secure = Koan.Core.Configuration.ReadWithSource(
       cfg,
       $"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.EnableSecureHeaders}",
       true);

   module.AddSetting(
       WebProvenanceItems.SecureHeadersEnabled,
       ProvenanceModes.FromConfigurationValue(secure),
       secure.Value,
       sourceKey: secure.ResolvedKey,
       usedDefault: secure.UsedDefault);
   ```

5. **Forward the resolved key** from `ConfigurationValue<T>` directly. `Configuration.ReadWithSource` emits `null` for defaults, dotted JSON paths for appsettings keys, and provider-format strings for environment variables.

6. **Handle runtime discoveries** (e.g., auth providers, service endpoints) by emitting a count descriptor plus per-item details via factory helpers:

   ```csharp
   module.AddSetting(
       WebAuthServicesProvenanceItems.ServicesDiscovered,
       ProvenancePublicationMode.Discovery,
       discoveredServices.Length,
       usedDefault: true);

   foreach (var service in discoveredServices)
   {
       module.AddSetting(
           WebAuthServicesProvenanceItems.ServiceDetail(service.ServiceId),
           ProvenancePublicationMode.Discovery,
           $"Scopes: {string.Join(", ", service.ProvidedScopes)} | Dependencies: {service.Dependencies.Length}",
           usedDefault: true);
   }
   ```

7. **Record tools and notes** as needed with `module.AddTool` / `module.AddNote` so diagnostics surfaces remain aligned with the descriptors.

## Publication mode selection

| Scenario | Helper | Notes |
| --- | --- | --- |
| AppSettings or environment-bound option | `ProvenanceModes.FromConfigurationValue(value)` | Pass through `sourceKey` and `usedDefault`. |
| Environment-only value (`KoanEnv`, feature flags) | `ProvenanceModes.FromBootSource(BootSettingSource.Environment, usedDefault)` | Treat framework defaults as `Auto`. |
| LaunchKit or custom bootstrap injection | `ProvenanceModes.FromBootSource(source, usedDefault)` | Set `BootSettingSource.LaunchKit` or `BootSettingSource.Custom`. |
| Runtime discovery / reflection scan | `ProvenancePublicationMode.Discovery` | Always mark `usedDefault: true` to signal no explicit config key. |

## Validation checklist

- [ ] Descriptor catalog lives in module `Infrastructure/` and reuses existing constants where possible.
- [ ] Registrar imports the catalog and `ProvenancePublicationModeExtensions` helpers instead of raw strings.
- [ ] Every `Configuration.ReadWithSource` result passes `sourceKey` and `usedDefault` to `module.AddSetting`.
- [ ] Canonical keys emitted by provenance match the descriptor key (e.g., `Koan:Admin:Logging:AllowTranscriptDownload`).
- [ ] Resolved keys from `Configuration.ReadWithSource` flow through untouched (defaults `null`, configuration keys dotted, environment keys provider format).
- [ ] All secrets or sanitizable values set `ProvenanceItem.MustSanitize` or `IsSecret` in the catalog.
- [ ] Runtime discoveries emit both summary counts and per-item details using descriptor factories.
- [ ] `dotnet build` and `scripts/build-docs.ps1 -Strict` succeed after refactoring.

## Edge cases

- **Secrets and CSP strings**: Flag descriptors with `MustSanitize` or `IsSecret` so provenance output redacts values automatically.
- **Blended sources**: When defaults can come from multiple toggles (e.g., module flag vs. `KoanEnv.AllowMagicInProduction`), compute the mode from the first non-default source and keep `usedDefault` accurate.
- **Conditional descriptors**: Only call `module.AddSetting` when the underlying value is meaningful (e.g., skip empty CSP strings) to avoid cluttering diagnostics.
- **High-volume discoveries**: Limit per-item detail descriptors or batch them with summaries to keep boot reports readable.
- **Multi-tenant modules**: Each tenant-specific descriptor should include the tenant identifier in the factory method to avoid collisions in provenance output.

## Related references

- `Koan.Core.Hosting.Bootstrap.ProvenanceItem`
- `Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions`
- `Koan.Core.Configuration.ReadWithSource`
- Module examples: `Koan.Admin.Initialization.KoanAdminAutoRegistrar`, `Koan.Data.Backup.Initialization.KoanAutoRegistrar`, `Koan.Data.Connector.Mongo.Initialization.KoanAutoRegistrar`, `Koan.Mcp.Initialization.KoanMcpAutoRegistrar`, `Koan.Scheduling.Initialization.KoanAutoRegistrar`, `Koan.Web.Initialization.KoanAutoRegistrar`, `Koan.Web.Auth.Initialization.KoanAutoRegistrar`, `Koan.Web.Auth.Services.Initialization.KoanAutoRegistrar`
