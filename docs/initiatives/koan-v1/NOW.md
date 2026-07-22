---
type: HANDOFF
domain: koan-v1
status: active
last_updated: 2026-07-21
framework_version: v0.20.0
---

# Koan v1 — current handoff

## Current objective

Complete the 0.20 maturity cycle by bringing every remaining earlier-line package owner to a
terminal, evidence-backed decision while preserving 0.20 as the supported-contract signal. Release
automation is infrastructure, not a product capability, and must remain proportionate.

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

Draft PR [#95](https://github.com/sylin-org/koan-framework/pull/95) contains the first seven promotion
candidates. It retains reusable conformance, lifecycle, API, compiler-drift, clean-consumer, and package
corrections. The terminal certificate, central exact-cell metadata, generic admission coordination,
and native candidate-planning machinery have been removed under the amended ADR.

The superseded machinery is gone, the PR narrative is value-led, and focused retained evidence is
complete. The first complete PR ratchet exposed three evidence-owner defects: incomplete host startup
was stopped before disposal and masked the original correction, Mongo database isolation lost the
container authentication source, and the clean consumer packed candidates without their newer local
dependency closure. Those owner-local repairs are implemented and focused validation is green; the
final PR ratchet is the remaining pre-merge check. Untracked `tmp/` is unrelated user-owned material
and must remain untouched and unstaged.

## Remote/public state

- PR `#93` merged to `main` as `ad9d739199da809fa44efc9a4ce3db8059348b42`. Main-boundary run
  `29792486934` succeeded: product-surface selection resolved the exact 38-package guaranteed 0.20
  closure, packing completed, and NuGet accepted the publication set.
- PR `#94` merged to `main` as `cfb60f848653686278a1976dcacc71386f4cb19e`. Main-boundary run
  `29796113330` succeeded and NuGet.org indexed the corrected `Sylin.Koan.Templates 0.20.6`.
- Draft PR `#95` targets `main` from `dev`. It publishes nothing while unmerged and now follows the
  amended value-led ARCH-0120 boundary.
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
  the connected CI runner owns its final clean network restore.
- The final PR ratchet is now proportionate because the draft is intentionally approaching a possible
  main/publication decision. It does not authorize merge or publication.

## Next actions

1. Obtain a green final PR ratchet for the repaired seven-package promotion slice.
2. Merge/publish only after the maintainer explicitly accepts that smaller boundary; then observe its public
   consumer and R12-07 recovery evidence.
3. Open the first high-value provider-family slice, starting with Entity data providers rather than
   the former owner-number sequence.

## Repository boundaries

- Preserve unrelated worktree changes and untracked `tmp/`; never stage `tmp/`.
- Do not inspect private dogfood applications.
- Do not publish from a workstation, tag, create a GitHub Release, or mutate unrelated remote
  configuration.
- Full release certification belongs to an explicitly requested milestone, not normal development or
  a release plumbing correction.
