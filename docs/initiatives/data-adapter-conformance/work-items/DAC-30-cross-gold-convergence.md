---
type: SPEC
domain: data
title: "DAC-30 Prove Cross-Gold Convergence and the Entity Authoring Loop"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: SQLite/MongoDB differential semantics and provisional Entity workflow
---

# DAC-30 — Prove cross-gold convergence and the Entity authoring loop

| Field | Value |
|---|---|
| Phase / kind | gold / certification and workflow proof |
| Depends on | DAC-12, DAC-22 |
| Unlocks | DAC-40–DAC-58 independent fleet lanes |
| Primer scope | provider-neutral rows common to both gold manifests plus P-06 |
| Production writes | convergence tests, initiative evidence/docs, and inactive CI lane contract only |
| Owner | Framework conformance workflow |

## Meaningful outcome

The same application decisions produce the same Koan semantics on embedded relational and remote document storage,
and the primer/Forge loop is sufficient to author or audit the remaining fleet.

## Required work

1. Build a shared differential corpus from the intersection of certified SQLite/MongoDB claims: value boundaries,
   identities, CRUD/get-many, filters/sorts/pages/counts, policy/lifecycle, common bulk/batch, cancellation/faults,
   diagnostics, mapping, registered operations, and bounded results.
2. Run it independently on both real providers. Compare semantic outcomes and receipts, not physical commands or
   latency. Every difference is an explicit capability/value rule or a defect.
3. Compare responsibility maps. Behavior duplicated in both adapters without native meaning returns to Framework or a
   Family; invalidate all consumers, re-certify the shared seam, update each new adapter, rerun DAC-13/DAC-24/DAC-23,
   and re-certify both golds.
4. Prove the provisional Entity workflow: discover → provider research/probe → claim authority → owner decision →
   ground-up/remediation implementation → strict conformance → packet → public truth. A dummy-adapter exercise must
   succeed without treating another adapter's internals as a template.
5. Define only fail-closed CI lane interfaces and artifact prerequisites. Fleet jobs stay inactive until their cards
   pass; DAC-90 freezes the evidence-backed topology.

## Definition of done

- [ ] Every shared semantic converges or has a ratified capability distinction.
- [ ] No duplicated provider-neutral behavior remains in both gold adapters.
- [ ] A fresh-agent dummy-adapter run completes from the primer, contracts, provider facts, and Forge.
- [ ] PROGRESS opens the independent fleet lanes.

## Stop conditions

Stop for a primer ambiguity, non-reproducible gold certification, second claim/catalog authority, premature CI
activation, or shared-runner change without DAC-03/DAC-50 impact recertification.
