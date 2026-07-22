---
id: ARCH-0120
slug: terminal-package-maturity
domain: Architecture
status: Accepted
date: 2026-07-21
title: Value-led promotion to the Koan 0.20 surface
related:
  - ARCH-0091
  - ARCH-0094
  - ARCH-0105
  - ARCH-0109
  - ARCH-0110
  - ARCH-0118
---

# ARCH-0120: Value-led promotion to the Koan 0.20 surface

## Outcome

Koan promotes meaningful public capabilities to 0.20 in cohesive product families. Product intent
decides whether a package belongs; proportionate executable evidence decides whether Koan can support
it; dependency closure only ensures that the promoted package rests on a valid supported foundation.

Database, vector, search, storage, authentication, and AI adapters are legitimate public extensions
even when no other Koan project depends on them. Their natural consumers are applications. A leaf
position in the repository graph is therefore not evidence for retirement or low product value.

Promotion uses fewer, more meaningful moving parts. Koan retains one product-claim source, ordinary
family test projects, standard package/API checks, one generated public surface, and the existing
main-boundary publisher. It does not require a second maturity ledger, a fixed owner-by-owner
reconciliation program, or a generic admission orchestration subsystem.

## Context

The first public 0.20 line deliberately included 38 supported package owners. Repository discovery
also found 55 active owners on earlier version lines. That snapshot exposed useful ambiguity, but it
did not establish that 55 independent bureaucratic decisions were the product outcome.

Many earlier-line owners are provider adapters. They are intentionally depended on by applications
rather than by other framework projects. Counting reverse dependencies, test totals, package rows,
or historical version numbers cannot decide whether those adapters belong in Koan.

The original R13 plan converted the snapshot into ten mandatory waves, a terminal-outcome
certificate, exact claim-cell declarations, candidate applicability planning, and generic result
coordination. Those mechanisms made the promotion process harder to understand than the product
decision they protected. The useful invariant was smaller: a supported claim, 0.20 version intent,
and supported public dependency closure must agree.

## Decision

### 1. Product intent is explicit and primary

The maintainer-owned product surface decides which capabilities Koan intends to publish. A package
belongs when it is the clearest owner of an intended user capability or provider integration.

- Provider adapters do not need in-repository consumers to justify publication.
- A package is not retained merely because another package depends on it.
- Publication, repository presence, and test count do not create a support promise.
- Absorption, migration, and retirement remain available for known duplication or an accepted
  external product home; they are not mandatory classifications for every historical package.

`product/claims.json` remains the single support-intent source. A real capability claim may own one
package or a cohesive family. If grouped providers mature at different rates, split the claim into
honest provider outcomes instead of holding back a ready adapter or weakening the guarantee.

### 2. Promote by user-facing family

The normal unit of work is a capability family, not a package row and not a predetermined wave:

- Entity data providers;
- storage, backup, and media;
- vector and search providers;
- AI runtime and providers;
- authentication providers;
- framework testing and operational extensions.

Foundation work precedes an adapter only when the adapter actually depends on it. Otherwise families
may progress and publish independently according to user value and evidence readiness.

### 3. One promotion contract

A retained package or cohesive family moves to `supported-foundation` or `supported-extension` and
project-local 0.20 intent in the same change only when all five conditions hold:

1. **Public guarantee:** the claim states the user outcome, provider limits, explicit non-claims, and
   corrective behavior in package-owned documentation.
2. **Family behavior:** the shared semantic/conformance suite passes, together with the smallest
   provider-specific delta that distinguishes the implementation.
3. **Real boundary:** external providers run against a real container, local runtime, or deterministic
   wire-contract service appropriate to the guarantee; unavailable infrastructure fails or reports
   honest non-applicability rather than a false pass.
4. **Consumer use:** a clean external project can restore the package, compose a normal `AddKoan()`
   application, and reach one meaningful result without repository project references.
5. **Package integrity:** the package packs cleanly, its public Koan dependency closure is already
   supported, its API baseline policy is active, and generated product truth is current.

The promotion pull request records the exact commands and results. Permanent product truth names the
owning test projects and consumer evidence; it does not reproduce individual test cases as central
workflow metadata.

### 4. Evidence is proportional to owner shape

| Owner shape | Meaningful proof |
|---|---|
| Contracts | compile and pack; intentional dependency shape; inert reference behavior; API review/baseline; one real consumer compile |
| Runtime module | real `AddKoan()` host; selected activation and unselected inertness; configuration precedence; corrective failure; lifecycle where state exists |
| Provider adapter | shared family semantics; provider-specific behavior and limits; real container/runtime/protocol boundary; readiness and cleanup; clean consumer |
| Projection/tooling | real host or user action; authorization/serialization boundary; corrective response; clean consumer |
| Known migration/retirement | destination behavior is public and green; consumer ownership is explicit; old public guidance is corrected |

Existing family test kits are extended. Koan does not create one bespoke framework per adapter or a
universal admission framework above ordinary test projects.

### 5. Dependencies are a safety constraint, not a value score

The product compiler retains the bidirectional 0.20 invariant:

- every supported claim owner has project-local 0.20 intent;
- every public Koan dependency of a supported owner is supported;
- every package with 0.20 intent belongs to a supported claim.

This law prevents an adapter from publishing on an unsupported foundation. It does not require any
other Koan package to depend on that adapter.

### 6. Keep the promotion path small

The durable promotion path consists of:

1. `product/claims.json` for product intent, guarantee ownership, and evidence locations;
2. package-owned README guidance and non-claims;
3. existing family test projects and provider-specific native/protocol tests;
4. `PackageValidationBaselineVersion` for supported assembly API evolution;
5. `ProductSurfaceCompiler` for claim/version/dependency agreement and generated projections;
6. one clean package-only consumer proof;
7. the existing pull-request validation boundary and main-boundary publisher.

Exact per-test admission declarations, a generic TRX admission coordinator, native-candidate planning,
and a fixed terminal-outcome certificate are not architectural requirements. Failures discovered by
ordinary tests should be repaired at their existing family owner rather than elevated into another
repository-wide subsystem.

### 7. Publish value in small slices

Promotion order follows user value and actual prerequisites, initially:

1. high-use Entity data providers;
2. vector and search providers;
3. AI runtime and Ollama, LM Studio, and ONNX providers;
4. storage, backup, and media;
5. external authentication providers;
6. only the already accepted cross-repository migrations and retirements.

Each ready family may publish and be observed independently. A family does not wait for unrelated
historical owners, and R13 does not require the entire repository to converge before delivering its
next useful provider.

## Current Wave 0 disposition

The draft Wave 0 work proved useful testing and package behavior, but it was implemented under the
broader original plan. Before merge, separate the durable value from the superseded bureaucracy.

Retain when independently justified:

- host/container lifecycle corrections;
- reusable Cache and Web adapter conformance;
- package-only consumer proof;
- public API baselines and generated-surface drift detection;
- the seven package promotions and their actual owner/provider evidence;
- the SQLite behavior and explanation corrections.

Remove or collapse unless a smaller existing owner genuinely needs them:

- the fixed 55-owner terminal-outcome certificate and reconciler;
- central exact-cell declarations in product claims;
- generic admission runner/planner/result models added only for R13;
- exact-candidate native applicability orchestration beyond a normal required native workflow;
- documentation and work-card ceremony whose only purpose is administering the former ten-wave plan.

## Acceptance

R13 succeeds when:

1. the maintainer-intended public package families are represented by honest supported claims;
2. each promoted family passes its proportionate semantic, provider-specific, consumer, package, and
   API evidence;
3. every promoted package and its public Koan dependency closure agree on the 0.20 support signal;
4. generated public guidance clearly identifies supported providers and their limits;
5. the promoted packages are observable from the public feed through clean consumer journeys;
6. known migrated or retired packages have completed public ownership correction;
7. no terminal ledger, universal admission framework, or mandatory owner-count reconciliation is
   required to declare the valuable public surface successful.

## Consequences

### Positive

- Provider adapters are evaluated as products rather than mistaken for unused leaf nodes.
- Work delivers public integrations sooner and in independently valuable slices.
- Evidence remains strong while living at the family that understands the semantics.
- The 0.20 signal remains compiler-enforced without a second maturity system.
- Maintainers and contributors can explain promotion in one short checklist.

### Tradeoffs

- Not every historical package receives a formal terminal label.
- Family priorities require explicit maintainer product judgment rather than an automatic graph order.
- A grouped provider claim may need to split when implementations mature at different rates.
- Native provider proof remains slower than unit testing and must be run when the guarantee requires it.

### Guardrails

- Do not infer product value from reverse dependencies.
- Do not promote a package from publication, version proximity, test count, or package-row presence.
- Do not weaken a provider guarantee to make a family appear uniformly ready.
- Do not create a new coordination layer when an existing test project, package owner, product claim,
  or workflow is the natural home.
- Reserve the complete release ratchet for an explicit certification or publication boundary.

## References

- [ARCH-0094 — Adapter Forge](ARCH-0094-adapter-forge.md)
- [ARCH-0105 — Koan product constitution](ARCH-0105-product-constitution.md)
- [ARCH-0109 — Bounded bootstrap test lanes](ARCH-0109-bounded-bootstrap-test-lanes.md)
- [ARCH-0110 — Main-boundary independently versioned package releases](ARCH-0110-main-release-boundary.md)
- [ARCH-0118 — Evidence-derived product surface](ARCH-0118-evidence-derived-product-surface.md)
- [R13 — Promote the meaningful public surface to 0.20](../initiatives/koan-v1/work-items/R13-terminal-package-maturity.md)
