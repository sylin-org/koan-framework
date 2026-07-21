---
type: HANDOFF
domain: koan-v1
status: active
last_updated: 2026-07-20
framework_version: v0.20.0
---

# Koan v1 — current handoff

## Current objective

Finish the road to the 0.20 pre-release with product work and a coherent public narrative. Release
automation is infrastructure, not a product capability, and must remain proportionate.

## Active slice: R12-06 bare-bones publication

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

## Remote/public state

- `Sylin.Koan 0.20.4` was accepted by nuget.org from main-boundary run `29790423844`; registry indexing
  may lag acceptance. The run then exposed and stopped on an invalid attempt to publish
  non-guaranteed `Sylin.Koan.AI 0.18.10`. Release selection is now restricted to the guaranteed 0.20
  closure.
- NuGet ownership remains split across `sylin.org` and the historical `sylin-labs` owner. Eighteen
  guaranteed existing IDs require ownership transfer or an old-owner credential; new IDs require
  create-package permission. This is the current external prerequisite.
- No `automation/package-lineage-dev` branch, `release/dev/*` tag, release-wave escrow, or GitHub
  Release was created.
- There is no manual dispatch path. Do not update `main` during local correction because a `main`
  commit is now the publication event.

## Validation posture

- Run focused checks only.
- For this slice, build `tools/Koan.Packaging`, run its remaining focused tests only if required by a
  compile failure, and statically inspect the workflow.
- Do not run the full solution, all test projects, the green ratchet, completed Tenancy/Classification
  suites, or package clean-room applications.
- Do not repeat a passing check unless affected code changes.

## Next actions

1. Commit the corrected main-boundary workflow and documentation to `dev`; this triggers nothing.
2. Open a pull request targeting `main`; the PR gate validates but does not publish.
3. Merge only when publication is intended; observe ordinary pack/push results from that `main`
   commit, correct only a concrete failing owner, and rerun the same workflow run if necessary.
4. Resume product and public-documentation work. Do not expand release infrastructure.

## Repository boundaries

- Preserve unrelated worktree changes and untracked `tmp/`; never stage `tmp/`.
- Do not inspect private dogfood applications.
- Do not update `main`, publish from a workstation, tag, release, or mutate remote configuration while
  correcting the workflow on `dev`.
- Full release certification belongs to an explicitly requested milestone, not normal development or
  a release plumbing correction.
