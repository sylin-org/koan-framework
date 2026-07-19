---
type: SPEC
domain: framework
title: "R08-05 - Observe the Initial Coherent Public Wave"
audience: [architects, maintainers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: pending
  scope: local readiness contract only; no remote configuration or publication performed
---

# R08-05 — Observe the initial coherent public wave

- Tranche: `T7B — V1 release readiness / public observation`
- Status: `pending explicit remote-operation authorization`
- Depends on: passed R08-01 through R08-04
- Unlocks: a later real public-to-candidate upgrade and rollback proof
- Owner: the `release-on-dev.yml` coordinator owns selection, proof, custody, promotion, and recovery;
  the maintainer owns only one-time trust setup and authorization to advance `dev`

## Meaningful outcome

A normal push or merge that advances `dev` automatically produces the first coherent public Koan wave.
The exact tested artifacts become both immutable GitHub Release evidence and visible NuGet packages. A
new machine can then install the public template, run meaningful Entity work, and inspect truthful startup
facts without repository access, local feeds, version choices, or operator-selected recovery steps.

This card prepares and records the observation. It does not authorize remote configuration, push, tag,
GitHub Release creation, or NuGet publication.

## Delight contract

The operator makes one business decision: **advance `dev` with these source changes**. Git owns package
intent. Automation derives every touched owner, version, dependency closure, template band, package order,
tag, and recovery action. The operator observes one wave; they do not shepherd 108 packages.

The developer's first public experience remains:

```bash
dotnet new install Sylin.Koan.Templates
dotnet new koan-console -o MyApp
dotnet run --project MyApp
```

The agent/reviewer can trace the resulting source commit, version commit, package manifest, application
proof, package hashes, completion receipt, public identities, and immutable Release without reconstructing
intent from logs.

## Authorization boundary

Before explicit authorization, all work is read-only or local. Stop before any of the following:

- creating or changing the nuget.org trusted-publishing policy;
- setting `NUGET_USER` or changing GitHub repository/branch/environment settings;
- enabling immutable Releases;
- pushing or merging to `dev` or `automation/package-lineage-dev`;
- creating, editing, publishing, or deleting a GitHub Release or tag;
- publishing, relisting, unlisting, or deprecating a NuGet package.

Authorization should name this operation: **configure the recorded one-time trust prerequisites if needed,
then advance `dev` once and observe R08-05 to a terminal state**. It must not be inferred from approval of
local implementation work.

## Preflight — no remote mutation

All cells must be checked against the exact intended source commit immediately before authorization.

### 2026-07-19 exact-candidate exploration checkpoint

**Task:** Repair the local lineage compiler's false package-rename rejection, then prove the exact
current candidate without remote mutation.

**Application intent:** A maintainer advances `dev` once; Git and evaluated package identity determine
which owners continue, retire, or begin, without package-by-package interpretation.

**Public expression:** The operator surface remains the normal reviewed `dev` advancement. There is no
new application API, reference, decoration, configuration, context, or runtime prerequisite.

**Guarantee/correction:** An unchanged package ID must remain at its recorded owner path; an unchanged
owner path must retain its package ID; retired IDs and paths remain permanently unavailable. A real
move or identity swap fails with the existing continuity correction. Deleting an old owner and adding
a genuinely new ID at a genuinely new path is accepted even when their `version.json` bytes happen to
match.

**Complete intent surface:** No additional user action exists beyond advancing `dev`; the release
compiler evaluates ordinary MSBuild/NuGet project identity and Git history.

**Public concepts:** None. This removes a conflicting internal heuristic and preserves the existing
Git, MSBuild project, NuGet package ID, and owner-path vocabulary.

**Docs read:** `docs/engineering/index.md` requires a disposable clone for controlled release
rehearsals; `docs/architecture/principles.md` requires standard .NET identity and one decision owner;
`docs/engineering/packaging.md` defines deletion plus a new identity/path as retirement and creation;
ARCH-0110 makes evaluated package lineage authoritative; this card owns the exact local proof and
remote stop boundary.

**Code read:** `ReleaseLineageCompiler` owns both evaluated continuity and the conflicting Git rename
heuristic; `RepositoryInspector` already returns changed paths with `--no-renames` and evaluates the
canonical package graph; `ProcessRunner` supplies the one process boundary; `PackagingConstants`
contains existing lineage identity and requires no new literal or option; lineage compiler/Git tests
already prove real move rejection, retirement, and new-package creation but not same-byte replacement.

**Reusing:** `ReconcilePackageContinuity`, `PackageGraph`, evaluated `PackageProject` identity,
`LineageRepository`, and the existing focused Packaging test project.

**Creating new:**

| New code | Location | Justification |
| --- | --- | --- |
| same-byte retirement/new-owner regression cell | `tests/Koan.Packaging.Tests/ReleaseLineageGitTests.cs` | proves the real repository failure through the existing Git-backed lineage boundary |

**Coalescence:** The closest pattern is `ReconcilePackageContinuity`, whose decision owner is the
lineage compiler and whose consumers are bootstrap and later waves. Keep that evaluated, identity-aware
owner; delete `RejectPackageRenamesAsync` and both calls. A Git similarity heuristic is too narrow to
know package identity and duplicates the semantic owner. No replacement service, option, threshold, or
exception list is introduced.

**Ergonomics:** The operator keeps one decision and receives one identity/path-specific correction.
Maintainers and agents can read the rule directly from evaluated package continuity instead of
reconciling it with an undocumented similarity score. IntelliSense and the application coding model do
not change.

**Constraints satisfied:** no HTTP or data-access surface is involved; no magic literal, option, DTO,
contract, module, or public concept is added; existing companion docs remain authoritative; the focused
test and disposable exact candidate are the bounded verification. The accepted packaging policy and
ADR do not change, so no new ADR or TOC entry is required.

**Risks:** The regression must continue rejecting a true same-ID path move and a same-path ID change;
the existing focused continuity cells cover both. The full candidate may expose later independent
preflight defects after this fail-fast repair.

### 2026-07-19 initial-lineage migration checkpoint

The repaired rehearsal reached the next fail-fast boundary: `origin/dev` predates the project-local
version-intent contract, so using that legacy event predecessor as the first lineage base fails before
projection (`Sylin.Koan.AI.Connector.Onnx` is the first noncanonical owner). This would affect the real
161-commit advancement exactly as it affects the disposable rehearsal.

**Application intent:** The first normal `dev` advancement establishes release automation from the
coherent current package graph; the maintainer does not manufacture a historical graph, seed a branch,
or choose package identities.

**Public expression and complete surface:** The expression remains one normal `dev` advancement with
no additional input. When no durable lineage exists, `prove_current` supplies the current source event
as its own bootstrap base and mints every current owner once. When durable lineage exists, the existing
previous-source/previous-lineage path remains unchanged.

**Guarantee/correction:** Initial authority covers every package active at the coherent bootstrap and
all continuity/retirement decisions afterward. The workflow never interprets a legacy tree with newer
ownership rules. A non-bootstrap wave still fails closed on missing/tampered lineage, a true owner move,
or a non-forward source event.

**Coalescence and ergonomics:** Keep bootstrap selection in the existing `prove_current` coordinator,
which alone knows whether durable lineage exists. Reuse `lineage` unchanged; do not add a legacy
evaluator, migration manifest, manual seed command, or second workflow. The operator retains one
decision and the compiler retains one evaluated package grammar.

**Placement and proof:** The conditional base belongs in
`.github/workflows/release-on-dev.yml`; one structural cell in `ReleaseWorkflowContractTests` pins it.
ARCH-0110, the packaging policy, and the tool README must describe the current-owner bootstrap. No
runtime API, constant, option, DTO, module, HTTP route, data access, or application concept is added.

| Gate | Required evidence | Stop condition |
|---|---|---|
| source boundary | intended changes reviewed; privacy gate and changed-doc lint green; no scratch/evaluator material | source contains private downstream identity, unintended files, or unexplained generated drift |
| release mechanics | focused workflow-contract, lineage, planner, package, template, escrow, and recovery tests green | any test requires weakening fail-closed behavior or choosing packages manually |
| exact candidate | canonical clean-room pack proves the selected closure, both templates, FirstUse, and GoldenJourney | source/version commit mismatch, package incoherence, audit failure, or application proof failure |
| startup truth | package-only probes reject the PMC-029 Communication-composition and health-registry collection failures while retaining truthful elections/guarantees | successful business work reports either false collection failure |
| trust settings plan | exact repository, owner, workflow filename, lineage branch, and protection changes are written down before mutation | setup identity is ambiguous or asks for a long-lived API key |
| recovery posture | maintainer has the failure map in `nuget-publishing.md`; no per-package recovery procedure is introduced | proposed recovery rebuilds published identity, replaces prepared evidence, or moves a tag |

The complete public-release ratchet runs once at this boundary. It is not repeated after every focused repair.

## One-time trust prerequisites — authorized remote setup

After explicit authorization and before advancing `dev`:

1. Configure nuget.org trusted publishing for the exact GitHub repository and
   `.github/workflows/release-on-dev.yml`.
2. Set the repository Actions variable `NUGET_USER` to the matching nuget.org owner.
3. Enable immutable GitHub Releases.
4. Protect `dev`, `automation/package-lineage-dev`, and the release workflow according to the recorded
   release trust policy; ordinary proof jobs must retain no write or OIDC permission.
5. Re-read the effective remote settings. If any prerequisite cannot be positively observed, stop before
   advancing `dev`; do not test trust by publishing a sacrificial package.

No long-lived NuGet key is created or stored.

## One trigger

Advance `dev` exactly once through the normal reviewed path. Do not manually invoke packaging commands,
edit version files, pre-create tags/Releases, or publish individual nupkgs. The push-triggered
`Release packages from dev` workflow is the product path being observed.

Record:

- source commit and predecessor;
- workflow run ID/attempt;
- prior durable lineage head, compiled version commit, and selected package count;
- whether prior-wave reconciliation was a no-op or performed work.

## Observe the automated wave

Observe the six permission boundaries as one state machine:

1. `prepare_prior` serializes earlier events and resolves any prior wave.
2. `stage_prior` and `promote_prior` run only if prior reconciliation requires them.
3. `prove_current` compiles exact lineage, runs the release ratchet once, packs the selected closure, and
   produces package-only template/FirstUse/GoldenJourney evidence.
4. `stage_current` persists the exact lineage candidate and uploads bundle before marker.
5. `promote_current` exchanges OIDC only after prepared escrow, converges packages/symbols, creates the
   full-commit tag, publishes the same draft, and requires immutable terminal state.

Do not cancel a slow valid run merely because long package output is buffered. Investigate only after the
bounded job timeout, an explicit failure, or contradictory external state.

## Terminal success evidence

The initial public wave passes only when all of the following agree:

- the workflow is green and every executed job used its documented least-privilege boundary;
- `automation/package-lineage-dev` targets the recorded full version commit;
- the exact `release/dev/<full-VersionCommit>` tag targets that same commit and was not forced;
- one published immutable GitHub Release contains the exact prepared marker, bundle, completion receipt,
  lineage, manifest, package/symbol hashes, and application evidence;
- every selected nupkg is visible on nuget.org at the manifest identity; symbol replay is recorded;
- a clean environment using public NuGet sources only installs the template package, creates both public
  template shapes, and reaches their meaningful business result;
- public package-only console, FirstUse, and GoldenJourney facts contain no PMC-029 false collection failure;
- no local feed, repository ProjectReference, package cache accident, or hand-edited version participates.

Store links and immutable identities in this card and `PROGRESS.md`; do not copy package state into a second
maintained ledger.

## Failure and stop map

| Observed state | Required response |
|---|---|
| proof fails before staging | fix source and advance/re-run normally; no current public identity exists |
| draft has no authoritative marker and no selected package is public | allow the coordinator's reset/rebuild path |
| prepared marker exists | never replace marker or bundle; resume from exact escrow |
| package push/visibility/response is uncertain | rerun or let the next event reconcile; skip visible nupkgs and replay exact symbols |
| public package exists without prepared escrow | stop all later waves and review; never rebuild beneath that identity |
| trusted-publishing exchange fails | correct the exact policy/owner mapping; never add a long-lived key |
| published Release is mutable | treat the wave as failed, stop later publication, and review repository settings; do not move tag or create a substitute Release |
| package-only startup repeats PMC-029 | do not call the public baseline coherent; preserve evidence and repair through ARCH-0119's owner |

## Acceptance

1. Preflight and one-time trust prerequisites are explicitly evidenced.
2. A single normal `dev` advancement reaches one terminal automated wave without package/version/recovery input.
3. Exact Git, workflow, immutable Release, NuGet, template, FirstUse, GoldenJourney, and startup-fact evidence agree.
4. Recovery behavior is observed if encountered; no manual package handling or evidence replacement occurs.
5. This card and `PROGRESS.md` record immutable identities and links, not mutable copied package state.
6. Only after these pass may R08 open the later public-to-candidate upgrade/rollback child.

## Current evidence

- R08-01 proves release-wave failure/recovery mechanics locally, including exact binary custody and six
  least-privilege workflow boundaries.
- R08-04 proves one exact 108-package candidate, both generated templates, and package-only
  FirstUse/GoldenJourney locally.
- ARCH-0119 repairs the console lifecycle root behind PMC-029; focused source-equivalent evidence is green.
- No real NuGet publication or immutable GitHub Release has been observed. Remote prerequisites remain
  deliberately unmodified pending explicit authorization.

## References

- [NuGet publishing](../../../../engineering/nuget-publishing.md)
- [Packaging policy](../../../../engineering/packaging.md)
- [ARCH-0110 — dev release compiler](../../../../decisions/ARCH-0110-dev-release-compiler.md)
- [ARCH-0119 — one console host lifecycle](../../../../decisions/ARCH-0119-one-console-host-lifecycle.md)
- [R08-01 — durable release wave](R08-01-durable-release-wave.md)
- [R08-04 — package-first templates](R08-04-package-first-templates.md)
