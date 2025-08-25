---
id: DX-0039
slug: DX-0039-s5-recs-ui-refactor-and-code-hygiene
domain: DX
status: Accepted
date: 2025-08-23
title: DX-0039 — S5.Recs UI refactor and code hygiene
---

## Context

S5.Recs (recommendations sample) shipped a pragmatic, single-file UI (`wwwroot/index.html`) with inline handlers, global state, and duplicated render logic for grid vs. list cards. The UX request introduced three concrete changes:

- Click-to-detail should be on the image only (not the whole card).
- Reserve the bottom part of the card for interactions (like, dropped, favorite, quick rate).
- Tags should be clickable to add/remove them from the preferred-tags filter.

Additionally, the results page size was increased (24 → 100), which raises mild performance and maintainability concerns in the current structure (heavy `innerHTML`, repeated templates, scattered constants).

This ADR formalizes a small refactor focused on code hygiene, safety, and DX while implementing the requested UX changes.

## Decision

Adopt a lightweight modular structure and UI componentization for S5.Recs’ frontend while preserving its static hosting and zero-build nature:

1) ES module split under `wwwroot/js/` (no bundler required)
- `config.js`: constants and tunables (PAGE_SIZE=100, prefer weight default, max tags) with dynamic merge from `/admin/recs-settings`.
- `api.js`: network layer wrappers (users, tags, recs, rate, library, anime-by-ids) and response normalization.
- `state.js`: UI state (current user, selected tags, caches) and small helpers.
- `cards.js`: card/list renderers and minimal sub-renderers (title, tags, actions, meta, rate picker).
- `filters.js`: filter and sort application (pure functions where possible).
- `tags.js`: preferred-tags UI and interactions.
- `toasts.js`: notification utilities.
- `main.js`: boot/wiring and event delegation.

2) Event delegation over inline handlers
- Remove `onclick="..."` from markup; bind once at container level for: image click (navigate), bottom controls (favorite/watched/dropped/rate), and tag toggles.
- Keep navigation bound to the image container only.

3) Componentize cards; dedupe grid/list
- Single `renderCard(anime, mode)` with layout policy for grid vs. list differences.
- Render actions in the bottom section of the card; image-only triggers navigation.
- Tags render as buttons that toggle the preferred-tags selection.

4) Safety, accessibility, and perf
- Prefer `textContent`/node creation; escape dynamic content when innerHTML is necessary.
- Add keyboard affordances (`role="button"`, `tabindex="0"`, Enter/Space to open details).
- Use `loading="lazy"` on images; keep page responsive with PAGE_SIZE=100.

This preserves the sample’s simplicity while aligning with Sora conventions: separation of concerns, no magic values, and predictable structure.

## Scope

In scope
- `samples/S5.Recs/wwwroot/*` frontend only (HTML/JS/CSS).
- UI behavior changes (image-only navigation; bottom interactions; tag toggles).
- Constants centralization and minimal module split (plain ES modules).

Out of scope
- Backend controllers/services and contracts (no changes required).
- Build tooling (no bundlers). Optional: add ESLint/Prettier for JS only.

## Consequences

Positive
- Better maintainability and testability (modular code, single rendering pathway).
- Safer DOM updates (escape helpers), improved accessibility and keyboard support.
- Clearer separation of concerns; constants/options are centralized.

Negative/risks
- Small up-front refactor cost; minor churn in `index.html` script wiring.
- ES modules require modern browsers (acceptable for the sample).

Operational
- With PAGE_SIZE=100, lazy-loading images keeps memory/CPU acceptable; optional progressive render or virtualization if lists grow larger.

## Implementation notes

- Insert a single `<script type="module" src="/js/main.js"></script>` in `index.html` and migrate inline handlers to delegated listeners.
- Keep `mapItemToAnime` in `api.js` or a dedicated mapper to normalize server variation.
- Maintain existing endpoints and payloads; no server changes needed.
- Use a tiny `escapeHtml` helper; prefer node creation for user content (title/synopsis/tags).
- Follow Sora docs: no empty placeholders; avoid duplication; constants in one place.

## Follow-ups

- Extract `details.html` logic into modules and share render helpers with cards.
- Add minimal lint config (ESLint + Prettier) scoped to `wwwroot/js`.
- Add 2–3 small tests for pure functions (sort/filter/mapItemToAnime) using a lightweight runner.
- Consider IntersectionObserver to progressively render for >100 results.

## References

- Engineering front door: `/docs/engineering/index.md`
- Architecture principles: `/docs/architecture/principles.md`
- Decisions:
  - `ARCH-0040-config-and-constants-naming.md`
  - `ARCH-0041-docs-posture-instructions-over-tutorials.md`
  - `WEB-0035-entitycontroller-transformers.md`
  - `OPS-0049-recs-sample-mongo-weaviate-ollama.md`
  - `DATA-0061-data-access-pagination-and-streaming.md`
