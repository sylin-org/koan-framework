---
type: SPEC
domain: framework
title: "R13-05 - Reconcile the Fixed Terminal Owner Baseline"
audience: [maintainers, architects, ai-agents]
status: current
last_updated: 2026-07-21
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-21
  status: passed
  scope: real 55-row ADR parse, empty partial certificate, strict final and mutation contracts
---

# R13-05 — Reconcile the fixed terminal-owner baseline

- Status: `passed`
- Depends on: passed R13-04
- Unlocks: complete pre-Wave-0 bootstrap and Wave 0 opening
- Owner: bounded R13 certificate plus a reconciler explicitly outside ProductSurfaceCompiler

## Entry gate

**Application intent:** A maintainer can prove that none of ARCH-0120's 55 baseline package owners
vanishes between active support and accepted removal outcomes.

**Public expression:** No application code. A removed baseline owner adds one bounded certificate entry
with disposition, destination when applicable, exact public commit, runnable commands, and evidence.
Active supported owners remain visible only through the existing compiled product surface.

**Guarantee/correction:** The reconciler reads exactly the fixed ARCH-0120 55-row table, the compiler-
validated active supported graph, and removed-owner entries. It rejects unknown, duplicate,
still-active removal entries, malformed disposition/evidence/provenance, or final missing owners. The
bootstrap accepts an empty structurally valid certificate in partial mode; explicit final R13
certification requires complete reconciliation.

**Complete intent surface:** ARCH-0120 table; active evaluated projects and product claims; the bounded
JSON certificate; partial bootstrap verification; explicit final strict verification.

**Public concepts:** The four accepted terminal dispositions and exact public evidence already defined
by ARCH-0120. No live status or package maturity is added to the certificate.

**Coalescence:** Keep active package truth entirely in ProductSurfaceCompiler. Add a separate bounded
R13 service/command because removed projects cannot enter active evaluation. Derive the baseline from
the accepted ADR rather than maintaining another 55-owner list. Disposition: keep active compiler;
create bounded reconciler; delete nothing until a wave proves removal.

**Ergonomics:** Partial output names resolved and remaining counts during the epic; final mode names
every missing/duplicate owner. The certificate stays small because supported active packages never
enter it.

## Exact placement

| Change | Location | Reason |
|---|---|---|
| empty bounded certificate | `docs/initiatives/koan-v1/R13-TERMINAL-OUTCOMES.json` | accepted fixed-epic evidence path |
| certificate/reconciliation model and command | new focused model/service under `tools/Koan.Packaging` plus `Program.cs` | separate from active ProductSurfaceCompiler while reusing evaluated truth |
| path/schema constants | `PackagingConstants` | stable identifiers centralized |
| fixed-baseline/partial/final mutation tests | `tests/Koan.Packaging.Tests` | closest compiler-policy boundary |

## Stop conditions

- Stop if the certificate becomes a live maturity ledger or product compiler input.
- Stop if the 55-owner baseline is silently duplicated or future packages are rejected by this fixed epic.
- Stop if final mode can pass with a missing or duplicate baseline owner.

## Implementation and evidence

- `R13-TERMINAL-OUTCOMES.json` is an empty schema-1 certificate bound to ARCH-0120. It contains no
  active-package state and will grow only when a baseline owner actually leaves the packable graph.
- `TerminalOutcomeReconciler` parses the accepted ADR's numbered table at runtime and validates its
  exact 55 contiguous, unique owners; no second package list exists in tooling or the certificate.
- Active resolution comes only from compiler-validated supported claims. Certificate entries are
  rejected when unknown, duplicated, still active, `supported`, missing a required destination,
  missing runnable commands/evidence, or lacking an exact lowercase public commit.
- Partial mode reports progress throughout the epic and is now an early `main` PR gate before the
  green ratchet. Explicit `--final` alone requires every fixed-baseline owner exactly once; future
  packages remain outside the bounded R13 comparison.

## Acceptance result

- Outcome: PASS
- Date and commit: 2026-07-21; intentionally uncommitted local R13 slice
- Application intent and complete public expression: no application surface; removed owners add one
  bounded evidence entry while retained supported owners stay solely in product truth
- Guarantee / correction: malformed/unknown/duplicate/still-active removals fail; final mode names
  every unresolved baseline owner
- Coalescence disposition: separate bounded reconciler, direct ADR parse, no product-compiler input
  and no duplicated 55-owner registry
- Ergonomics proof: real partial command reports 0/55 resolved and the exact 55 remaining owners
- Tests / validation: fixed-baseline/partial/final/mutation/workflow tests 13/13; aggregate bootstrap
  tooling slice 65/65; Release tool build has zero warnings/errors
- Strict proof: the real `--final` command fails with all 55 unresolved owners, as required before
  any package maturity wave has executed
- Unsupported scenarios: future package discovery is not rejected; active supported owners never
  enter the certificate; no certificate entry is accepted before the project leaves active packing
- Follow-up work: pre-Wave-0 bootstrap is complete; open Wave 0 under the parent execution contract
- Reviewer: pending maintainer review
