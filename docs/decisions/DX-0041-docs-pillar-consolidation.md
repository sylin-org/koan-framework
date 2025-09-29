---
type: DEV
domain: core
title: "Pillar documentation consolidation roadmap"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-28
framework_version: v0.6.2
validation:
  date_last_tested: 2025-09-28
  status: verified
  scope: docs/decisions/DX-0041-docs-pillar-consolidation.md
---

# DX-0041: Pillar documentation consolidation roadmap

Status: Accepted

## Context

Earlier restructuring (DX-0040) unified the documentation tree and established governance norms, yet the pillar surfaces remain fragmented. Guides and references for Data, Web, AI, Flow, and automation repeat identical samples, while the getting-started track sprawls across overlapping walkthroughs. Troubleshooting advice lives both in the support hub and guide-specific leaflets, leading to drift and inconsistent metadata during lint remediation.

Agents and contributors now spend significant time reconciling which file is authoritative, and metadata updates rarely reach every duplicate. The latest lint sweep highlighted the maintenance burden, prompting proposals to merge redundant surfaces into single, scoped documents that preserve intent without repetition.

## Decision

1. **Data pillar merge** – Combine `docs/guides/data-modeling.md` with `docs/reference/data/index.md`, keeping lifecycle appendices (`entity-lifecycle-events.md`) as focused references.
2. **Web pillar merge** – Fold `docs/guides/building-apis.md` into `docs/reference/web/index.md`, elevating pagination and transformer material as subsections instead of separate tutorials.
3. **AI pillar merge** – Consolidate `docs/guides/ai-integration.md` with `docs/reference/ai/index.md`, producing a single “AI Pillar Companion” that owns chat, streaming, embedding, and RAG patterns.
4. **Flow & pipelines merge** – Collapse `docs/reference/flow/index.md`, `docs/reference/core/semantic-streaming-pipelines.md`, and `docs/guides/semantic-pipelines.md` into a unified Flow reference covering ingestion stages and semantic pipeline DSL usage.
5. **Getting-started track merge** – Recompose `docs/getting-started/overview.md`, `docs/getting-started/quickstart.md`, and `docs/getting-started/guide.md` into a staged onboarding hub with anchored sections for quickstart, expansion, and enterprise adoption pointers.
6. **Troubleshooting hub alignment** – Centralize troubleshooting material inside `docs/support/troubleshooting.md`, demoting guide-level duplicates into anchored subsections within the same document.

## Rationale

- **Single source of truth** – Each pillar and journey has one canonical entry, reducing drift during version bumps.
- **Better lint efficiency** – Metadata updates touch fewer files, so schema changes land cleanly.
- **Improved discoverability** – Navigation reflects how readers think (pillar ➜ capabilities ➜ recipes) without forcing context switches between guides and references.
- **Agent friendliness** – Fewer large, near-duplicate files improve chunk quality and reduce conflicting responses.

## Scope

- Applies to instructional content in `/docs/guides`, `/docs/reference`, `/docs/getting-started`, and `/docs/support`.
- ADRs, historical archives, and case studies remain untouched.
- Consolidation must respect existing cross-links by retaining anchors or adding short redirects/aliases where external references rely on legacy paths.

## Consequences

- Fewer documents overall, each longer but better structured with sectional navigation.
- Some `/docs/guides/**` paths will be removed; references in READMEs and Toc files require updates.
- Contributors will update lint metadata once per pillar instead of mirroring values across guides and references.
- Documentation reviews should focus on clarity within merged sections rather than cross-file consistency.

## Follow-ups

- Draft merge execution plan and issue tickets per pillar, noting required TOC edits and anchor preservation.
- Perform each consolidation incrementally, running `scripts/docs-lint.ps1` and the strict DocFX build after every batch.
- Update `docs/guides/README.md`, `docs/toc.yml`, and any affected case studies or samples to point at the new canonical locations.
- Communicate the new structure in the contributor guidelines once merges complete (update `CONTRIBUTING.md` or docs governance notes).
