---
type: ARCHITECTURE
domain: framework
title: "Koan V1 Reorganization Roadmap"
audience: [architects, maintainers, ai-agents]
status: draft
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
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
                              -> T7B V1 release readiness
```

Feedback may move from later tranches to earlier ones. A later tranche cannot declare an earlier exit
gate satisfied without updating the earlier artifact and evidence.

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

## T7B — V1 release readiness

**Outcome:** the public product, package set, upgrade contract, documentation, and actual implementation
describe the same framework.

**Exit gate:**

- atomic packages and templates are published and clean-install verified;
- SemVer and compatibility policy are enforced;
- upgrade rehearsal is green;
- support and provider matrices are current;
- production guidance covers schema, deployment, recovery, diagnostics, and security posture;
- public claims are generated from or linked to repository evidence;
- experimental surfaces cannot be mistaken for foundation;
- every maintained sample has graduated golden-example evidence or an explicit non-maintained disposition;
- the architect makes an explicit V1 release decision.
