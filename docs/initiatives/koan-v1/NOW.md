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

## Accepted next epic: terminal package maturity

[ARCH-0120](../../decisions/ARCH-0120-terminal-package-maturity.md) rejects the proposed common-version
cohort and retains the bidirectional product invariant: supported claim owners are on 0.20, their
public Koan dependency closure is supported, and every 0.20 package owns part of a supported claim.
The current generated surface remains the baseline: 93 packable projects, 38 supported owners at
0.20, and 55 earlier-line owners (19 demonstrated, two verified, two experimental, and 32
unassessed).

R13 now owns a terminal outcome for every one of those 55 owners. Retained Koan packages earn
supported 0.20 admission only after their public guarantee, non-claims, corrective behavior, focused
owner/provider tests, consumer journey, dependency closure, and API baseline agree. Packages whose
accepted behavior belongs elsewhere complete proved absorption, public migration, or retirement and
leave the active package graph instead of receiving a cosmetic version bump.

The accepted dependency order is ten waves: testing/quick wins; inert contracts; Redis and remote
Data; the local Storage floor; Storage/Backup/Media/Zen Garden; local Vector; remote Vector/Search;
mainline AI; external authentication; and the terminal cross-repository AI handoff. The mandatory
pre-Wave-0 bootstrap and Wave 0 implementation now pass. The first dependency-closed publication
will also close
[R12-07](work-items/r12/R12-07-preview-evolution.md)'s upgrade/recovery proof. The bootstrap is split
into five dependency-ordered children, followed by R13-06. All six now pass locally; seven package
owners are admitted at supported 0.20 and public observation remains pending. The mutable epic charter is
[R13](work-items/R13-terminal-package-maturity.md).

## Exact pause point and autonomous continuation

The ARCH-0120/R13 planning change and the completed R13-01 API-baseline slice are intentionally
uncommitted. On `dev`,
`HEAD` is `779548d79dc4ddd22410f80d9c343eb0934dd002`, one commit ahead of `origin/dev` at
`243a346bec9b6f1fb521e35640fe24e7fc3e4205`. R13 is claimed and its five bootstrap children are
recorded. R13-01 adds exact project-local SDK API baselines to the 35 previously supported assembly packages,
keeps the three content-only owners outside API comparison, and adds the fail-closed
`Koan.Packaging api-baselines` guard before the existing main publisher packs. R13-02 adds a real
product compile and byte-exact generated-drift check to the `main` PR boundary. R13-03 adds bounded,
named-result admission to bootstrap and Forge and makes integration-host teardown fail loud. R13-04
adds always-emitted, exact-candidate native applicability/results. R13-05 adds the bounded
fixed-baseline removed-owner certificate and reconciler. R13-06 implements and locally admits the
three testing owners plus SQLite Cache, SoftDelete, Admin, and the Auth Test connector. It adds
fail-closed lifecycle, reusable Cache conformance, shared Web container fixtures, exact deterministic
and native cells, and a seven-package external consumer. The implementation boundary is complete;
publication and public R12-07 observation are not authorized in this session.
Untracked `tmp/` is unrelated user-owned material
and must remain untouched and unstaged.

The maintainer has authorized a fresh-session agent to continue autonomously for several hours. That
authorization covers local R13 exploration, decomposition, implementation, focused validation, and
initiative-ledger updates without waiting for routine confirmation. It does not authorize a push,
merge, tag, GitHub Release, package publication, deliberate remote interruption, `main` mutation,
private-dogfood inspection, or any other externally visible action. Per the initiative charter, do
not commit unless the maintainer explicitly requests a commit; leave coherent, well-validated local
changes and preserve this planning diff.

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
- The planning slice already passed exact 55-owner accounting (`55` rows, `55` unique, `0` missing,
  `0` extra, and `0` topology violations), wave counts `7/2/10/3/6/4/6/7/3/7`, changed-document
  link/trailing-whitespace checks, and `git diff --check`. Broader documentation lint had zero errors
  and eleven non-gating warnings from the established ADR/index/NOW frontmatter schema mismatch.
- R13-01 passes: public read-only verification reports 35/35 exact earliest 0.20 API baselines,
  zero first-publication pending, and three content-only owners; baseline policy passes 7/7, the
  focused packaging slice passes 35/35, the Release tool build has zero warnings/errors, Core's real
  SDK-validation pack and the content-only foundation pack pass, generated product truth is byte-
  identical, and `git diff --check` is clean.
- R13-02 passes: focused product/compiler/baseline/workflow tests pass 22/22, and the real Release
  check compiles 29 claims and 93 packages with both generated projections current.
- R13-03 passes: focused tooling tests pass 34/34, integration-host lifecycle passes 2/2, the Fast
  bootstrap admits 20/20 named results, and record/InMemory Forge is GREEN with exactly 5/5 cells.
- R13-04 passes: focused product/admission/native tests pass 52/52, the Release tool builds warning-
  clean, and generated product truth remains 29 claims/93 packages/current.
- R13-05 passes: fixed-baseline/partial/final/mutation tests pass 13/13; the real partial command
  reports 0/55 resolved and strict final correctly rejects all 55 currently unresolved owners.
- R13-06 passes eleven exact cells with 53/53 named results and zero skips/failures; product truth is
  33 claims / 93 packages, API posture is 35/42 configured plus seven first-publication pending, and
  terminal reconciliation is 7/55 resolved.
- During implementation, run the affected owner, shared family conformance, provider-specific, and
  package-consumer evidence defined by ARCH-0120.
- Do not run the full solution, all test projects, the green ratchet, completed
  Tenancy/Classification suites, or package clean-room applications merely to reopen this completed
  documentation slice.
- Run the complete release ratchet once at explicit epic certification, not after every wave, and do
  not repeat a passing check unless affected code changes.

## Next actions

1. Obtain explicit authorization for the Wave 0 publication boundary; do not infer it from local
   implementation approval. Observe the dependency-closed public artifacts and consumer upgrade for
   [R12-07](work-items/r12/R12-07-preview-evolution.md), including its bounded recovery proof.
2. After successful public observation, open Wave 1 on the two inert contract owners. Preserve the
   same atomic claim/version/admission rule and do not advance around an unresolved Wave 0 artifact.

## Repository boundaries

- Preserve unrelated worktree changes and untracked `tmp/`; never stage `tmp/`.
- Do not inspect private dogfood applications.
- Do not publish from a workstation, tag, create a GitHub Release, or mutate unrelated remote
  configuration.
- Full release certification belongs to an explicitly requested milestone, not normal development or
  a release plumbing correction.
