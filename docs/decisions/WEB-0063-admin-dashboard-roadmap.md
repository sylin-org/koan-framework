---
id: WEB-0063
status: accepted
title: Koan Admin Dashboard Roadmap
created: 2025-10-13
scope: web-admin
---

## Decision

We will deliver the refreshed Koan Admin experience in two focused phases:

1. **Dashboard (Phase 1)** – modernize the landing view with the dark enterprise aesthetic, left navigation rail, top navigation tabs, stats bar, pillar accordion, and per-setting source tags. Data groundwork includes extending module manifests with provenance metadata and exposing module tooling affordances.
2. **Configurations (Phase 1b)** – add a dedicated configuration surface that renders settings grouped by source and module, presents LaunchKit exports, and reuses the provenance metadata to color-code values.

Monitor telemetry panels and module plug-in UIs slide into **Phase 2** once the primary views ship.

## Motivation

- Align with the enterprise dashboard mock: high-density stats, pillar overviews, anchored navigation, and cohesive theming.
- Improve operational clarity by showing setting provenance (auto discovery, configuration files, environment variables, LaunchKit) directly in the UI.
- Avoid half-functional navigation by sequencing Monitor/Plug-ins after core pages exist.

## Impact

### Immediate Scope

- Extend `KoanAdminModuleSetting` and downstream contracts with a `Source` discriminator plus optional tool descriptors.
- Update `KoanAdminStatusResponse` and SPA rendering logic to surface provenance tags, left navigation, and the new stats layout.
- Reserve top navigation tabs (`Dashboard`, `Configurations`, `Monitor`) with basic routing; ship Dashboard + Configurations first, leave Monitor inactive.
- Prepare CSS design tokens that harmonize pillar colors with the dark theme while keeping existing module palettes functional.

### Deferred Work (Phase 2)

- Introduce live monitoring (short polling → SignalR) and pooled telemetry visualizations under the Monitor tab.
- Finalize the plug-in contract for module-contributed web tools (e.g., Backup UI) and render them within the dashboard and configuration views.

## Alternatives Considered

- **Ship all three views simultaneously**: rejected; Monitor lacks backend streaming support today, risking incomplete UX.
- **Keep current layout**: rejected; it clashes with the agreed design direction and hides provenance insights.

## References

- WEB-0062 – Koan Admin Pillar Visualization
- Admin Dashboard Enterprise v2 mock (template-2-enterprise-v2.html)
