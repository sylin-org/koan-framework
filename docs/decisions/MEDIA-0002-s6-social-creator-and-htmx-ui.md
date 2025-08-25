---
id: MEDIA-0002
slug: s6-social-creator-and-htmx-ui
domain: media
status: accepted
date: 2025-08-24
title: S6 Social Creator sample, htmx UI, and general-purpose image actions
---

Context

- We need a concrete sample (S6) to demonstrate the new Media pillar with Storage integration, simple uploads, derived media (thumbnail), and task pipelines.
- Target is a minimal, real-world scenario that highlights reusable, general-purpose image actions and keeps strong separation of concerns.
- UI should be lightweight and served from the API (no SPA build pipeline), with server-driven HTML fragments.

Decision

- Pick “Social Media Content Creator” for S6.
  - Users upload base images and optional brand assets.
  - The system generates a thumbnail to demonstrate Tasks and derived media.
  - Pipelines compose general image actions (resize, text overlay, format) to export common social formats.
- UI technology: adopt htmx for progressive enhancement; serve fragments from MVC controllers; static shell via wwwroot.
- Orchestration-first posture: centralize pipelines and action registration in Media; keep sample controllers thin; one class per file, controllers-only for HTTP.
- Create a new reusable project for image actions: Sora.Media.Actions.Image (ImageSharp or System.Drawing-based to start), exposing code@version actions:
  - image/thumbnail@v1, image/resize@v1, image/format@v1, image/overlay@v1, image/text@v1
- MVP: start with thumbnail + format; expand with overlay/text and export presets.

Scope

- Add a new sample: samples/S6.SocialCreator with:
  - Upload endpoint and Local storage configuration
  - Task execution and status polling (MVP in-memory)
  - Static UI shell + htmx; HTML/JS/CSS per-component separation
  - Media streaming fallback for non-presign providers
- Add a new library: src/Sora.Media.Actions.Image (initially thumbnail + format); integrate later with a central task registry.

Consequences

- Demonstrates Media + Storage end-to-end with real-world flows and clean SoC.
- Sets a pattern for future pipelines and tasks with derivation keys and idempotency.
- htmx allows server-led UI without a complex frontend toolchain.
- Short-term: a simple, in-memory task runner in the sample; long-term: move orchestration into Sora.Media.Core.

Implementation notes

- Controllers: attribute-routed only (no MapGet/MapPost). One class per file.
- No magic values: use constants and typed options for route roots and cache TTLs.
- Use Local storage provider with profiles and per-model container binding.
- Idempotency: normalize args to compute a DerivationKey-based output key; start with a stable "-thumb" suffix.
- Security: enable antiforgery for htmx forms; same-origin.

Follow-ups

- Implement MediaTaskRegistry + PipelineRunner in Sora.Media.Core.
- Port the sample’s thumbnail action to Sora.Media.Actions.Image and wire through the registry.
- Add export presets (post, story, linkedin) using composed actions.

References

- MEDIA-0001 media pillar baseline and storage integration
- ARCH-0041 docs posture; ARCH-0042 per-project docs
- STOR-0005 local provider; STOR-0006 default routing and fallbacks
