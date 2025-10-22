---
title: ARCH-0063 – Documentation IA realignment and retrieval improvements
status: Accepted
date: 2025-10-09
deciders: Koan maintainers
consulted: Pillar leads (Core, Data, Web, AI, Flow), DX team
---

# Context

The documentation has grown organically across multiple hubs (Getting Started, Guides, Reference, ADRs, case studies), producing:

- Redundant coverage (pagination semantics across Web and Data; multiple “start here” on-ramps)
- Inconsistencies (AI surface `IAi` vs `IAiService`, `Get(id)` vs `ById(id)`, bootstrap examples)
- Missing references (Flow reference; Web HTTP API; OpenAPI generation; data streaming/pager guide)
- Retrieval friction (scattered links, sparse cross-linking, and inconsistent TOC formatting)

We need a clear information architecture (IA), consistent patterns, and automated enforcement to lower cognitive load and improve findability.

# Decision

1) Adopt a simplified top-level IA

- Home → Getting Started → Pillars (Reference) → Developer Guides → Architecture → Decisions → Case Studies → Support → Templates → Archive.
- Pillars (Reference) include: Core, Data, Web, AI, Flow, Messaging, Storage, Canon—each with a concise index (contract, edge cases, canonical samples, related ADRs).

2) Consolidate and de-duplicate content

- Single “Getting Started Hub” as the one on-ramp; remove/merge other quickstart-like pages.
- Keep Web pagination attributes under Web Reference; move materialization/streaming/pager usage into a Data guide (derived from DATA-0061) and cross-link.
- Standardize AI interface samples to `IAi` and data access samples to entity statics; eliminate repository-pattern examples.
- Program bootstrap examples are minimal: `builder.Services.AddKoan(); var app = builder.Build(); app.Run();` (document any `UseKoan()` usage in Core Reference if required).

3) Fill critical gaps (net-new docs)

- Flow Reference (docs/reference/flow/index.md)
- Web HTTP API (docs/api/web-http-api.md)
- OpenAPI generation (docs/api/openapi-generation.md)
- Data: All/Query/Streaming/Pager guide (docs/guides/data/all-query-streaming-and-pager.md)
- Data: Working with entity data (docs/guides/data/working-with-entity-data.md)
- Operations runbooks (production readiness, observability)

4) Standardize style and samples

- Mandatory “Contract” and “Edge Cases” blocks for Guides/Reference.
- Consistent naming: `IAi` (not `IAiService`), `Get(id)` as preferred by-id static; note aliases only once.
- Canonical samples centralized in `docs/examples/_canonical-samples.md` and referenced from multiple pages to prevent drift.

5) Improve retrieval and cross-linking

- Add front-matter tags and maintain a term/synonym map to boost search.
- Each pillar reference ends with “Related Guides” and “Related ADRs”.
- Keep the top-level TOC shallow and consistent.

6) Enforce via build and CI

- Build both the API docs and the full docs site in strict mode; fail on broken links.
- Add front-matter and TOC linters (keys, versions from `version.json`, indentation/formatting).
- Block manual edits to generated artifacts under `docs/reference/_generated/**`.

# Alternatives considered

- Do nothing: preserves current sprawl; findability and consistency issues persist.
- Incremental fixes only: localized improvements without a shared IA still lead to drift; poor long-term maintainability.

# Consequences

Positive:
- Lower cognitive load; predictable navigation; faster task-to-doc retrieval.
- Reduced duplication and fewer contradictory examples.
- CI catches regressions (broken links, metadata drift) early.

Trade-offs:
- Short-term churn (moves/redirects) during migration.
- Authors must follow stricter templates and CI checks.

# Implementation plan

Phase 1 – Structure & consistency (now)
- Fix TOC formatting; normalize bootstrap, `IAi`, and `Get(id)` across high-traffic docs.
- Add `docs/examples/_canonical-samples.md` and reference from pillars/guides.

Phase 2 – Gap fill (short-term)
- Author and land Flow Reference, Web HTTP API, OpenAPI generation, Data streaming/pager guide, Entity data playbook, Ops runbooks.

Phase 3 – Enforcement (short-term)
- Extend strict build to full docs; add front-matter/TOC/link linters; version sync with `version.json`.

Phase 4 – Migration & redirects (short-to-mid)
- Apply redirects for renamed/moved pages; update internal links; archive legacy duplicates.

Phase 5 – Polish (mid)
- Add glossary to Home; finalize “Related” sections; tune tags/synonyms for common searches.

# Out of scope

- Major content rewrites of case studies beyond link and structure fixes.
- Generator/template changes outside documentation build and linting.

# Follow-ups

- Register and track tasks in the docs backlog; assign per-pillar owners.
- Add DX coverage in Decisions TOC (existing `DX-0041`); cross-link from Engineering front door.

# References

- ARCH-0041 – Docs posture: instructions over tutorials
- DATA-0061 – Data access semantics (All/Query/Stream/Page)
- Engineering guardrails: docs/engineering/index.md
