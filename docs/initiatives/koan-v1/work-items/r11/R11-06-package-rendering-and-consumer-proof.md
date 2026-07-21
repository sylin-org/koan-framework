---
type: SPEC
domain: framework
title: "R11-06 - Prove Package Rendering and Consumer Comprehension"
audience: [architects, maintainers, developers, operators, reviewers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: source-first
validation:
  date_last_tested: 2026-07-19
  status: tested
  scope: remaining package-page truth, exact artifacts, and representative clean-consumer proof
---

# R11-06 — Prove package rendering and consumer comprehension

- Tranche: `T7B — package-product graduation`
- Status: `passed`
- Depends on: passed R11-05 terminal package topology
- Unlocks: R11-07 complete release-certification boundary

## Application intent

> From a NuGet page alone, a developer or coding agent can install a Koan package, write its smallest truthful use,
> understand what reference plus `AddKoan()` activates, and identify the missing dependency or unsupported boundary
> when the result cannot be delivered.

## Discovery and scope

R11-05 closed every architecture disposition. The generated 93-package report leaves exactly 25 presentation findings
on seven packages, all in the already-terminal AI vertical: Agents, Eval, Models, Orchestration, Review, Data.AI, and
the HuggingFace connector. Six legacy pages use pre-prefix titles and omit recognizable result/boundary sections;
five omit technical ownership. HuggingFace has no owned page at all. Several examples advertise signatures absent
from current source—for example in-memory `EvalCase` measurement, fluent Review mutations, parameterless RAG retrieval,
and async-enumerable model pull—so headings alone would preserve false consumer promises.

This child repairs those seven rendered package contracts from current public APIs, modules, tests, and package
dependencies. It does not reopen their R11-02 `keep` decisions, restructure production code, add package metadata,
create a support registry, inspect private applications, or rerun broad AI behavior suites whose dependencies are
unchanged.

## Truth model

Each repaired page must state:

1. exact `Sylin.Koan.*` identity and standard install expression;
2. the shortest current C# result and when a running host is required;
3. the automatic module/reference effect and any deliberate application registration;
4. the provider/capability or durable-data prerequisites;
5. corrective failure and explicit non-guarantees;
6. the runtime owner and lifecycle in a proportional technical companion.

HuggingFace must be especially explicit: it contributes Hub model search/pull management (`ModelList` and `Pull`),
not chat/embed inference. Review must distinguish automatic review infrastructure from the application's explicit queue
definition and caller-owned Entity persistence. Eval must require an adapter with `MetricCompute`. Agents and chains
must identify their AI/Data/Vector prerequisites and read-only-by-default Entity tools. Models must describe
capability-routed operations rather than promise every transform/runtime exists. Data.AI must distinguish on-demand
operations from attribute-selected automatic lifecycle behavior.

## Focused proof plan

- regenerate package quality/product surface and require 93 structurally ready packages with zero findings;
- build and pack only the seven affected owners, inspect exact nupkg rendering/content/dependencies, and audit current
  direct/transitive vulnerability reports;
- compile one clean temporary consumer against the seven project/package surfaces using only the public expressions
  advertised by their pages; no live model, Hub download, provider, or Data service is required;
- run public package-document truth and strict DocFX gates; retain the broad docs linter as a zero-error/non-gating
  warning observation;
- reserve the complete graph, template journeys, live integrations, and release ratchet for R11-07.

## Acceptance

1. generated package quality has no remaining repair/review finding;
2. every repaired snippet names existing public signatures and compiles in the clean consumer;
3. exact artifacts render owned README/icon/metadata and expected dependency boundaries;
4. operator/reviewer/agent language agrees with automatic module provenance and corrective failures;
5. no new maintained list, support claim, production mechanism, or release mutation is introduced.

## Completion evidence

- All 25 remaining generated findings were corrected at their actual owners. Package quality is now 93 packages,
  zero repair-required, zero review-required, 93 structurally ready, and zero findings; product surface remains 93
  packages and 26 claims.
- Agents, Eval, Models, Orchestration, Review, Data.AI, and HuggingFace now use exact package titles/install expressions,
  current public signatures, automatic activation truth, prerequisites, corrective failures, and explicit limits.
  Six missing technical companions were added; Data.AI's existing technical contract was retained and reoriented to
  exact identity.
- False legacy examples were removed: no in-memory `EvalCase` API, fluent Review persistence facade, parameterless
  retrieval, async-enumerable model pull, universal transform/deploy promise, or HuggingFace inference claim remains.
  Review states that application code owns queue declarations, authorization, persistence, concurrency, and audit;
  HuggingFace declares only Hub `ModelList`/`Pull` behavior.
- One clean temporary net10.0 consumer references all seven owners and compiles the advertised Agents, Chain, Eval,
  Models/HuggingFace, Review, and Data.AI expressions with warnings as errors and zero warnings/errors. It performs no
  live provider, model, Hub, Data, or Vector operation.
- All seven Release packs succeed. Archive inspection confirms exact package identities plus README, icon, DLL/XML,
  build-transitive props, symbols packages, repository provenance, and evaluated dependency groups. Current
  direct/transitive vulnerability audits report clean for all seven projects.
- Strict API/full-site DocFX succeeds. Public documentation truth passes 233 current files and 42 navigation targets;
  broad docs lint reports zero errors and 1,624 existing non-gating front-matter/TOC warnings.
- No production code, package topology, claims, support registry, broad AI suite, live integration, full release
  ratchet, private downstream, push, publication, tag, release, deployment, or remote configuration changed. All
  temporary consumer/package artifacts remain under untracked `tmp/` and must not be staged.
