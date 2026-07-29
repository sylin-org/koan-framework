---
type: SPEC
domain: data
title: "DAC-20 Harvest MongoDB Provider Lessons"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: MongoDB lesson harvest, public-contract inventory, black-box scenarios, and legacy retirement inventory
---

# DAC-20 — Harvest MongoDB provider lessons

| Field | Value |
|---|---|
| Phase / kind | gold / harvest |
| Depends on | DAC-09 |
| Unlocks | DAC-15 with DAC-10 |
| Primer scope | MongoDB public/alternate surfaces, provider probes, claim facts, and every candidate gold profile |
| Production writes | forbidden; initiative evidence only |
| Owner | MongoDB harvester; no implementation authority |

## Meaningful outcome

The current MongoDB adapter yields provider and user-contract lessons while contributing no source, structure, or
preferred seam to the replacement.

## Required work

1. Pin the current adapter, MongoDB server image/version/topology, driver, fixture, least-privilege roles, primer
   fingerprint, and reproducible source identity. Exercise standalone and transaction-capable topology when facts differ.
2. Inventory externally meaningful continuity candidates: package/assembly identity, public types and operations,
   configuration keys, observable outcomes, published claims, and documented corrections. Record facts, not automatic
   preservation decisions; DAC-15 owns the replacement contract.
3. Probe real MongoDB and authoritative server/driver documentation for native facts: client/pool lifecycle,
   database/collection addressing, BSON/value behavior, filter/sort/projection translation, bulk/conditional writes,
   transactions and commit ambiguity, indexes/TTL, topology/permission discovery, cursors, cancellation, and exact
   error categories. Record each as `L-*` with reproducible proof and no implementation prescription.
4. Exercise the current public surface as a black box across all four source postures and each claim-relevant topology.
   Convert useful behavior, failures, and deployment surprises into implementation-neutral scenario specifications.
   Old test source and fixtures are not transferred.
5. Inspect current source, tests, and relevant history only in the harvester role. Extract negative lessons around
   lifecycle authority, swallowed index/abort failures, message-text inference, static topology claims, unbounded
   collection state, policy bypass, and client fallback. No current document-store seam, class, helper, codec, filter,
   bulk, pooling, or cache design is recommended to the replacement author.
6. Build `retirement.json`: every legacy file/content hash, project/compile item, internal type, registration/election
   route, factory, helper, option, fallback, adapter-specific fixture, and test. Separate ratifiable public contract
   names from implementation identities. The replacement author cannot consume this inventory.
7. Split any newly discovered Framework or Document Family contract gap into a bounded Foundation child card. Desired
   placement follows the primer and ratified contract, never duplication or structure observed in the old adapters.
8. Publish `evidence/mongodb/harvest/lessons.md`, `compatibility.json`, `black-box.json`, and `handoff.md`, plus
   `evidence/mongodb/restricted/retirement.json` and exact probes. A separate sanitizer verifies that author-facing
   harvest artifacts contain only facts and outcomes.

## Verification

- Production and shipped tests remain unchanged.
- LIVE facts execute against every claim-relevant pinned topology and role; missing infrastructure is not green.
- Every `L-*` fact cites a provider probe or authoritative provider/driver source, not an implementation assertion.
- The sanitizer rejects source excerpts, internal structures, preferred legacy seams, and implementation-shaped advice.
- The retirement inventory is exhaustive under an independent repository and registration-path search.

## Definition of done

- [ ] MongoDB lessons and black-box scenarios are reproducible, topology-aware, and implementation-neutral.
- [ ] Public compatibility candidates and Target/Declined questions are explicit for human ratification.
- [ ] Every legacy implementation and implementation-coupled test path is present in the sealed retirement inventory.
- [ ] No KEEP, LOCALIZE, move, port, or partial-remediation plan is handed to DAC-21.

## Stop conditions

Stop if a claim-relevant topology or role cannot run, the evidence identity cannot reproduce, a provider fact cannot be
separated from current implementation behavior, or sanitization cannot remove implementation lineage from the brief.
