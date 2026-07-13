---
type: ARCHITECTURE
domain: framework
title: "Koan V1 Reorganization Progress"
audience: [architects, maintainers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: reviewed
  scope: initial work-item state and dependency readiness
---

# Koan V1 Reorganization Progress

This is the initiative's only live status ledger. Update it in the same change that starts, blocks,
or completes a work item. The roadmap describes order; it does not report progress.

## Initiative state

- Overall: `active`
- Current tranche: `T1 — product constitution`
- Active work item: `R01`
- Next decision: ratify which product principles are constitutional, directional, tactical, or
  overstated
- V1 readiness: `not assessed`

## Work items

| ID | Work item | Tranche | Status | Depends on | Claim | Evidence / note |
|---|---|---|---|---|---|---|
| R00 | [Establish the privacy boundary](work-items/R00-privacy-boundary.md) | T0 | passed | — | Codex · 2026-07-13 | Published branch tips are clean; operator accepted retained historical residue and declined a disruptive rewrite. |
| R01 | [Ratify the product constitution](work-items/R01-product-constitution.md) | T1 | in-progress | R00 | Codex · 2026-07-13 | Draft charter exists; reconcile it with code-backed architecture and public positioning. |
| R02 | [Build the capability truth baseline](work-items/R02-capability-baseline.md) | T2 | pending | R01 | — | Assessment queue initialized in `CAPABILITIES.md`. |
| R03 | [Define the Entity Semantics Contract](work-items/R03-entity-semantics-contract.md) | T3 | pending | R02 | — | Requires capability evidence and ecosystem comparison. |
| R04 | [Harden the framework foundation](work-items/R04-foundation-hardening.md) | T4 | pending | R03 | — | Backlog must be evidence-ranked before implementation. |
| R05 | [Prove the golden V0-to-V1 journey](work-items/R05-golden-v0-v1-journey.md) | T5 | pending | R04 | — | Anonymous business domain only. |

Allowed status values are `pending`, `in-progress`, `blocked`, `passed`, and `stopped`. Only one work
item should normally be `in-progress`.

## Readiness queue

| Work item | Ready? | Gate |
|---|---|---|
| R00 | passed | Forward-only sanitization and residual-risk acceptance recorded on 2026-07-13. |
| R01 | yes | R00 passed; charter, principles, code, and public overview are available. |
| R02 | no | The product constitution must be canonical enough to classify evidence. |
| R03 | no | Capability baseline and focused ecosystem research must exist. |
| R04 | no | Entity and module boundaries must be decided. |
| R05 | no | The foundation path must be stable enough to measure honestly. |

## Divergence and risk log

| Date | Item | Observation | Disposition |
|---|---|---|---|
| 2026-07-13 | R00 | A current architecture ledger contained an identifying downstream token. | Remove the token and retain only the generic privacy rule. |
| 2026-07-13 | R00 | Current tracked content and paths are clean; 53,203 historical objects contain no identifying paths. | Current-tree and historical-path checks pass. |
| 2026-07-13 | R00 | The predecessor of the sanitized line proves identifying content remains reachable in history. Two bounded, non-emitting full-content traversals did not complete. | Treat exposure as confirmed but extent as inconclusive; stop before history mutation and request an operator disposition. |
| 2026-07-13 | R00 | The operator explicitly authorized rewriting affected published history and force-pushing affected refs. | Create an offline backup, rewrite in an isolated mirror, verify, and push only refs whose object IDs changed. |
| 2026-07-13 | R00 | Precise auditing showed a rewrite would alter all published branches, release tags, and GitHub-managed pull refs; historical object counts also amplified repeated content and generic matches. | Operator chose the proportionate forward-only path: keep all live branch tips clean, accept historical residue, and spend no further initiative energy on rewriting. |
| 2026-07-13 | Initiative | Existing architecture prose contains claims stronger than currently collated evidence. | R02 must classify code, tests, docs, and unsupported scenarios before publication changes. |

## Operator gates

The following actions require a recorded maintainer decision and are not implied by initiative approval:

- rewriting published Git history;
- deleting or renaming public packages or APIs;
- changing compatibility guarantees;
- publishing a V1 date or support claim;
- disclosing any private downstream identity or artifact.

## Session close protocol

Before ending a session:

1. update the active row and link durable evidence;
2. add unresolved disagreement or failure to the divergence log;
3. replace [`NOW.md`](NOW.md) with the exact next safe action;
4. run the verification required by the active card;
5. use `passed` only after [`ACCEPTANCE.md`](ACCEPTANCE.md) is satisfied.
