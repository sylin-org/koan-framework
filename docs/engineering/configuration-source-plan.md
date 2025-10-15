---
type: NOTE
domain: engineering
title: "Configuration ReadWithSource Improvements"
audience:
   - developers
   - maintainers
   - ai-agents
status: current
last_updated: 2025-10-15
framework_version: v0.2.18+
validation:
   date_last_tested: 2025-10-16
  status: manual
  scope: docs/engineering/configuration-source-plan.md
---

# Configuration ReadWithSource Improvements

## Current behavior

- `Configuration.ReadWithSource` normalizes the incoming key to a colon-delimited path, then probes environment variables and configuration providers.
- Environment values return the exact key used (double underscore, single underscore, and uppercase variants).
- Configuration provider hits return the colon-delimited path that was probed (e.g., `Koan:Admin:Logging:AllowTranscriptDownload`).
- Defaults (`BootSettingSource.Auto`) surface the normalized colon key even though the value did not come from a provider.
- Callers now need to post-process `ResolvedKey` to match provenance expectations (null for defaults, dotted paths for appsettings.json).

## Goals

1. Emit the *actual* source key shape from `ReadWithSource`, so registrars can forward it directly.
2. Preserve backward compatibility for callers that rely on colon keys by documenting the change and providing helper APIs for conversions if needed.
3. Keep the helper lightweight (no additional allocations or provider introspection) and deterministic.

## Change outline

1. **Normalize defaults at the source**
   - When `Source == BootSettingSource.Auto`, set `ResolvedKey` to `string.Empty` (or `null` via a new API) instead of echoing the probe key.
   - Update `ConfigurationValue<T>` and downstream callers to treat empty as "no source".

2. **Convert configuration keys to dotted notation**
   - After a successful provider read with `Source == AppSettings`, convert the resolved key from colon or underscore form to dotted JSON notation (replace `:` with `.` and trim redundant underscores).
   - Centralize the conversion in a helper (`NormalizeConfigKeyForDisplay`) so other diagnostics surfaces stay consistent.

3. **Retain environment keys verbatim**
   - Do not transform keys returned from `Environment.GetEnvironmentVariable`; provenance should reflect the exact variable name used.

4. **Expose opt-in legacy behavior if necessary**
   - Provide a `ConfigurationValue.WithColonKey()` extension (or optional flag) for modules that still expect colon form; mark it obsolete to encourage migration.

5. **Update provenance integrations**
   - Remove ad-hoc normalization from registrars once the helper emits canonical keys.
   - Document the new behavior in the descriptor guideline checklist (already noted).

6. **Regression coverage**
   - Unit-test `ReadWithSource` for:
     - Defaults → `ResolvedKey == string.Empty`
     - AppSettings (colon & underscore probes) → dotted keys
     - Environment variants → original key preserved
   - Add an integration test to ensure `/_.koan/admin/api/status` mirrors the expectations after the change.

## Implementation summary

- `Configuration.ReadWithSource` now returns `null` for default values, dotted JSON paths for appsettings keys, and untouched environment variable names.
- All provenance registrars can pass `ResolvedKey` directly to descriptor helpers without manual normalization.
- Unit coverage added in `Koan.Core` to enforce the new behaviors; downstream builds succeed.

## Next steps

1. Audit any registrars still on string-based `AddSetting` overloads (Koan.Admin, Koan.Data.Backup, and Koan.Data.Connector.Mongo now use descriptors) and migrate them to descriptor catalogs with canonical keys.
2. Communicate the behavior update via release notes and engineering guidelines (descriptor doc already updated).
3. Validate post-change telemetry on `/_.koan/admin/api/status` to confirm canonical keys and source keys align.
