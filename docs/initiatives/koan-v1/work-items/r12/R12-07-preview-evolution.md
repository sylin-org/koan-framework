---
type: SPEC
domain: framework
title: "R12-07 - Prove Preview Evolution"
audience: [architects, maintainers, release-engineers, ai-agents]
status: resolved
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: passed
  scope: public upgrade and idempotent mixed-state publication evidence accepted; maintainer GO recorded
---

# R12-07 — Prove preview evolution

- Tranche: `T7C — 0.20 public-preview maturity`
- Status: `passed — maintainer accepted GO on 2026-07-22`
- Depends on: completed R12-06 and the first dependency-closed publication wave from
  [R13](../R13-terminal-package-maturity.md)
- Unlocks: R12 completion independently of R13's remaining provider families
- Owner: public upgrade, idempotent publication recovery, feedback triage, and the final R12
  go/no-go record

## Meaningful outcome

A later supported 0.20 promotion slice evolves the already public preview without changing its product or
release architecture. An application created from the public template upgrades through ordinary
NuGet resolution and keeps its business expression. A mixed already-published/missing package set
converges through the same immutable `main` job and `--skip-duplicate`; no manufactured registry
failure is required to prove the mechanism. The resulting public feedback is converted into bounded
evidence, and the maintainer receives an explicit R12 close decision.

This card consumes the first suitable R13 promotion slice; it does not own the broader provider
portfolio and does not keep R12 open until every R13 family or migration is complete.

## Evidence contract

The selected R13 promotion slice must record:

1. the exact source/merge commit, supported claim closure, package IDs, NBGV versions, and successful
   protected admission checks;
2. an application generated from the already public 0.20 template/package line, its before/after
   package graph, clean restore/build, and the same meaningful runtime result after upgrade;
3. one ordinary `main` publication run encountering both an already-published immutable identity and
   missing identities, with `--skip-duplicate` allowing the same job to publish the missing set without
   tags, escrow, synthetic commits, workstation publish, or deliberate registry failure;
4. the public-feed observation after indexing, including any install, upgrade, corrective-failure,
   or documentation feedback and its bounded fix/defer/non-claim disposition;
5. a concise R12 go/no-go record stating whether the 0.20 preview contract is coherent enough to
   close and what remains owned by R13 or a later compatibility tier.

ARCH-0110 and ARCH-0121 already define rerun recovery. A deliberate production interruption would add
risk without testing a different mechanism, so it is not part of this card.

## Evidence — 2026-07-22

- PR `#95` merged to `main` as `a8a1bb61b53195ce44bef00024d722862deb949d` after exact lean PR
  gate `29891719299` passed with no tests or containers.
- Main publication run `29891926990` selected the complete 45-package supported closure, packed it,
  encountered the already-public `Sylin.Koan.Templates 0.20.6`, skipped that immutable duplicate, and
  published the missing artifacts successfully. No recovery subsystem or workstation publish ran.
- NuGet.org indexed all seven newly supported owners at their exact first 0.20 versions. A clean
  NuGet.org-only external consumer restored those seven packages, built with zero warnings/errors,
  booted their public expressions, and printed `R13-06|PUBLIC-CONSUMER|PASS`.
- A console application generated from public `Sylin.Koan.Templates 0.20.6` first restored and ran
  with exact `Sylin.Koan 0.20.4` and `Sylin.Koan.Data.Connector.Sqlite 0.20.4`. Ordinary `0.20.*`
  resolution then selected `0.20.5` and `0.20.6`; clean restore/build and the same SQLite Entity
  save/load/query result passed before and after.
- Feedback disposition: the older graph emitted the known misleading SQLite fallback correction;
  the upgraded graph selected the adapter-owned `embedded-default` candidate and removed that false
  diagnostic while preserving behavior. No new restore/build/runtime defect was observed.

**Go/no-go recommendation:** GO. The public 0.20 line upgraded through ordinary NuGet semantics, the
same business expression remained valid, and the one-job publisher converged a mixed immutable set.
R13 completed its provider-family promotion independently.

**Maintainer decision — 2026-07-22:** GO accepted. This closes R12's 0.20 preview-maturity contract.
It does not declare V1 GA, set a V1 support date, or widen the generated supported package surface.

## Acceptance

R12-07 passes only when:

1. one new dependency-closed supported promotion slice is publicly visible;
2. the public-created application upgrades and preserves its meaningful result without repository
   access or architectural reset;
3. a mixed already-published/missing set converges through the ordinary immutable publisher with no
   second recovery system or manufactured failure;
4. feedback is recorded as evidence and triaged into bounded action or explicit non-claim;
5. the maintainer accepts the final R12 go/no-go record.

**Acceptance result:** PASS on 2026-07-22. All five conditions are satisfied, including the
maintainer's explicit GO.

## Stop conditions

- Stop if the selected R13 slice is not independently supported and dependency-closed.
- Stop if recovery requires a new branch, tag, manifest, escrow, synthetic commit, or manual package
  publication.
- Do not manufacture a remote interruption when ordinary mixed-state publication proves the same
  `--skip-duplicate` convergence mechanism.
- Stop if anecdotal feedback is used to reopen settled architecture without reproducible evidence.
