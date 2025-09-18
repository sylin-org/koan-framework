---
id: ARCH-0041
slug: ARCH-0041-docs-posture-instructions-over-tutorials
domain: ARCH
status: Accepted
date: 2025-08-22
title: Documentation posture — instructions and references over tutorials/courses
---

# ADR 0041: Documentation posture — instructions and references over tutorials/courses

## Context

Koan’s documentation was a mix of conceptual references, engineering guidance, and tutorial/course-style content (quickstarts, “getting started,” long-form walkthroughs). Tutorial content became stale quickly, duplicated guidance, and distracted from authoritative, testable instructions and reference material. The framework favors consistent engineering guardrails and clear APIs over narrative journeys.

## Decision

- Adopt an instruction-first documentation posture:
  - Keep engineering/front-door pages, architectural principles, ADRs, API/reference docs, and concise, actionable guides.
  - Remove tutorial- and course-aligned docs (quickstarts, “getting started,” step-by-step courses, capstones).
  - Prefer runnable, minimal examples embedded in reference/guides over multi-part tutorials.
- Enforce posture in the build:
  - Exclude tutorial paths in DocFX (`docs/api/docfx.json`) so they don’t publish even if present locally.
  - Keep Strict docs builds as a gate; broken links to removed content must be eliminated before merge.
- Centralize adapter capabilities in a single YAML source and generate the matrix at build time; link to it from relevant references instead of duplicating.
- Keep “front doors” prominent and canonical:
  - Engineering: `docs/engineering/index.md`
  - Architecture principles: `docs/architecture/principles.md`
  - Copilot instructions: `.github/copilot-instructions.md`

## Scope

- Applies to all docs in `docs/` and the website output.
- “Tutorial/course” means multi-part journeys, quickstarts, capstones, and prescriptive end-to-end walkthroughs. Concise how-tos that are purely instructional and map 1:1 to stable APIs are allowed in Guides/Reference.

## Consequences

- Less surface area to maintain; fewer stale narratives.
- Clearer signal for developers: authoritative guidance lives in Engineering, Architecture, Reference, and ADRs.
- Samples remain the venue for end-to-end flows; docs link to them sparingly when illustrative.

## Implementation notes (completed)

- Removed tutorial files and TOC entries under:
  - `docs/api/quickstart/**`
  - `docs/guides/core/getting-started.md`, `docs/guides/core/cqrs-for-humans.md`
  - `docs/guides/messaging/messaging-getting-started.md`, `docs/guides/messaging/messaging-how-to.md`
- Scrubbed inbound references in pages and ADRs; updated guidance to point to Reference/Decisions.
- Hardened DocFX content excludes in `docs/api/docfx.json` to skip the above paths.
- Validated Strict builds end-to-end.

## Follow-ups

- Keep Decisions updated when docs posture needs exceptions (e.g., a short “migration playbook” that’s instructional and API-anchored).
- Expand Reference with small, runnable snippets and cross-links to samples where helpful.

## References

- Engineering front door: `docs/engineering/index.md`
- Architecture principles: `docs/architecture/principles.md`
- Copilot entrypoint: `.github/copilot-instructions.md`
- DocFX config: `docs/api/docfx.json`
- Adapter matrix source: `docs/reference/_data/adapters.yml`
