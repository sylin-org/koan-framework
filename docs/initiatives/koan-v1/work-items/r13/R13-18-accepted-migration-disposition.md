---
type: SPEC
domain: framework
title: "R13-18 - Dispose accepted cross-repository migrations"
audience: [architects, maintainers, developers, ai-agents]
status: resolved
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: passed
  scope: Public Agyo and Zen Garden destination evidence, Koan ownership/version/claim posture, and transition-safe disposition
---

# R13-18 — Dispose accepted cross-repository migrations

## Decision

Close R13 without retiring the remaining Koan AI vertical. The accepted Agyo and Zen Garden ownership
moves remain valid, but their destination-evidence gate is not met. They continue under the cross-repository
program owned by [ARCH-0089](../../../../decisions/ARCH-0089-ai-pillar-dissolution.md), not as unfinished
0.20 package-promotion work.

This is a transition-safe disposition, not a reversal of ownership:

- do not promote the departing Koan owners to `0.20`;
- do not delete behavior before an equivalent destination and consumer exist;
- do not create forwarding packages or make Koan depend upward on Agyo or Zen Garden;
- resume from public destination evidence, not another Koan-side assessment.

## Public destination evidence

### Agyo

Read-only inspection of public `sylin-org/agyo-tools` default branch `dev` at
`5a202843ea36dc25c701c4b8812129c32cbb7138` (2026-06-21) found:

- `Agyo.Rag`, `Agyo.Rag.Abstractions`, and their focused RAG suite exist;
- RAG contains retrieval evaluation helpers, but no migrated Koan ReAct agent/tool loop or typed
  Orchestration chain owner;
- no standalone Eval or Review project exists;
- NuGet.org contains no package with `Agyo` in its ID, and the intended `Sylin.Agyo.Rag`,
  `Sylin.Agyo.Rag.Abstractions`, `Sylin.Agyo.Eval`, and `Sylin.Agyo.Review` IDs are absent.

Therefore Agents/Orchestration, Eval, and Review have no public destination package or consumer proof.

### Zen Garden

Read-only inspection of public `sylin-org/zen-garden` default branch `dev` at
`3f8885ffd876cbfc557181db560bb0b6f14c9790` (2026-04-18) found substantial resource inventory,
AI routing/catalog, provider, and model-import mechanics. That public tip predates ARCH-0089 and has not
advanced since the R11 handoff assessment. It contains no Hugging Face destination path and no newer
evidence preserving Koan Models' complete pull/convert/quantize/deploy/version behavior and consumer.

Therefore Models and Hugging Face still fail the accepted destination-equivalence gate.

## Koan disposition

`Sylin.Koan.AI.Agents`, `Sylin.Koan.AI.Orchestration`, `Sylin.Koan.AI.Eval`,
`Sylin.Koan.AI.Review`, `Sylin.Koan.AI.Models`, and `Sylin.Koan.AI.Connector.HuggingFace` remain at
truthful `0.17` intent with no supported product claim. They are neither promoted nor removed.

The already-completed local retirements—Training, the legacy Zen Garden connector, and the false Compute
surface—remain retired. No source, package, version, claim, workflow, or sibling repository changes are
required by this disposition.

## R13 consequence

R13's intended public provider families are published, indexed, public-consumer green, and API-baseline
complete. The cross-repository moves have now received their required evidence-based disposition without
holding the 0.20 promotion epic open. S3 and Backup remain shelved and outside this result.
