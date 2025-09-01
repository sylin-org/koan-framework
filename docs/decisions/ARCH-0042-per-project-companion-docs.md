---
id: ARCH-0042
slug: per-project-companion-docs
domain: Architecture
status: Accepted
date: 2025-08-24
title: Per-project companion docs — README.md (informational) and TECHNICAL.md (reference + architecture)
---

## Context

New contributors need concise, project-scoped documentation that is discoverable in the repository, aligned with our docs posture (instructions over tutorials), and consistent with Sora’s engineering guardrails. The centralized docs site is the canonical reference, but module owners and consumers benefit from files colocated with code for quick onboarding and day-to-day use.

We evaluated options: a single README, a docs/ folder per project, and dual files separating quick information from deep technical reference. We want:
- Fast path for developers to understand capabilities and get a minimal working setup.
- A stable, complete technical reference that covers contracts, configuration, design/ops, and extension points without bloating the README.
- Clear alignment with Sora rules: controllers (no inline endpoints), first-class data model statics (All/Query/FirstPage/Page/Stream), no magic values (constants/options), and links to ADRs.

## Decision

Adopt “two docs per project” at the project root:

1) README.md — informational, onboarding-focused
- Purpose and scope (1–2 sentences), optional non-goals
- Capabilities (bullets)
- Minimal setup (NuGet, one-liner wiring)
- Usage quick examples (2–4 safe snippets)
  - Data: Item.All/Query/FirstPage/Page/QueryStream
  - Web: MVC controllers with attributes; no inline MapGet/MapPost
- Customization pointer → link to TECHNICAL.md#configuration
- Compatibility (TFMs, adapters/providers)
- See also (Engineering front door, ADRs, Guides, Samples)

2) TECHNICAL.md — reference + architecture (Proposal A + C)
- Contract: inputs/outputs, options with defaults, error modes, success criteria
- Key types and surfaces: primary classes, extension points
- Configuration: options catalog; constants and typed Options; examples
- Edge cases and limits: large/slow, auth/permissions, concurrency, paging/streaming
- Observability and Security: logs/metrics/tracing; AuthN/AuthZ, headers/policies
- Design model: abstractions, data flow, composition with other Sora layers
- Extensibility and versioning stance
- Deployment/topology: dependencies, scale-out, adapters/providers
- Performance guidance: paging/streaming/batching, known constraints
- Operational cookbook: diagnostics, SLOs, failure modes, rollback
- Compatibility and migrations; provider/adapter matrix link
- References: ADRs, Guides, Samples

Alias: ARCHITECTURE.md may be used instead of TECHNICAL.md for architecturally heavy modules; content structure stays identical.

## Scope

- Applies to all projects in `src/` and `samples/` that are published or intended for reuse by other code.
- New projects must include both files before being considered “ready.”
- Existing projects should adopt this structure incrementally; prioritize foundation modules (Data, Web, Messaging, Auth, Vector).

## Consequences

Positive
- Faster onboarding with small, stable READMEs.
- Reduced churn: deep details live in TECHNICAL.md, keeping READMEs lean.
- Clearer alignment to guardrails; examples are uniform and production-safe.
- Better doc site integration via predictable file names.

Risks and mitigations
- Duplication between files → README.md links to TECHNICAL.md for details; TECHNICAL.md is source-of-truth for options/architecture.
- Drift over time → add a PR checklist item: “Updated README/TECHNICAL as needed?”
- Site discoverability → ensure DocFX includes these patterns; add a Modules index.

## Implementation notes

Placement
- `src/<Project>/README.md`
- `src/<Project>/TECHNICAL.md` (or `ARCHITECTURE.md`)

Content rules (enforced by review)
- Data examples use first-class model statics: `Item.All(ct)`, `Item.Query(...)`, `Item.FirstPage(...)`, `Item.Page(...)`, `Item.QueryStream(...)`.
- Web samples use MVC controllers with attribute routing; no inline endpoints.
- No magic values: prefer typed Options or centralized constants.
- Keep examples short and production-safe; link to `samples/` for depth.

DocFX integration (historical)
- Include `src/**/README.md` and `src/**/(TECHNICAL|ARCHITECTURE).md` in the site content.
- Add an index page under Reference to link notable module READMEs.

PR hygiene
- Update both files when changing public behavior, options, or extension points.
- Reject new stubs/placeholders; “no empty artifacts.”

## Follow-ups

1) Previously: Add DocFX content glob for `src/**/README.md` and `src/**/TECHNICAL.md`.
2) Create skeletons for Sora.Data.Core and Sora.Web; wire links to relevant ADRs.
3) Add “Modules index” page under docs/reference that links to key project READMEs.
4) Update PR template to include a “docs updated” checkbox for README/TECHNICAL.

## References

- Engineering guardrails: `docs/engineering/index.md`
- Docs posture — instructions over tutorials: `docs/decisions/ARCH-0041-docs-posture-instructions-over-tutorials.md`
- Config and constants naming: `docs/decisions/ARCH-0040-config-and-constants-naming.md`
- Data access semantics (paging/streaming): `docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- Web HTTP conventions and EntityController transformers: `docs/decisions/WEB-0035-entitycontroller-transformers.md`
