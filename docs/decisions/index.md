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
| ARCH-0114 | [Layered capability activation](ARCH-0114-layered-capability-activation.md) | Accepted | Inert declaration, Reference = Intent activation, concern-owned election, adapter interpretation, and inspectable outcomes |
| ARCH-0115 | [Semantic Application Model and typed contribution compilation](ARCH-0115-semantic-contribution-compilation.md) | Accepted | Business-to-code design input, specificity cascade, typed contribution compiler, immutable host plans, and truthful projections |
| ARCH-0116 | [One module lifecycle](ARCH-0116-one-module-lifecycle.md) | Accepted | One retained `KoanModule` per implementation assembly; standard identity, isolated contracts, and no legacy or reference-metadata bridge |
| ARCH-0117 | [Safe connector telemetry by construction](ARCH-0117-safe-connector-telemetry.md) | Accepted | One credential grammar and structured log boundary; shared configuration/discovery narration; no application ceremony or global logger interception |
| ARCH-0118 | [Evidence-derived product surface](ARCH-0118-evidence-derived-product-surface.md) | Accepted | Standard project facts plus one irreducible claims input compile every human, agent, and release projection |
| ARCH-0120 | [Value-led promotion to the Koan 0.20 surface](ARCH-0120-terminal-package-maturity.md) | Accepted | Product intent selects meaningful public families; provider, consumer, dependency, and API evidence promote them with no second maturity or admission subsystem |
| ARCH-0121 | [Claim-scoped validation and a cheap main boundary](ARCH-0121-claim-scoped-validation.md) | Accepted in part | Claim-owned evidence and cheap PR coherence remain; ARCH-0124 replaces its release boundary |
| ARCH-0122 | [Dogfood-derived runtime control and deterministic test seams](ARCH-0122-dogfood-runtime-and-test-seams.md) | Accepted | Exact SSE intent, AI source lifecycle and inspection, public deterministic Jobs testing, and concise agent workflow |
| ARCH-0124 | [One stable release train for all active packages](ARCH-0124-single-package-release-train.md) | Accepted in part | Complete active inventory, explicit tag, and certify-once artifact handoff remain; ARCH-0125 replaces its single version authority |
| ARCH-0125 | [Each package owns its version on one shared compatibility train](ARCH-0125-per-project-package-versions.md) | Accepted | Project-local version ownership, a per-package version manifest, and publication that skips unchanged packages |
| ARCH-0126 | [A process-global resource is owned by a process-derived fact](ARCH-0126-process-global-resource-ownership.md) | Accepted | Standard output ownership resolved from the process before composition; capabilities observe it rather than claim it |
| ARCH-0127 | [Connector fleet strategy — capability without infrastructure](ARCH-0127-connector-fleet-strategy.md) | Accepted | Connectors ranked by capability added per unit of infrastructure imposed; a connector without an existing conformance oracle is not delegable |
| ARCH-0128 | [Environment posture is a named decision, not a boolean read](ARCH-0128-environment-posture-is-a-named-decision.md) | Accepted | Capabilities gate on a named decision (`KoanEnv.Gate`), not on `IsDevelopment`/`IsProduction`; Production is the gate and consent is the unlock, except where nothing may unlock |
| DATA-0113 | [Bulk reads use the strongest strategy the provider supports](DATA-0113-bulk-reads-use-the-strongest-strategy.md) | Accepted | Bulk-consumer source reads have one owner; amends only the consumer-inheritance clauses of DATA-0107 and DATA-0108 |

## Authoring Principles

1. Decision first – lead with the outcome, then rationale.
2. Cite related ADR IDs (avoid re-stating prior rationale).
3. Capture consequences (positive, negative, neutral) explicitly.
4. Prefer removal over indefinite deprecation when safe (reduces cognitive load).
5. Reference = Intent: an application reference activates a module's contribution; repository or
   solution membership alone implies no maturity or support promise.
6. **Precedence points forward.** A decision that is no longer in force must say what took over, in
   front matter *and* in a leading `> **...**` banner — front matter is invisible to someone reading the
   body. Use `superseded_by:` when the decision is fully replaced, `amended_by:` when it still stands and
   a later decision replaces only part of it, and `superseded_by: none` when the capability was removed
   outright. When a newer decision declares `supersedes:`, the older one must point back at it; a
   one-directional link leaves a reader stranded on dead guidance that reads as live.
7. **An ADR id is issued once.** Allocate the next unused number for the domain; never reuse one. If a
   collision has to be repaired, renumber the less-referenced side, keep the old number in `former_id:`,
   and say so in a banner so an older reference is still findable.

`docs-lint` enforces 6 and 7 as errors, so neither can drift back in. It exists because a retired
decision that named no successor was cited as live policy in a shipped ADR (see ARCH-0128).

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
