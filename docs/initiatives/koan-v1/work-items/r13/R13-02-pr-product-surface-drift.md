---
type: SPEC
domain: framework
title: "R13-02 - Reject Product Surface Drift Before Main"
audience: [maintainers, release-engineers, ai-agents]
status: current
last_updated: 2026-07-21
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-21
  status: passed
  scope: real main PR product compile, two-projection byte drift, and workflow ordering
---

# R13-02 — Reject product-surface drift before main

- Status: `passed`
- Depends on: passed R13-01
- Unlocks: R13-03 result-aware admission
- Owner: `.github/workflows/pr-gate.yml` invoking the existing product compiler

## Entry gate

**Application intent:** A maintainer cannot merge source/claim/version changes whose generated public
product truth is stale or whose supported graph is invalid.

**Public expression:** Open a pull request to `main`. The existing PR gate runs the real
`Koan.Packaging product-surface` command and compares both generated projections with their checked-in
files before the ordinary green ratchet. No local application or release command changes.

**Guarantee/correction:** Compiler failure or byte drift in either JSON or Markdown fails the PR and
shows the regeneration command. Passing compiler unit tests without compiling the real repository is
insufficient.

**Complete intent surface:** `pr-gate.yml`, `Koan.Packaging product-surface`, `product/claims.json`,
evaluated active projects, and `docs/reference/product-surface.{json,md}`.

**Public concepts:** None beyond the existing PR gate and generated files.

**Coalescence:** Keep claim validation/generation in `ProductSurfaceCompiler`; keep integration
enforcement in the existing main PR workflow. Do not reproduce compiler rules in YAML or add a release
workflow. Disposition: keep compiler, rebuild the gate's missing invocation, delete no product owner.

**Ergonomics:** One early named PR step reports compile failure or exact file drift and tells humans
and coding models which command repairs it.

## Exact placement and proof

| Change | Location | Reason |
|---|---|---|
| real compile plus two-file comparison | `.github/workflows/pr-gate.yml` | accepted pre-merge integration chokepoint |
| minimal workflow invocation contract | `tests/Koan.Packaging.Tests` | prove the gate calls the real command and checks both outputs without restating its full YAML |

Run the product compiler to temporary outputs, compare both checked-in projections, run focused
packaging tests, and prove a deliberately stale temporary projection fails. Do not run publication or
the full ratchet for this slice.

## Stop conditions

- Stop if the workflow writes generated truth without checking it.
- Stop if validation moves into `dev`, publication, or a second workflow coordinator.

## Implementation and evidence

- `Koan.Packaging product-surface --check` runs the real evaluated compiler, generates both canonical
  projections in memory, and byte-compares them with the checked-in JSON and Markdown.
- Missing or stale output fails with the exact canonical regeneration command; check mode refuses
  output-writing arguments so validation cannot silently repair the checkout it is judging.
- The existing `main` PR workflow runs the real surface check, then the R13-01 public API-baseline
  guard, then the existing green ratchet. The workflow remains read-only and cannot publish.
- Focused tests prove exact match, stale rejection, both canonical paths, and workflow ordering without
  duplicating product-compiler rules in YAML.

## Acceptance result

- Outcome: PASS
- Date and commit: 2026-07-21; intentionally uncommitted local R13 slice
- Application intent and complete public expression: open a pull request to `main`; no application or
  release command changes
- Guarantee / correction: invalid graph/claim/version truth or stale JSON/Markdown fails before the
  merge ratchet and reports the canonical regeneration command
- Coalescence disposition: generation remains in ProductSurfaceCompiler; integration enforcement
  remains one thin step in the existing PR gate
- Ergonomics proof: one `product-surface --check` command works identically in CI and locally
- Evidence: real Release check reports 29 claims, 93 packages, and generated outputs current
- Tests / validation: focused product/compiler/baseline/workflow slice 22/22
- Unsupported scenarios: the command never writes in check mode and performs no publication
- Follow-up work: R13-03 establishes required-result and deadline semantics before native cells rely on it
- Reviewer: pending maintainer review
