---
type: SPEC
domain: framework
title: "R08-01 - Make One Release Wave the Complete Durable Truth"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: passed
  scope: cross-event recovery, historical package inputs, and least-privilege promotion boundary
---

# R08-01 — Make one release wave the complete durable truth

- Tranche: `T7B — V1 release readiness`
- Status: `passed`
- Depends on: R08 entry assessment, R07-03, and R07-04
- Unlocks: connector security closure and a trustworthy first public package wave
- Owner: `Koan.Packaging` and the serialized `dev` release workflow

## Meaningful outcome

Any `dev` event can finish automatically even when an earlier package push succeeded, its symbol or
state step failed, and a later source event has already arrived. The same owner also remembers every
evaluated package input, so adding, changing, deleting, or renaming an external packed file selects
exactly its package owner. A maintainer never reconstructs an old release or decides what to repack.

## Why now

The current compiler proves same-event replay but loses two kinds of history at event boundaries:

- exact prior-wave artifact/symbol completion after a later lineage advancement (PMC-016); and
- arbitrary evaluated external pack inputs after deletion or rename (PMC-017).

Both are missing durable facts about the same release wave. Solving them separately would create two
historical-state mechanisms. The entry audit also found an invalid three-part version intent and a
privileged single workflow job; both must be corrected at this owner before its first real use.

## Evidence to read first

- Code: `ReleaseLineageCompiler`, `ReleasePlanner`, `RepositoryInspector`, `PackagePipeline`, release
  manifest/state models, `release-on-dev.yml`, and package/version MSBuild policy.
- Tests: `ReleaseLineageGitTests`, `ReleaseLineageCompilerTests`, `ReleasePlannerTests`, and
  `ReleaseWorkflowContractTests`.
- Documentation / decisions: ARCH-0085, ARCH-0110, R07-03, packaging/versioning/publishing guides,
  and PMC-016/017.
- Relevant external primary sources: GitHub Actions artifact/permission guarantees and NuGet package
  plus symbol publication APIs, selected only after the local state model is mapped.

## Decisions

### DECIDED

- Extend one release-wave model; do not introduce a parallel recovery database, package checklist,
  or operator-managed artifact archive.
- Persist normalized per-package evaluated input maps and compare `previous union current`. The
  owning package is selected for add, change, delete, and rename; unrelated owners remain unchanged.
- Bind exact nupkg/snupkg hashes and completion stages to `VersionCommit` and package identity.
- Before publishing a later wave, automatically reconcile every incomplete earlier identity from its
  exact verified artifacts. Never mint replacement source or bits under that identity.
- Symbol completion is explicit state. Public nupkg visibility alone cannot advance it.
- Source validation rejects noncanonical `version.json` intent before lineage mutation or expensive
  certification and names the owner/path/correction.
- Build/test/proof executes without `contents:write` or `id-token:write`. Only the narrow step that
  consumes verified artifacts receives the permissions needed for publication and final evidence.
- No real publication or remote mutation is part of this card.

### DEFAULT

- Evolve current schemas with deterministic migration/replay validation rather than accepting
  ambiguous older state.
- Keep version truth in Git-owned lineage. Stage the complete verified manifest, nupkg, and snupkg
  set on the eventual draft GitHub Release before requesting a NuGet credential; publishing that
  same release is the single durable completion transition.

### PROVED

- Two direct packs of the same project at the same HEAD produced different SHA-256 hashes for both
  nupkg and snupkg. Exact rebuild is not an artifact-recovery mechanism; the original verified bytes
  must survive until package, symbol, and final release completion converge.
- Six jobs are the minimum honest authority split for prior/current recovery: read-only proof,
  contents-write staging, and contents-write-plus-OIDC promotion for each wave. Promotion consumes the
  prepared coordinator/handoff and never restores, compiles, tests, packs, or rebuilds source.

## Scope

### In

- Correct the current `Koan.AI` version intent and add a whole-inventory regression.
- Persist and validate per-package evaluated input ownership in lineage state/artifacts.
- Select owners from the union of prior/current input maps.
- Persist exact artifact hashes and package/symbol completion for incomplete waves.
- Reconcile incomplete earlier waves before later-wave promotion.
- Split unprivileged verification from narrowly privileged publication/evidence steps.
- Update workflow contracts and truthful recovery documentation.

### Out

- Real NuGet publication, remote branch/tag/release mutation, or trusted-publishing setup.
- Connector log redaction (PMC-019), aggregate certification reports (PMC-020), template dependency
  generation, public support matrices, or the compatibility-window decision.
- Retry/dead-letter semantics for application Communication.

## Business/operator proof

The only happy-path instruction remains:

```text
push or merge the package-affecting change into dev
```

The adversarial proof is also operator-free:

```text
wave A: nupkg visible, symbol/state interrupted
wave B: later source event arrives
automation: recover exact A artifacts -> confirm A package+symbol -> publish eligible B identities
```

No step asks a maintainer for a package ID, version, old commit, artifact path, or recovery choice.

## Execution plan

1. Map the current lineage, manifest, artifact, state, and workflow authority boundaries; choose one
   schema evolution that owns both missing histories.
2. Add red tests for noncanonical inventory intent and per-package external input add/change/delete/
   rename behavior across real Git events.
3. Add a red publication simulation for nupkg success, symbol/state failure, later lineage advance,
   and automatic exact-wave recovery.
4. Persist the minimum additional wave state and implement prior-wave reconciliation before later
   publication.
5. Separate verification and promotion permissions while preserving one compiled manifest and exact
   artifacts; pin the workflow contract structurally.
6. Run focused packaging/Git/workflow tests, one disposable multi-wave rehearsal, strict docs/diff/
   privacy checks, and update support claims. Do not run release certification unless risk expands.

## Verification

- Focused tests: packaging compiler/planner/pipeline and workflow contract.
- Real Git proof: input add, change, delete, and rename select only the owner unless the same event
  also creates a breaking closure.
- Publication simulation: exact A nupkg/snupkg hashes survive an interrupted A and later B; A
  reconciles first; different bits under A are rejected.
- Failure proof: missing/tampered artifacts, ambiguous schema, false symbol completion, and
  noncanonical version intent fail before publication with a corrective message.
- Documentation / sample checks: packaging, versioning, publishing, initiative ledgers, docs lint,
  and changed marked examples where applicable.
- Privacy check: no private downstream identity, path, persona, or workflow enters artifacts.

## Acceptance additions

- PMC-017 acceptance passes without operator input: add, change, rename, and deletion of an evaluated
  external packed file select only its owner from the prior/current input-map union. PMC-016 also
  passes locally through exact-escrow and cross-event failure-injection evidence.
- Every privileged workflow permission is absent from build/test/proof jobs and present only where
  consumed.
- Replaying the same source or advancing a later source cannot lose an earlier package/symbol stage.
- Verification leaves no unexpected/generated tracked drift. Intentional closure/governance documents
  are enumerated in the handoff; ignored/untracked evidence under `tmp/` is never staged.

## Stop conditions

- Stop if the design relies only on time-limited artifacts without a durable failure posture.
- Stop if exactness depends on mutable registry metadata, latest-toolchain reinterpretation, or
  rebuilding unchecked bits.
- Stop if job splitting requires repacking after verification or produces two competing manifests.
- Stop before any remote mutation or real credential exchange.

## Implementation record

### 2026-07-16 — canonical intent and input-history foundation

- One `VersionIntent` policy now validates inventory and historical Git reads. It accepts only the
  canonical unsigned `major.minor` form; diagnostics name the owner, project/path, correction, and
  NBGV patch ownership. `Koan.AI` is corrected from `0.18.1` to `0.18`.
- Lineage schema 3 records a sorted normalized evaluated input map for every active and retired
  package. Compiler impact and independent planner validation use prior plus current ownership;
  `git diff --no-renames` preserves both sides of a rename.
- Focused canonical/compiler tests pass 39/39. Real Git lineage tests pass 7/7, including external
  input add/change/rename/delete and retirement behavior. The packaging tool builds with zero
  warnings/errors, and no release certification or remote mutation ran.
- Schema 2 fails closed because its missing historical arbitrary-input ownership cannot be inferred.
  The T7 audit found no promoted automatic lineage, so the first trusted run can bootstrap directly
  into schema 3; any subsequently discovered remote v2 tip requires an explicit migration decision.

### 2026-07-16 — exact escrow and resumable promotion

- Commit `f450a7efe` adds one deterministic release-wave bundle and marker that bind exact lineage,
  manifest, nupkg/snupkg, application evidence, source/version commits, and full-commit tag.
- Commit `d3f43923b` adds GitHub Release escrow and one resumable coordinator. It derives state from the
  exact Release, reconciles a prior wave before a later wave, validates every byte, replays symbols,
  waits for package visibility, emits one completion receipt, and fails closed on conflicting custody.
- Commit `546817ee0` separates prior/current proof, staging, and promotion into six workflow jobs.
  Build/test/pack has no publish authority; staging has no OIDC credential; promotion does not rebuild.
- Failure-injection, escrow, bundle, NuGet target, coordinator, and workflow-structure tests were
  recorded green when the commits landed. The companion ADR and operator/tooling documentation now
  describe the same authority and recovery model.
- No release certification rerun, NuGet publication, tag, GitHub Release, or remote configuration
  mutation was part of closure. Real immutable-Release observation remains an R08 operator-gated step.

## Acceptance result

- Outcome: PASS
- Date and commit: 2026-07-16; `4acff8b3b` through `546817ee0`
- Evidence: lineage schema 3, exact wave bundle/marker, GitHub Release escrow, resumable coordinator,
  NuGet promotion target, and six-job workflow contract
- Tests / validation: focused packaging, real-Git lineage, failure-injection, and workflow contract
  evidence recorded with the implementation; documentation/diff validation is repeated at R09 kickoff
- Unsupported scenarios: no real public wave or immutable Release has been observed; trusted publishing,
  repository configuration, package-first upgrade, and support policy remain R08 work
- Follow-up work: preserve this baseline through R09, then resume R08 at Safe public surface
- Reviewer: maintainer-authorized autonomous implementation
