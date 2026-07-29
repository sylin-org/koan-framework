---
type: GUIDE
domain: data
title: "DAC-90 Reconcile Fleet Truth and Freeze the Delivery Workflow"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: fleet claims, evidence, documentation, and CI reconciliation prompt
---

# DAC-90 — Reconcile fleet truth and freeze the delivery workflow

| Field | Value |
|---|---|
| Phase / kind | closure / truth, workflow, and CI freeze |
| Depends on | every dynamically discovered adapter evaluation and required remediation |
| Primer scope | all manifests and packet rows |
| Production writes | only executable claim declarations, generated facts/product surface, connector README/TECHNICAL files, CI definitions, and initiative evidence; all runtime behavior forbidden |
| Owner | Framework conformance publication |

## Meaningful outcome

Every capability a developer can discover means the same thing and resolves to current executable evidence, and the
author workflow/CI topology is frozen only after both Entity and Vector fleets have proved it.

## Execute

1. Rerun dynamic discovery. Reconcile every shipped Data/Vector adapter, family package, test suite, docs page, and
   product claim with the DAC roster; create evaluation cards for anything newly found before continuing.
2. Validate every evidence packet and immutable audit identity. No existing maturity label or historical R13 evidence
   counts unless it satisfies the exact current primer/profile evidence kind. Run impact validation and reopen every
   packet whose consumed owner/path/tool/profile/fixture fingerprint changed, regardless of card order.
3. Project runtime capabilities, TestKit applicability, diagnostics/facts, packet scorecards, READMEs/technical docs,
   and product maturity from the single executable claim declaration wherever feasible.
4. Produce a claim decision set for every unsupported current claim, new Target, proposed withdrawal/downgrade, and
   non-shipping disposition. Stop for explicit human product approval. Apply only approved public-truth changes under a
   new pinned identity; do not change adapter behavior or silently narrow the evaluated manifest.
5. Detect orphan packages, undocumented adapters, unreferenced evidence, stale generated files, family claims without
   provider proof, and provider claims without LIVE evidence.
6. Re-run the adapter-author workflow against the completed Entity and Vector evidence, remove provisional steps, and
   freeze one versioned workflow back to the primer and Forge.
7. Encode, activate, and exercise CI lanes: dockerless PR, strict SQLite+Mongo merge, networked nightly/release, and
   heavy-provider execution. Each required lane fails closed when its provider/evidence prerequisite is absent.
8. Update `PROGRESS.md` and `NOW.md` with a frozen roster and handoff to DAC-99; do not certify the portfolio here.

## Verification

- Clean regeneration produces no diff; tampering with a claim/evidence link makes validation red.
- Docs lint, package/product-surface checks, every packet validator, and representative CI lane dispatches pass.
- A machine-readable report has zero orphan, unsupported, stale, or skip-green entries.

## Definition of done

- [ ] Every discovered adapter has a green packet or explicit non-shipping disposition.
- [ ] Runtime, tests, diagnostics, docs, and product surface derive from one claim truth.
- [ ] All CI tiers exist and fail closed for missing required evidence.
- [ ] The final versioned author workflow is proven by both Entity and Vector adapter families.
- [ ] Every claim narrowing/public disposition has explicit recorded human approval.
- [ ] The frozen roster and packet index are ready for an independent certifier.

## Stop conditions

Any undiscovered adapter, unresolved RED/DEFER shipping claim, unverifiable evidence identity, required skipped lane, or
need to modify runtime behavior sends work back to a bounded remediation/evaluation card.
