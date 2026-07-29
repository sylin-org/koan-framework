---
type: SPEC
domain: data
title: "DAC-10 Harvest SQLite Provider Lessons"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: SQLite lesson harvest, public-contract inventory, black-box scenarios, and legacy retirement inventory
---

# DAC-10 — Harvest SQLite provider lessons

| Field | Value |
|---|---|
| Phase / kind | gold / harvest |
| Depends on | DAC-09 |
| Unlocks | DAC-15 with DAC-20 |
| Primer scope | SQLite public/alternate surfaces, provider probes, claim facts, and every candidate gold profile |
| Production writes | forbidden; initiative evidence only |
| Owner | SQLite harvester; no implementation authority |

## Meaningful outcome

The current SQLite adapter is exhausted for useful evidence without becoming the design, source material, or starting
tree for its replacement.

## Required work

1. Pin the current adapter, managed/native SQLite drivers, OS/filesystem/architecture, database modes, primer
   fingerprint, and reproducible source identity. This identity describes evidence being harvested, not code to keep.
2. Inventory externally meaningful continuity candidates: package/assembly identity, public types and operations,
   configuration keys, observable outcomes, published claims, and documented corrections. Mark each as observed fact;
   DAC-15 alone decides whether the replacement contract includes it.
3. Probe real SQLite and authoritative provider/driver documentation for native facts: connection and URI modes,
   file creation, locking/concurrency, transactions, parameter binding, value/JSON behavior, identifier grammar,
   schema introspection, query plans, cancellation, error codes, and resource lifecycle. Record each as `L-*` with
   reproducible proof and no implementation prescription.
4. Exercise the current public surface as a black box across Managed/RW, Managed/RO, External/RW, and External/RO.
   Record valid outcomes as provider-neutral scenario specifications; record failures and surprises as regression
   cases or negative lessons. Old test source and fixtures are not transferred.
5. Inspect current source, tests, and relevant history only in the harvester role. Extract performance traps, policy
   bypasses, swallowed failures, duplicate ownership, unbounded state, lifecycle hazards, and approaches not to repeat.
   Do not emit a replacement class graph, file map, helper design, control flow, cache strategy, or code disposition.
6. Build `retirement.json`: every legacy file/content hash, project/compile item, internal type, registration/election
   route, factory, helper, option, fallback, adapter-specific fixture, and test. Separate ratifiable public contract
   names from implementation identities. This inventory is for DAC-15/DAC-23/certification and is forbidden input to
   the replacement author.
7. Split any newly discovered Framework or Relational Family contract gap into a bounded Foundation child card. The
   finding may inform the shared contract; no legacy SQLite code is hoisted or copied into that owner.
8. Publish `evidence/sqlite/harvest/lessons.md`, `compatibility.json`, `black-box.json`, and `handoff.md`, plus
   `evidence/sqlite/restricted/retirement.json` and exact probes. A separate sanitizer verifies that the author-facing
   harvest artifacts contain facts and outcomes only.

## Verification

- Production and shipped tests remain unchanged.
- Every `L-*` fact cites a provider probe or authoritative provider/driver source, not an implementation assertion.
- The black-box corpus describes inputs, outcomes, provider posture, and acceptance IDs without old test/source code.
- The sanitizer rejects implementation-shaped advice, source excerpts, internal type/file references, and inferred
  architectural authority in every prospective rewrite input.
- The retirement inventory is exhaustive under an independent repository and registration-path search.

## Definition of done

- [ ] SQLite lessons and black-box scenarios are reproducible, useful, and implementation-neutral.
- [ ] Public compatibility candidates and Target/Declined questions are explicit for human ratification.
- [ ] Every legacy implementation and implementation-coupled test path is present in the sealed retirement inventory.
- [ ] No KEEP, LOCALIZE, move, port, or partial-remediation plan is handed to DAC-11.

## Stop conditions

Stop if the evidence identity cannot reproduce, a public claim cannot be frozen, a provider fact cannot be separated
from current implementation behavior, or sanitization cannot remove implementation lineage from the author brief.
