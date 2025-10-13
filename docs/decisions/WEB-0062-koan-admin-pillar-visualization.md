# WEB-0062 Koan Admin pillar visualization

**Status**: Accepted 2025-10-13  \
**Drivers**: Configuration visibility, Koan Admin UX, Pillar diagnostics  \
**Deciders**: Koan Framework maintainers  \
**Inputs**: BootReport module data, Koan Admin UX proposal  \
**Outputs**: Pillar catalog, enhanced status payload, configuration matrix card  \
**Related**: WEB-0061, ARCH-0044, DX-0038

## Context

The refreshed Koan Admin dashboard (WEB-0061) exposes module settings and notes but does not communicate how configuration is distributed across Koan pillars (Data, Web, Messaging, Flow, AI, Storage, etc.). Operators asked for a configuration visualization that is purely configuration-driven, color-coded per pillar, and reusable by other surfaces without violating separation of concerns.

Key requirements:

- Define a canonical set of pillar identifiers with stable color/emoji pairs that can be reused in diagnostics.
- Keep the metadata in a central catalog rather than scattering literals across the UI.
- Have the admin API surface pillar metadata alongside module settings so clients can render charts without recomputing classification.
- Load any visualization libraries from a CDN-compatible script (compatible with vanilla JS / AngularJS) to avoid bundler requirements.
- Mask secrets and avoid leaking runtime-only state—everything should be derived from BootReport data.

## Decision

1. Introduce `KoanPillarCatalog`, a shared catalog that pillar modules populate at startup. Each pillar registers its descriptor (code, label, color, icon) and declares namespace aliases, giving downstream features a single registry to consult. Provide deterministic fallback for unknown modules.
2. Extend the Koan Admin status payload with two constructs:
   - `KoanAdminModuleSurface` gains `Pillar`, `Color`, and `Icon` fields so each module reports its classification.
   - A new `KoanAdminPillarSummary` collection aggregates modules/settings per pillar for quick visualization.
3. Update `KoanAdminStatusController` to classify modules using the catalog and emit both the per-module metadata and summarized counts. Classification rules are namespace-based (e.g., `Koan.Data.*` → “Data”) and can be overridden via module notes.
4. Update the SPA (`index.html`, `app.js`, `styles.css`) to:
   - Load Chart.js from a CDN with SRI/crossorigin safeguards.
   - Render a “Configuration Coverage” card that visualizes pillar distribution (e.g., doughnut chart) and reuses the palette metadata for legends/badges.
   - Color-code module accordions and badges using the provided color values and emoji icons.
5. Document this decision here, referencing WEB-0061 to clarify the evolution of the admin surface.

## Rationale

- Centralizing the catalog prevents hard-coded colors in multiple files and keeps SoC intact.
- Namespace-based classification works with existing BootReport module naming conventions without requiring adapters to change registration flows.
- Returning both per-module and aggregated views keeps the API flexible for future clients (CLI, telemetry exporters) that may want the same data.
- Chart.js meets the “lightweight CDN” requirement, supports vanilla JS, and adds minimal footprint when loaded on demand.
- Continuing to mask secret values in the controller ensures no regression in the confidentiality guarantees established earlier.

## Consequences

- Status responses grow with additional metadata but remain backward compatible (new optional properties).
- The SPA now depends on an external CDN; we add graceful degradation when the script is unavailable.
- Any new pillar must be registered in `KoanPillarCatalog` to stay visually consistent.
- Future tooling can reuse the palette for command-line or report outputs, promoting a unified diagnostic language across Koan surfaces.

## Implementation notes

- `KoanAdminModuleStyleResolver` consults `KoanPillarCatalog` (by explicit note label, canonical code, or module namespace) and builds `(pillar, color, icon)` tuples with deterministic hashing for unknown modules.
- Modules can set `notes.pillar` (code `data`, `web`, etc.) or `notes.pillar-label` to override discovery without changing namespaces.
- Pillar owners implement a `*PillarManifest` that calls `KoanPillarCatalog.RegisterDescriptor(...)` in their auto-registrar, and adapters call `AssociateNamespace(...)` with their module prefix so diagnostics remain accurate.
- The controller produces `KoanAdminPillarSummary` entries containing module count and setting count per pillar.
- The SPA checks for `window.Chart` before instantiating the donut chart and falls back to a text legend if unavailable.
- Styles leverage CSS custom properties for dynamic badge coloring and ensure WCAG contrast for the default palette.
