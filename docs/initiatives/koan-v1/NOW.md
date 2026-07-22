---
type: HANDOFF
domain: koan-v1
status: active
last_updated: 2026-07-22
framework_version: v0.20.0
---

# Koan v1 — current handoff

## Current objective

Complete the 0.20 maturity cycle by promoting the maintainer-intended provider families through
small, evidence-backed slices while preserving 0.20 as the supported-contract signal. Historical
package counts do not define completion. Release automation is infrastructure, not a product
capability, and must remain proportionate.

## Completed baseline: first supported wave and public consumer proof

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

## Accepted next epic: value-led 0.20 promotion

[ARCH-0120](../../decisions/ARCH-0120-terminal-package-maturity.md) keeps the useful 0.20 invariant:
supported claim owners use 0.20 intent, their public Koan dependency closure is supported, and every
0.20 owner belongs to a supported claim. It now rejects the larger fixed-owner program that grew
around that invariant.

Product intent—not reverse dependencies—decides whether a package belongs. Database, vector, search,
storage, authentication, and AI adapters are first-class public extensions even when applications
are their only consumers. R13 promotes those capabilities by meaningful family: shared semantics,
provider-specific real-boundary proof, a clean package consumer, and package/API integrity. The
historical 38/55 inventory remains discovery, not an execution ledger or completion count.

The lean [R13](work-items/R13-terminal-package-maturity.md) uses the first seven-owner testing and
operational slice to prove the smaller promotion path. After that publication decision, high-use Data
providers are next, followed by Vector/Search, AI providers, Storage/Media, external Auth, and only
already-decided public migrations. Families may progress independently when no real prerequisite
connects them.

## Exact pause point

The lean process change and first seven-owner slice are merged, published, indexed, and public-consumer
green. R12-07's ordinary public upgrade proof is also green; its GO recommendation awaits explicit
maintainer acceptance rather than another technical exercise.

PostgreSQL R13-07 and SQL Server R13-08 are merged, published, indexed, public-consumer green, and
baseline-captured at exact `0.20.1` versions. The active local slice is
[R13-09](work-items/r13/R13-09-mongodb-provider-promotion.md): MongoDB and the inert Zen Garden contract
foundation have separate supported claims and 0.20 intent. The real provider suite passed 68/68,
focused contract tests passed 45/45 plus 7/7, both packages packed, and a clean staged-package
consumer passed Mongo Entity behavior while proving no runtime provider activation. Generated truth,
API posture, cheap coherence, commit, PR, publication, public indexing, and baseline capture remain.
Untracked `tmp/` is unrelated user-owned material and must remain untouched and unstaged.

## Remote/public state

- PR `#93` merged to `main` as `ad9d739199da809fa44efc9a4ce3db8059348b42`. Main-boundary run
  `29792486934` succeeded: product-surface selection resolved the exact 38-package guaranteed 0.20
  closure, packing completed, and NuGet accepted the publication set.
- PR `#94` merged to `main` as `cfb60f848653686278a1976dcacc71386f4cb19e`. Main-boundary run
  `29796113330` succeeded and NuGet.org indexed the corrected `Sylin.Koan.Templates 0.20.6`.
- PR `#95` merged to `main` as `a8a1bb61b53195ce44bef00024d722862deb949d`. Lean gate
  `29891719299` passed with one job and no tests/containers; release run `29891926990` published the
  45-package supported closure. NuGet.org indexed all seven newly supported owners and their clean
  public consumer passed.
- PR `#96` merged to `main` as `b89cec6266080186db4fdd3fee99aa04b089abbc`. Lean gate
  `29893297175` passed as one job with no tests/containers; release run `29893491621` published
  PostgreSQL and Npgsql `0.20.1`. Both are indexed, and the exact public package consumer passed.
- PR `#97` merged to `main` as `a8d3869adc84d15a330acb52cdf5c7dca916a6ad`. Lean gate
  `29894628337` passed as one job with no tests/containers; release run `29894829655` published SQL
  Server `0.20.1`. It is indexed, and the exact public package consumer passed.
- The historical `sylin-labs` NuGet organization is retired. Ownership of all 166 indexed historical
  Sora and Koan package IDs was preserved under `sylin.org`; the authenticated account reports one
  organization, `sylin.org`, with 240 packages. No packages were deleted or unlisted. Public owner
  search is eventually consistent and may temporarily report stale `sylin-labs` results.
- No `automation/package-lineage-dev` branch, `release/dev/*` tag, release-wave escrow, or GitHub
  Release was created.
- There is no manual dispatch path. Any future `main` commit is a publication event; work on `dev`
  triggers nothing.

## Validation posture

- The smaller slice retains API baselines, generated-surface drift detection, host/container lifecycle,
  reusable family conformance, package-only consumption, and direct provider evidence.
- Focused repairs pass the host failure oracle, Communication 44/44, the affected Data correction 1/1,
  and the real Mongo Web owner 52/52. The package consumer builds the complete local dependency closure;
  its connected clean-network run passed.
- The main PR now runs only product/API agreement, one Release build, lockfile and structural
  documentation/tooling checks. Affected behavior and native evidence run at their owning family;
  complete certification is never inferred from promotion or publication.

## Next actions

1. Compile generated truth and API posture for R13-09, run cheap coherence, and commit the focused
   MongoDB plus inert-contract promotion.
2. Open and merge the next `dev` → `main` PR after the exact lean gate passes; observe the ordinary
   pack/push job and public MongoDB consumer.
3. Record MongoDB and Zen Garden Contracts' exact first public API floors, then select the next
   high-value Entity provider independently.

## Repository boundaries

- Preserve unrelated worktree changes and untracked `tmp/`; never stage `tmp/`.
- Do not inspect private dogfood applications.
- Do not publish from a workstation, tag, create a GitHub Release, or mutate unrelated remote
  configuration.
- Full release certification belongs only to an explicitly requested whole-framework milestone, not
  normal development, merge, promotion, version calculation, publication, or release plumbing.
