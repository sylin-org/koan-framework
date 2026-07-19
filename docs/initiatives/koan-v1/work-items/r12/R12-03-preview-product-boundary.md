---
type: SPEC
domain: framework
title: "R12-03 - Compile the Preview Product Boundary"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-19
  status: in-progress
  scope: exact supported claims, package admission, dependency closure, and 0.20 intent
---

# R12-03 — Compile the preview product boundary

- Tranche: `T7C — 0.20 public-preview maturity`
- Status: `in-progress`
- Depends on: passed R12-01 version/admission contract and passed R12-02 blocker dispositions
- Unlocks: R12-04's coherent greenfield public narrative
- Owner: one generated, evidence-derived product boundary connecting guarantees to exact package owners

## Meaningful outcome

Koan has one small recommended 0.20 spine and a legible set of supported extensions. Every promoted
package owns an accepted guarantee and has a completely admitted public Koan dependency closure.
Available but non-promoted packages remain visible as demonstrated, experimental, or unassessed; they
do not inherit support from proximity or build success.

## Guardrails

- Start from accepted claims and user journeys, not from the package list.
- Promote every owner required to keep an admitted guarantee true, and only those owners.
- Set admitted owners to project-local `"version": "0.20"`; NBGV owns exact patches.
- Preserve independent lineage and the existing mixed-maturity dependency policy.
- Generate/check the boundary from repository truth; do not introduce a maintained package allowlist.
- Do not rewrite the public curriculum here; R12-04 consumes the accepted boundary.
- Use focused graph/version/packaging evidence. The next full candidate belongs after narrative convergence.

## Discovery order

1. Reconcile the 35-package R12-01 assessment slate with R12-02's terminal PMC decisions.
2. Define the smallest supported-foundation claim and the independently useful supported-extension claims.
3. Compute every claim's exact public Koan dependency closure and resolve any maturity leak.
4. Present the exact package/claim/version checkpoint before editing `version.json`.
5. Apply `0.20` only to admitted owners and prove generated product truth, dependency bands, and focused packs.

## Stop conditions

- Stop if a package is promoted only because another package depends on it.
- Stop if a guarantee requires an excluded or unassessed dependency without first admitting that dependency's contract.
- Stop before any version edit until the exact claim/package checkpoint is recorded.
- Stop before public narrative rewriting, publication, tags, pushes, or remote configuration.
