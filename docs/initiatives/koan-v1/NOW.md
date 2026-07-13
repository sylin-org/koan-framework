---
type: GUIDE
domain: framework
title: "Koan V1 Reorganization Current Handoff"
audience: [maintainers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: reviewed
  scope: R03 Entity semantics and ecosystem-boundary handoff
---

# Koan V1 Reorganization Current Handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Active work

- Work item: [R03 — Define the Entity Semantics Contract](work-items/R03-entity-semantics-contract.md)
- State: `in-progress`
- Objective: define which semantics belong on `Entity<T>`, how modules extend that language without
  IntelliSense clutter, and which ecosystem ideas Koan should adopt, adapt, integrate, complement, or
  decline.
- Foundation: R01 passed through [ARCH-0105](../../decisions/ARCH-0105-product-constitution.md) and the
  canonical [product constitution](../../architecture/product-constitution.md).
- Current state: R02 passed. All 13 surfaces are classified in
  [`CAPABILITIES.md`](CAPABILITIES.md), with reproducible command and claim evidence in
  [`R02-EVIDENCE.md`](R02-EVIDENCE.md). No surface is currently labeled supported.

## Next safe actions

1. Inventory `Entity<T>`, partials, extension methods, attributes, contexts, and namespaces exactly as
   a consuming developer or coding agent sees them.
2. Group the current surface by intrinsic entity semantics, capability facets, infrastructure control
   planes, and application workflows; record collisions and misleading placements.
3. Mine current ABP primary documentation/source first, then only the closest .NET/Rails approaches
   that can change a concrete Koan decision.
4. Draft an Entity admission test, namespace/overload rules, context and backend-selection rules, and
   a disposition matrix: adopt, adapt, integrate, complement, or decline.
5. Prove the draft with one anonymous business domain and one deliberate counterexample kept off Entity.

## Expected working tree

R02 closure adds capability/evidence records and corrects materially unsafe front-door claims. Treat
every other pre-existing change as user-owned.

## Verification at handoff

- every current Entity extension category maps to a proposed contract or remediation item;
- ecosystem conclusions cite current primary sources and change a named Koan decision;
- representative consumer/IntelliSense probes cover ambiguity and discoverability;
- documentation metadata, links, TOC, privacy scan, and `git diff --check` pass;
- no private downstream detail enters evidence or examples.

## Do not infer

- Do not copy ABP, Rails, EF Core, Aspire, or agent frameworks for feature parity.
- Do not put infrastructure control planes on Entity merely to maximize IntelliSense discoverability.
- Do not infer semantic correctness from an existing extension method or partial class.
- Do not implement R04 runtime repairs while R03's semantic contract is still open.
