---
type: SPEC
domain: framework
title: "R12-07 - Prove Preview Evolution"
audience: [architects, maintainers, release-engineers, ai-agents]
status: current
last_updated: 2026-07-21
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-21
  status: pending
  scope: accepted completion contract; execution waits for the first dependency-closed R13 wave
---

# R12-07 — Prove preview evolution

- Tranche: `T7C — 0.20 public-preview maturity`
- Status: `pending`
- Depends on: completed R12-06 and the first dependency-closed publication wave from
  [R13](../R13-terminal-package-maturity.md)
- Unlocks: R12 completion independently of R13's remaining maturity waves
- Owner: public upgrade, interrupted-publication recovery, feedback triage, and the final R12
  go/no-go record

## Meaningful outcome

A later supported 0.20 wave evolves the already public preview without changing its product or
release architecture. An application created from the public template upgrades through ordinary
NuGet resolution and keeps its business expression. A partially completed publication recovers by
rerunning the same immutable `main` job. The resulting public feedback is converted into bounded
evidence, and the maintainer receives an explicit R12 close decision.

This card consumes the first suitable R13 wave; it does not own the 55-package terminal program and
does not keep R12 open until every R13 migration is complete.

## Evidence contract

The selected R13 wave must record:

1. the exact source/merge commit, supported claim closure, package IDs, NBGV versions, and successful
   protected admission checks;
2. an application generated from the already public 0.20 template/package line, its before/after
   package graph, clean restore/build, and the same meaningful runtime result after upgrade;
3. an authorized interruption after at least one immutable package is accepted but before the wave
   completes, followed by correction/rerun of the same `main` publication job and successful
   `--skip-duplicate` convergence without tags, escrow, synthetic commits, or workstation publish;
4. the public-feed observation after indexing, including any install, upgrade, corrective-failure,
   or documentation feedback and its bounded fix/defer/non-claim disposition;
5. a concise R12 go/no-go record stating whether the 0.20 preview contract is coherent enough to
   close and what remains owned by R13 or a later compatibility tier.

Remote interruption is not implied by this planning card. Immediately before execution, revalidate
the exact target, credential boundary, failure injection, observable recovery, and maintainer
authorization required by R12-06 and ARCH-0110.

## Acceptance

R12-07 passes only when:

1. one new dependency-closed supported wave is publicly visible;
2. the public-created application upgrades and preserves its meaningful result without repository
   access or architectural reset;
3. the interrupted publication converges through an ordinary rerun with immutable identities and no
   second recovery system;
4. feedback is recorded as evidence and triaged into bounded action or explicit non-claim;
5. the maintainer accepts the final R12 go/no-go record.

## Stop conditions

- Stop if the selected R13 wave is not independently support-admitted and dependency-closed.
- Stop if recovery requires a new branch, tag, manifest, escrow, synthetic commit, or manual package
  publication.
- Stop before any deliberate remote interruption without exact-target validation and explicit
  maintainer authorization.
- Stop if anecdotal feedback is used to reopen settled architecture without reproducible evidence.
