# CORE-0090 — Application Self-Description Baseline

## Status

Accepted — 2025-11-12

## Context

Koan solutions currently surface fragmentary identity data. Logging borrows `IHostEnvironment.ApplicationName`, provenance modules infer module IDs, and feature surfaces such as OpenAPI or health dashboards require bespoke configuration. The lack of a unified application descriptor causes:

- duplicated literals for application name, short code, and narrative copy;
- inconsistent presentation across Swagger UI, console panels, and downstream tooling;
- no canonical source that infrastructure components can trust when stamping telemetry, building URLs, or exposing metadata to users.

We need a core contract that expresses “who this app is” once, allows configuration overrides, and flows through hosting surfaces automatically. This descriptor must be accessible early in startup, exposed via Koan bootstrap APIs, and available to components such as `Koan.Web.OpenApi`, provenance reporters, and admin consoles.

## Decision

1. **Introduce `Koan:Application` options**

   - Shape: `{ Name, Code, Description, Contact, SupportUrl, Tags[] }` with room for future fields.
   - Default binding: `services.AddKoanOptions<ApplicationIdentityOptions>("Koan:Application")` in `Koan.Core` bootstrap.
   - Option defaults resolve in order: explicit configuration → `AssemblyTitleAttribute` / `AssemblyProductAttribute` → `IHostEnvironment.ApplicationName` → fallback literals (`"Koan Application"`, `"koan-app"`). Description falls back to `AssemblyDescriptionAttribute` or an empty string.

2. **Surface application identity at runtime**

   - Extend `AppRuntime` (and corresponding bootstrap snapshot) with `ApplicationIdentity` property exposing the resolved options.
   - Publish provenance entries for name/code/description sources, noting whether values originate from configuration or assembly metadata.
   - Make identity available via `AppHost.Application` static accessor for downstream modules.

3. **Integrate with OpenAPI & Swagger**

   - `Koan.Web.OpenApi` consumes `ApplicationIdentityOptions` to populate document `Info` (title, description, contact, terms/support URLs) unless explicitly overridden by `Koan:OpenApi` keys.
   - `Koan.Web.Connector.Swagger` uses the same identity to label UI definitions (e.g., `"<Name> v1"`).

4. **Expose in diagnostics**
   - Console bootstrap panels include application name and code.
   - Background services / logs reference the canonical code when emitting structured telemetry (`app.code`).

## Consequences

- **Unified metadata**: Once configured, the same Name/Code/Description automatically ripple to OpenAPI, Swagger UI, provenance, and logs.
- **Backwards compatibility**: Apps without configuration still function, falling back to assembly metadata. Consistent defaults avoid nulls in downstream consumers.
- **DX improvements**: Developers set application identity in one place. Missing config can be flagged by analyzers when building for production.
- **Future extensions**: The descriptor can grow (e.g., icons, documentation URLs) and feed other modules such as admin portals, discovery, or service catalogs with no further breaking changes.
- **Implementation work**: Requires `ApplicationIdentityOptions` type, bootstrap wiring, provenance updates, and adjustments to OpenAPI/Swagger modules. Testing must cover precedence rules and serialization through `AppRuntime` snapshots.
