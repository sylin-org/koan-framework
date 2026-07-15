# Architectural & Technical Decisions

This index aggregates accepted and proposed decision records. Each ADR is:

- Stable once marked Accepted
- Superseded only via an explicit Supersedes / Superseded-By chain
- Focused on one concern (single-responsibility guidance)

Refer to `toc.yml` for categorized navigation. Use the template (`AAAA-0000`) for new ADRs.

## Cross-Repo Stack Canon (STACK)

Decisions that bind all three Sylin sibling repos (Koi, Zen Garden, Koan). Authored once and copied verbatim into each repo's decision directory — edits must propagate to all three.

| ID | Title | Status | Scope |
|----|-------|--------|-------|
| STACK-0001 | [The Sylin stack — layering, contracts, and trust topology](STACK-0001-sylin-stack-canon.md) | Accepted (2026-06-13) | Koi → Zen Garden → Koan layering law, contract types per seam, trust topology, mission canon (ten decisions) |

## AI Lifecycle Expansion (AI-0022 – AI-0030)

Vision and capability expansion for Koan.AI: model catalog, compute fabric, prompt primitive, chain composition, media analysis, training, evaluation, and review queues.

| ID | Title | Status | Depends On |
|----|-------|--------|------------|
| AI-0022 | [Unified AI Lifecycle — Vision](AI-0022-unified-ai-lifecycle-vision.md) | Proposed | — |
| AI-0023 | [Model Catalog and Lifecycle](AI-0023-model-catalog-and-lifecycle.md) | Proposed | AI-0022 |
| AI-0024 | [Compute Fabric](AI-0024-compute-fabric.md) | Proposed | AI-0023 |
| AI-0025 | [Prompt Primitive](AI-0025-prompt-primitive.md) | Proposed | AI-0022 |
| AI-0026 | [Chain Composition](AI-0026-chain-composition.md) | Proposed | AI-0025 |
| AI-0027 | [Media Analysis Attribute](AI-0027-media-analysis-attribute.md) | Proposed | AI-0023, MEDIA-0001 |
| AI-0028 | [Training and Dataset](AI-0028-training-and-dataset.md) | Proposed | AI-0023, AI-0024 |
| AI-0029 | [Eval and Gates](AI-0029-eval-and-gates.md) | Proposed | AI-0023, AI-0028 |
| AI-0030 | [Review Queues](AI-0030-review-queues.md) | Proposed | AI-0028 |
| AI-0031 | [Entity-Aware Agents](AI-0031-entity-aware-agents.md) | Proposed | AI-0026, AI-0014 |

## Media Pillar (MEDIA-0001 – MEDIA-0004)

Storage, variant routing, transform pipeline, and the recipe-based rendering surface.

| ID | Title | Status | Depends On / Supersedes |
|----|-------|--------|-------------------------|
| MEDIA-0001 | [Media pillar baseline and storage integration](MEDIA-0001-media-pillar-baseline-and-storage-integration.md) | Accepted | — |
| MEDIA-0002 | [S6 Social Creator sample and htmx UI](MEDIA-0002-s6-social-creator-and-htmx-ui.md) | Accepted | MEDIA-0001 |
| MEDIA-0003 | [Variant routing, automatic transforms, and canonical signature](MEDIA-0003-media-variant-routing-and-transforms.md) | Accepted | MEDIA-0001 |
| MEDIA-0004 | [Recipe pipeline, format-preserving transforms, and overlay composition](MEDIA-0004-recipe-pipeline.md) | Proposed | Extends MEDIA-0003; supersedes DX-0047 encoding policy |

## Recent Archival / Streamlining Decisions

| ID | Title | Intent |
|----|-------|--------|
| ARCH-0062 | S8 legacy snapshot removal and sample streamline | Removed obsolete duplicate S8 snapshot; clarified single active sample path. |

## Product Constitution and Entity Language

| ID | Title | Status | Scope |
|----|-------|--------|-------|
| ARCH-0105 | [Koan product constitution and proposal decision test](ARCH-0105-product-constitution.md) | Accepted | Durable product principles, meaningful-step definition, evidence boundary, and proposal decision test |
| ARCH-0106 | [Entity language admission, facets, and responsibility boundaries](ARCH-0106-entity-semantics-contract.md) | Accepted | Entity admission test, C# 14 module facets, context/lifecycle boundaries, and migration rules |
| ARCH-0113 | [Entity capability lifting and the Communication boundary](ARCH-0113-entity-capability-communication.md) | Accepted | Lifecycle/Events/Transport separation, scalar/set/stream law, Core context ownership, and greenfield rebuild map |

## Authoring Principles

1. Decision first – lead with the outcome, then rationale.
2. Cite related ADR IDs (avoid re-stating prior rationale).
3. Capture consequences (positive, negative, neutral) explicitly.
4. Prefer removal over indefinite deprecation when safe (reduces cognitive load).
5. Reference = Intent: inclusion in the solution implies support & maintenance.

## Change Workflow (Summary)

1. Draft ADR using template.
2. Add file under `docs/decisions` with next sequential domain prefix & number.
3. Register in `docs/decisions/toc.yml`.
4. Reference from related ADRs if superseding.
5. Validate doc build (link integrity, anchors).

## Tags Recommendation

When removing sizable code surfaces, prefer a lightweight git tag (e.g., `archive/<area>-<date>`) instead of leaving dead code in-tree.

---
This index is intentionally terse. For pillar-specific entry points see root documentation TOC.
