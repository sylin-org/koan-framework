---
title: Koan Admin Dashboard Refresh
status: Draft
authors: Web Admin Working Group
created: 2025-10-13
sponsor: Web Pillar Core
---

**Contract**

- Inputs: ADMIN enterprise mock (template-2-enterprise-v2.html), manifest metadata (modules, pillars, settings, features), Koan Admin REST endpoints.
- Outputs: Phase 1 dashboard implementation brief, configuration view concept, roadmap alignment with WEB-0062 and WEB-0063.
- Error Modes: Missing provenance metadata, inconsistent pillar registration, legacy modules without tooling descriptors.
- Acceptance Criteria: Adopt dark enterprise layout for landing dashboard, document configuration view plan, enumerate backend extensions, outline follow-up work (monitoring, plug-ins).

## Executive Summary

Koan Admin is evolving from a utilitarian diagnostic surface into a cohesive operations console. This proposal captures the UX and technical plan for Phase 1: the refreshed Dashboard landing view and the companion Configurations view. The design anchors on the enterprise V2 mock—dark theme, stats bar, pillar accordion, tagged settings—while the engineering roadmap extends module manifests with provenance and tool metadata. Monitor telemetry panels and module plug-in surfacing remain Phase 2 items per WEB-0063.

## Goals

- Deliver a modern, legible landing dashboard that highlights uptime, module footprint, health, and pillar composition at a glance.
- Introduce configuration provenance so operators understand where each setting originates (auto-discovery, appsettings, environment, LaunchKit).
- Set up navigation primitives (top tabs, left rail) for future Monitor and plug-in surfaces without shipping inert UI.
- Preserve existing API contracts where possible while enriching payloads to support new visual affordances.

## Non-Goals

- Real-time streaming telemetry (Monitor tab) and module-hosted tool canvases (plug-ins) are explicitly deferred to Phase 2.
- Replacing LaunchKit export mechanics—Phase 1 simply relocates the current experience within the new layout.

## UX Plan

### Layout

- Adopt the mock’s dark theme, IBM Plex Mono/Inter pairing, and tokenized color palette; reuse pillar color variables from `KoanAdminModuleStyleResolver` as CSS custom properties.
- Add a persistent left navigation rail linking to Dashboard sections (Overview, Pillars, Health, Notes, LaunchKit).
- Transform the top header into a tabbed navigation bar exposing `Dashboard`, `Configurations`, and a disabled `Monitor` stub for future work.

### Dashboard Content

- Render a stats bar featuring uptime, total modules, health, active configuration count, and capability coverage—all driven from the existing status response.
- Present each pillar as a single-row accordion (icon, color, metrics) expanding to module rows with inline metadata chips and per-setting source tags.
- Surface module tool actions (when provided) adjacent to module headers, preparing for plug-in endpoints while remaining optional.

### Configurations View

- Provide a filterable list of settings grouped by source and module, mirroring a color-coded `appsettings.json` representation.
- Highlight active values, secrets (masked), and LaunchKit-exportable entries; cross-link to modules consuming each value.
- Co-locate LaunchKit bundle download controls and manifest exports to consolidate configuration workflows.

### Accessibility & Responsiveness

- Maintain high contrast ratios (>4.5:1) on dark backgrounds.
- Ensure accordion controls support keyboard navigation (Enter/Space to toggle, arrow key roving) and announce expanded state via ARIA attributes.
- Collapse the left rail into a top hamburger menu for sub-1024px viewports without hiding anchors.

## Technical Plan

### Backend Extensions

1. **Setting Provenance**

   - Extend `KoanAdminModuleSetting` with `Source` (`Auto`, `AppSettings`, `Environment`, `LaunchKit`, `Custom`) and optional `Providers` (modules relying on the value).
   - Update module manifest builders to capture provenance from discovery services, configuration binders, and LaunchKit processors.
   - Propagate through `KoanAdminModuleSurfaceSetting` so the SPA can render source tags.

2. **Module Tool Descriptors**

   - Allow modules to register optional tool metadata (display name, description, route, capability flag) during manifest generation.
   - Surface descriptors in the status payload for dashboard actions and in LaunchKit metadata for deep links.

3. **Pillar Registry Hygiene**

   - Enforce namespace association within pillar manifests to ensure new modules inherit icon/color defaults, avoiding fallback collisions highlighted in WEB-0062.

4. **Configuration Summary Enhancements**
   - Augment `KoanAdminConfigurationSummary` to include total distinct settings, secrets, and cross-pillar overlaps to power Configurations filters.

### Front-End Implementation

1. **Routing Scaffold**

   - Introduce lightweight hash routing (`#dashboard`, `#configurations`, future `#monitor`) to map top tabs to views without a full SPA framework.
   - Lazy-load future Monitor scripts to keep Phase 1 payload minimal.

2. **Design Tokens & Theme**

   - Extract CSS variables for colors, typography, spacing from the mock; co-locate in `styles.css` with fallbacks for module color injection.
   - Update `module-visuals.css` to consume the new token set while retaining pillar-specific overrides.

3. **Dashboard Components**

   - Refactor `app.js` into modular renderers (`renderStatsBar`, `renderPillarAccordion`, `renderModuleRow`) matching the mock’s DOM structure.
   - Implement setting source chips with consistent iconography and tooltips explaining provenance.
   - Wire tool descriptors to contextual action buttons (e.g., “Open Backup UI”).

4. **Configurations View**
   - Build a dedicated renderer that groups settings by source, supports text search, and shows module badges alongside values.
   - Integrate LaunchKit bundle fetch/download triggers vetted in the current notes section.

### Data & Telemetry

- Maintain existing polling cadence (manual refresh + optional auto-refresh) for Phase 1; evaluate SignalR vs. shorter polls during Phase 2 planning.
- Instrument UI actions (tab switches, tool launches) with lightweight telemetry hooks to the admin logging pipeline for future UX validation.

## Edge Cases & Risks

1. Modules lacking provenance metadata fall back to “Unknown”; highlight in UI and log warnings to prompt manifest updates.
2. Legacy modules without pillar manifests may reuse fallback colors; ensure resolver hashes remain stable to avoid flashing.
3. Large configuration payloads (hundreds of settings) could impact client performance; implement virtualized rendering if pagination becomes necessary.
4. Tool descriptors should validate authority scopes to prevent exposing admin-only endpoints without proper guards.
5. Dark theme must degrade gracefully when custom module palettes conflict with accessibility ratios; enforce contrast checks.

## Phase Milestones

| Milestone                | Scope                                                                             | Owner                   | Timeline |
| ------------------------ | --------------------------------------------------------------------------------- | ----------------------- | -------- |
| M1 – Contracted API      | Setting provenance, tool descriptors, summary enhancements                        | Web Admin services      | Week 1-2 |
| M2 – Dashboard UX        | Stats bar, pillar accordion, left nav, tabs (Dashboard + Config stub)             | Web Admin SPA           | Week 3-4 |
| M3 – Configurations View | Provenance filters, LaunchKit consolidation, JSON visualization                   | Web Admin SPA           | Week 5-6 |
| M4 – Validation          | Accessibility audit, docs update (WEB-0063 alignment), pilot in GardenCoop sample | Web Admin QA            | Week 7   |
| Phase 2 Prep             | Monitor design spike, plug-in contract refinement                                 | Web Admin working group | Week 8   |

## Dependencies

- WEB-0062 (pillar visualization) and WEB-0063 (dashboard roadmap) for registry and sequencing guidance.
- Koan Admin manifest service enhancements to capture provenance.
- Design review sign-off on dark theme tokens and accessibility metrics.

## Success Criteria

- Dashboard landing page matches mock layout within agreed variance and passes accessibility checks.
- Configurations view exposes setting provenance and LaunchKit exports with <1s render time on 500-setting dataset.
- All pillar-manifested modules display consistent colors/icons without fallback collisions.
- Stakeholder sign-off (UX, Web pillar, Support) prior to initiating Phase 2 workstream.

## Future Work (Phase 2)

- Live Monitor tab (SignalR or pooled polling) with health, job queues, and adapter telemetry.
- Module plug-in canvas enabling embedded UIs (e.g., backup, storage browser) with granular authorization.
- Multi-tenant theming hooks for branded admin deployments.

## References

- WEB-0062 – Koan Admin Pillar Visualization
- WEB-0063 – Koan Admin Dashboard Roadmap
- template-2-enterprise-v2.html – Enterprise dashboard mock
- Koan Admin status API (`KoanAdminStatusController`) and SPA renderers (`app.js`)
