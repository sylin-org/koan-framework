---
type: ARCHITECTURE
domain: framework
title: "Koan V1 Reorganization Roadmap"
audience: [architects, maintainers, ai-agents]
status: draft
last_updated: 2026-07-21
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-21
  status: reviewed
  scope: tranche dependencies and exit criteria
---

# Koan V1 Reorganization Roadmap

This file defines dependency order and exit criteria. It intentionally does not track live status;
[`PROGRESS.md`](PROGRESS.md) is the only status ledger.

## Dependency graph

```text
T0 Privacy and memory boundary
  -> T1 Product constitution
      -> T2 Capability truth baseline
          -> T3 Entity Semantics Contract + ecosystem boundaries
              -> T4 Foundation hardening
                  -> T5 Golden V0-to-V1 proof
                      -> T6 Capability-ring graduation
                          -> T7A Semantic Composition Kernel
                              -> T7B Local release-candidate readiness
                                  -> T7C 0.20 public-preview maturity
                                      -> T8 Public provider promotion
```

Feedback may move from later tranches to earlier ones. A later tranche cannot declare an earlier exit
gate satisfied without updating the earlier artifact and evidence.

T8 is allowed to open at R12-06's completed public-baseline checkpoint before T7C fully exits. Its
first dependency-closed wave supplies T7C's final R12-07 upgrade/recovery evidence; this explicit
overlap does not declare the earlier exit gate satisfied in advance.

## T0 — Privacy and memory boundary

**Outcome:** private dogfeeding can inform framework maturity without creating identifying public
artifacts.

**Exit gate:**

- current repository contains no known private identifiers;
- published-history exposure has been assessed;
- the charter contains the private-downstream rule;
- work-item and session templates require privacy review;
- anonymous reproduction is the only route from private finding to public work.

## T1 — Product constitution

**Outcome:** Koan has a concise, reviewable definition of what it is optimizing for.

**Exit gate:**

- positioning, V0, V1, invariants, non-goals, and collaborator posture are approved;
- `Entity<T>` is ratified as semantic spine without becoming an implementation sink;
- meaningful-step and business-density measures are defined;
- canonical architecture documentation is updated through an accepted decision.

## T2 — Capability truth baseline

**Outcome:** public maturity follows evidence rather than project inventory or aspiration.

**Exit gate:**

- every advertised capability has an outcome, semantic surface, evidence, limitation, and maturity;
- claims map to reproducible tests or are qualified/removed;
- foundation, supported-extension, experimental, and retired surfaces are distinguishable;
- private validation is absent from public evidence fields;
- the initial keep/harden, repair/simplify, and incubate/archive disposition is approved.

## T3 — Entity Semantics Contract and ecosystem boundaries

**Outcome:** modules grow the entity language consistently and remain discoverable, inspectable, and
removable.

**Exit gate:**

- participation, IntelliSense, execution, context, lifecycle, projection, capability, explanation,
  and removal contracts are defined;
- compile-time consumer probes are specified;
- host ownership and test isolation requirements are explicit;
- design mining classifies candidate approaches as adopt/adapt/integrate/complement/decline;
- transactional events, history, scoped context, and typed facets have decisions rather than assumed
  roadmap status.

## T4 — Foundation hardening

**Outcome:** shared foundations are dependable before capability breadth increases.

Prioritize:

1. coherent package versions and atomic publication;
2. host-scoped registries, lifecycle, and repeatable integration-test hosts;
3. exact clean-checkout installation and first use;
4. actionable error taxonomy;
5. truthful startup, provider, fallback, and security reporting;
6. composition-lockfile consistency;
7. safe mutation, isolation, and fallback defaults;
8. documentation currentness and executable front-door examples.

**Exit gate:** R04 converts these into dependency-ordered implementation cards with measurable proof;
the highest-risk base cards pass before T5 becomes release-gating.

## T5 — Golden V0-to-V1 proof

**Outcome:** one anonymous application demonstrates meaningful growth without architectural reset.

**Exit gate:**

- each step adds a deployable business capability;
- no step exists solely to create scaffolding;
- the scorecard records concepts, files, business code, plumbing, inspectability, and agent success;
- local-to-production progression preserves business rules;
- clean-checkout automation executes the documented path;
- the journey becomes a release gate rather than a showcase-only sample.

## T6 — Capability-ring graduation

Capabilities graduate in this order unless evidence changes the dependency graph:

1. entity/data/composition/testing;
2. events/context/isolation;
3. production progression: access, concurrency, audit, schema, recovery, telemetry;
4. governed agent operation;
5. optional semantics such as vector, media, advanced caching, and typed facets.

**Exit gate:** each ring meets [`ACCEPTANCE.md`](ACCEPTANCE.md), has a published support boundary, and
does not depend on an ungraduated lower ring.

## T7A — Semantic Composition Kernel

**Outcome:** application intent compiles once through one Semantic Application Model into typed,
host-owned execution plans and truthful projections without exposing framework machinery in normal
business code.

**Exit gate:**

- every capability slice starts from its business sentence and smallest honest C# expression;
- shared contribution/election mechanics have one owner while pillar semantics remain typed;
- Tenancy proves a hard cross-pillar overlay and ZenGarden proves optional layered activation;
- inactive capabilities remain inert, runtime paths execute compiled plans, and separate hosts remain isolated;
- startup, facts, health, errors, agents, and tests project the same canonical decisions;
- superseded registries, cross-pillar shortcuts, and post-hoc decision twins are deleted.

## T7B — Local release-candidate readiness

**Outcome:** source, package topology, samples, release mechanics, and one exact local candidate describe
the same framework before external publication.

**Exit gate:**

- release lineage, exact escrow, package selection, and recovery fail closed locally;
- every maintained sample has graduated golden-example evidence or an explicit non-maintained disposition;
- every active NuGet package earns a distinct reference intent, package-owned orientation, truthful
  metadata, and role-proportional consumer evidence or has a terminal disposition;
- public claims are generated from or linked to repository evidence;
- an exact package-only candidate proves templates, FirstUse, GoldenJourney, and immutable-byte custody;
- no publication occurs implicitly as part of candidate construction.

## T7C — 0.20 public-preview maturity

**Outcome:** the contracts Koan is prepared to guarantee earn the 0.20 version signal and become one
coherent, externally testable public product. Other packages retain truthful lower maturity and version
signals rather than inheriting promotion from repository membership.

**Exit gate:**

- the exact NuGet/SemVer preview, compatibility, support, and feedback contract is explicit;
- each package promoted to 0.20 maps to a supported guarantee and a defensible public dependency boundary;
- preview-blocking runtime, safety, configuration, and lifecycle concerns are fixed, removed, or excluded;
- all public-facing content tells one present-tense greenfield narrative and is guarded against drift;
- the recommended spine, advanced extensions, experiments, and non-claims are unmistakable;
- the coherent preview packages and templates are published and clean-install verified;
- an independent later wave proves ordinary upgrade and mixed-state idempotent publication recovery;
- production guidance covers schema, deployment, recovery, diagnostics, secrets, and security posture;
- external public-context-only readers reproduce the intended journey and their anonymous feedback is triaged;
- the architect receives an explicit go/no-go record for the next maturity band.

## T8 — Public provider promotion

**Outcome:** Koan's maintainer-intended provider families become useful public 0.20 extensions through
shared semantics, provider-specific real-boundary proof, clean package consumption, and package/API
integrity. Historical package counts do not substitute for product intent.

**Exit gate:**

- [ARCH-0120](../../decisions/ARCH-0120-terminal-package-maturity.md) remains the governing promotion
  decision and [R13](work-items/R13-terminal-package-maturity.md) records execution;
- product intent, not reverse dependencies, identifies the intended public adapters;
- every promoted family has shared conformance, provider-specific proof, and a clean consumer journey;
- public Koan dependencies are supported before their consumers and every promoted API follows the
  first-0.20 compatibility policy;
- generated product truth and current provider guidance agree;
- no terminal ledger or generic admission subsystem is required for completion.
