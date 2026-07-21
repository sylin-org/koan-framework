---
type: HANDOFF
domain: koan-v1
status: active
last_updated: 2026-07-21
framework_version: v0.20.0
---

# Koan v1 — current handoff

## Current objective

Finish the road to the 0.20 pre-release with product work and a coherent public narrative. Release
automation is infrastructure, not a product capability, and must remain proportionate.

## Active slice: SQLite discovery explanation correction completed; package-line alignment proposed

The maintainer rejected the automatic release compiler after multiple long validation cycles exposed
failures in release orchestration rather than framework behavior. The later manual-from-`dev`
replacement also changed the integration boundary without explicit approval. The corrected decision
is recorded in [R12-06](work-items/r12/R12-06-publish-and-observe-first-wave.md) and ARCH-0110:

- commits and pull requests targeting `dev` trigger no validation or publication workflow;
- pull requests targeting `main` run the existing validation gate but cannot publish;
- the resulting `main` commit automatically runs one package publication job;
- each packable project keeps its local NBGV `version.json`;
- the workflow compiles the product surface, runs `dotnet pack` with `PublicRelease=true`, and runs
  `dotnet nuget push --skip-duplicate` only for the 38 guaranteed package owners validated at 0.20,
  using the established `NUGET_API_KEY`;
- no lineage branch, synthetic commits, manifests, escrow, tags, GitHub Releases, recovery ledger,
  duplicated proof lanes, or full test ratchet participates in publication.

The former release subsystem and its implementation-detail tests are removed. Package
inventory, quality assessment, product-surface compilation, NBGV stamping, bounded internal
dependency ranges, and package metadata remain.

Focused implementation evidence:

- `Koan.Packaging` Release build: clean in 2.3 seconds;
- trimmed `Koan.Packaging.Tests` compile: clean in 2.1 seconds; tests were not executed;
- evaluated inventory: 93 independently versioned packages in 10.3 seconds;
- declared product surface: 18 supported claims map to exactly 38 guaranteed package owners, all
  with `versionIntent=0.20`;
- workflow: one `main`-push job, zero test commands, one API-key reference, and no Git mutation or
  legacy release state;
- `Sylin.Koan.Templates` is the one packable project outside `Koan.sln` and has one explicit standard
  pack command in the same job.
- Public `Sylin.Koan.Templates 0.20.6` now uses standard `0.20.*` patch floats. A fresh NuGet.org
  install generated both templates; isolated restores completed without warnings, the web project
  built with zero warnings/errors, and the console passed SQLite Entity save/load/query.
- SQLite startup explanation now follows adapter-owned fallback selection and no longer emits the
  misleading correction path for local auto mode. The fix is tracked as complete in `R12-05`.

## Proposed next slice: align active packages on 0.20

The maintainer wants the next session to assess promoting the remaining non-0.20 projects. The
current generated product surface contains 93 packable projects: 38 at `0.20` and 55 on earlier
lines (one at `0.1`, 43 at `0.17`, ten at `0.18`, and one at `0.19`). Of those 55, 19 belong to
`demonstrated` claims, two to `verified` claims, two to an `experimental` claim, and 32 are
unassessed.

This is a deliberate policy change, not a mechanical version edit. Current guidance, compiler
validation, tests, and publication all equate `0.20` with the supported guarantee. The simplest
candidate model is to make `0.20` the common preview release line for every active packable project
while keeping product maturity independent: availability and a shared release cohort do not turn
`demonstrated`, `verified`, `experimental`, or `unassessed` packages into supported promises. Before
editing the 55 project-local `version.json` files, record and accept one concise architecture
checkpoint for that decoupling, then update the existing product-surface compiler and one `main`
publication job rather than adding another release mechanism.

## Remote/public state

- PR `#93` merged to `main` as `ad9d739199da809fa44efc9a4ce3db8059348b42`. Main-boundary run
  `29792486934` succeeded: product-surface selection resolved the exact 38-package guaranteed 0.20
  closure, packing completed, and NuGet accepted the publication set.
- PR `#94` merged to `main` as `cfb60f848653686278a1976dcacc71386f4cb19e`. Main-boundary run
  `29796113330` succeeded and NuGet.org indexed the corrected `Sylin.Koan.Templates 0.20.6`.
- The historical `sylin-labs` NuGet organization is retired. Ownership of all 166 indexed historical
  Sora and Koan package IDs was preserved under `sylin.org`; the authenticated account reports one
  organization, `sylin.org`, with 240 packages. No packages were deleted or unlisted. Public owner
  search is eventually consistent and may temporarily report stale `sylin-labs` results.
- No `automation/package-lineage-dev` branch, `release/dev/*` tag, release-wave escrow, or GitHub
  Release was created.
- There is no manual dispatch path. Any future `main` commit is a publication event; work on `dev`
  triggers nothing.

## Validation posture

- Run focused checks only.
- For this slice, build `tools/Koan.Packaging`, run its remaining focused tests only if required by a
  compile failure, and statically inspect the workflow.
- Do not run the full solution, all test projects, the green ratchet, completed Tenancy/Classification
  suites, or package clean-room applications.
- Do not repeat a passing check unless affected code changes.

## Next actions

1. Preserve the completed SQLite correction as one focused change before starting package-version
   work; its six modified files are still uncommitted, and `tmp/` remains unrelated and untracked.
2. Assess and record the proposed version-line/maturity decoupling. Do not silently interpret a
   `0.20` version as a support promotion.
3. If accepted, align the remaining 55 active package owners to `0.20`, update the existing
   product-surface invariant and public narrative, and let the existing `main` workflow publish the
   resulting package set. Keep validation to the focused packaging compiler/build/tests; do not
   reintroduce release bureaucracy or run the full ratchet.

## Repository boundaries

- Preserve unrelated worktree changes and untracked `tmp/`; never stage `tmp/`.
- Do not inspect private dogfood applications.
- Do not publish from a workstation, tag, create a GitHub Release, or mutate unrelated remote
  configuration.
- Full release certification belongs to an explicitly requested milestone, not normal development or
  a release plumbing correction.
