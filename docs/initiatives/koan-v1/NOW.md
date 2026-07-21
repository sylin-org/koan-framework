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

## Active slice: R12-06 published; public-feed observation next

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

- PR `#93` merged to `main` as `ad9d739199da809fa44efc9a4ce3db8059348b42`. Main-boundary run
  `29792486934` succeeded: product-surface selection resolved the exact 38-package guaranteed 0.20
  closure, packing completed, and NuGet accepted the publication set.
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

1. Perform one focused public-feed consumer observation against the live 0.20 packages: restore the
   documented first-use path from NuGet and record only concrete failures.
2. Reconcile R12-05's pre-publication language with the completed minimal release; do not revive the
   removed release compiler or full certification bureaucracy.
3. Resume product and coherent public-documentation work. Do not expand release infrastructure.

## Repository boundaries

- Preserve unrelated worktree changes and untracked `tmp/`; never stage `tmp/`.
- Do not inspect private dogfood applications.
- Do not publish from a workstation, tag, create a GitHub Release, or mutate unrelated remote
  configuration.
- Full release certification belongs to an explicitly requested milestone, not normal development or
  a release plumbing correction.
