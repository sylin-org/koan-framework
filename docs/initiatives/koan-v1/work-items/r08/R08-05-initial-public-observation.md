---
type: SPEC
domain: framework
title: "R08-05 - Observe the Initial Coherent Public Wave"
audience: [architects, maintainers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-19
  status: stopped
  scope: exact 93-package local candidate and API-key promotion contract retained; public observation superseded by R12
---

# R08-05 — Observe the initial coherent public wave

- Tranche: `T7B — V1 release readiness / public observation`
- Status: `stopped — local evidence retained; public observation superseded by R12-06`
- Depends on: passed R08-01 through R08-04
- Unlocks: a later real public-to-candidate upgrade and rollback proof
- Owner: the `release-on-dev.yml` coordinator owns selection, proof, custody, promotion, and recovery;
  the maintainer owns only one-time trust setup and authorization to advance `dev`

R12 preserves this card's exact local release evidence and API-key boundary. Its selective 0.20
guarantee set invalidates using the historical 93-package all-owner bootstrap as the next public
candidate, so R12-06 owns a fresh exact public observation after the maturity and narrative work.

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
tag, and recovery action. The operator observes one wave; they do not shepherd a package catalog.

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

- creating, replacing, or deleting the `NUGET_API_KEY` repository Actions secret;
- changing GitHub repository/branch/environment settings;
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

### 2026-07-19 API-key promotion checkpoint

**Task:** Retain the established NuGet API-key process while connecting it correctly to the current
independent per-project versioning, prepared-escrow, and promotion workflow.

**Application intent:** A maintainer advances `dev`; the release compiler derives every selected
project and version, while the existing repository secret authorizes only promotion of the exact
prepared artifacts.

**Public expression:** The operator surface is one reviewed `dev` advancement plus the pre-existing
repository Actions secret `NUGET_API_KEY`. The operator supplies no package list, version, ordering, or
recovery input.

**Guarantee/correction:** Missing credentials fail the promotion step before `wave-promote` and before
any package push. The prepared-escrow inspection remains an earlier step. Source selection, per-owner
lineage, manifest identity, hashes, publication order, retry, and recovery remain unchanged.

**Complete intent surface:** Maintainers provision and rotate the existing publish credential through
the standard GitHub Actions secret surface. `NUGET_USER`, a trusted-publishing policy, and an OIDC token
exchange are not part of this process.

**Public concepts:** No application concept is added. The only operator-facing credential concept is
the conventional repository Actions secret `NUGET_API_KEY`.

**Docs read:** `docs/engineering/index.md` keeps controlled release proof local until explicit remote
authorization; `docs/architecture/principles.md` favors standard platform concepts and one current
path; `docs/engineering/packaging.md`, `docs/engineering/nuget-publishing.md`, ARCH-0110, and this card
own the release and trust contract.

**Code read:** `.github/workflows/release-on-dev.yml` currently exchanges OIDC only in the two
promotion jobs; `Program.cs` already defaults `wave-promote` to `NUGET_API_KEY`;
`NuGetPackagePromotionTarget` already performs exact hash verification, ordered NuGet push, retry,
visibility checks, and credential redaction; workflow and promotion-target tests own the focused
contract.

**Reusing:** The six existing workflow boundaries, prepared-escrow gates, `wave-promote`,
`NuGetPackagePromotionTarget`, `NUGET_API_KEY`, and the existing release workflow contract tests.

**Creating new:** None. The workflow, focused contract tests, and current release documentation are
edited in place; no service, option, DTO, command, publishing mechanism, or secret name is introduced.

**Coalescence:** `NuGetPackagePromotionTarget` remains the sole publication mechanism and the two
promotion steps remain the sole credential boundary. Remove the parallel OIDC login ceremony and pass
the established secret directly to those steps only.

**Ergonomics:** Maintainers keep the credential process they already operate. Package selection and
per-project versions stay automatic, proof/staging jobs never receive the key, and a missing secret
produces one direct correction without exposing its value.

**Constraints satisfied:** The change uses standard GitHub Actions secrets and the existing .NET/NuGet
publisher. It neither changes package identity nor reruns the release ratchet, and it performs no remote
configuration, push, tag, Release, or publication.

**Risks:** An API key is long-lived, so maintainers must scope it to publishing, protect and rotate it,
and never print or copy it. Local proof can verify the credential boundary and redaction but cannot
verify that the remote secret exists. Immutable GitHub Releases remain a separate prerequisite.

| Gate | Required evidence | Stop condition |
|---|---|---|
| source boundary | intended changes reviewed; privacy gate and changed-doc lint green; no scratch/evaluator material | source contains private downstream identity, unintended files, or unexplained generated drift |
| release mechanics | focused workflow-contract, lineage, planner, package, template, escrow, and recovery tests green | any test requires weakening fail-closed behavior or choosing packages manually |
| exact candidate | canonical clean-room pack proves the selected closure, both templates, FirstUse, and GoldenJourney | source/version commit mismatch, package incoherence, audit failure, or application proof failure |
| startup truth | package-only probes reject the PMC-029 Communication-composition and health-registry collection failures while retaining truthful elections/guarantees | successful business work reports either false collection failure |
| trust settings plan | exact repository, secret name, lineage branch, and protection changes are written down before mutation | setup is ambiguous, exposes the key, or grants broader than package-publish scope |
| recovery posture | maintainer has the failure map in `nuget-publishing.md`; no per-package recovery procedure is introduced | proposed recovery rebuilds published identity, replaces prepared evidence, or moves a tag |

The complete public-release ratchet runs once at this boundary. It is not repeated after every focused repair.

## One-time publishing prerequisites — authorized remote setup

After explicit authorization and before advancing `dev`:

1. Verify that the existing repository Actions secret `NUGET_API_KEY` holds a publish-scoped nuget.org
   credential without exposing its value. Create or rotate it only under separate explicit authorization.
2. Enable immutable GitHub Releases.
3. Protect `dev`, `automation/package-lineage-dev`, and the release workflow according to the recorded
   release trust policy; proof and staging jobs must not receive the NuGet key.
4. Re-read the effective remote settings. If any prerequisite cannot be positively observed, stop before
   advancing `dev`; do not test trust by publishing a sacrificial package.

The API key is stored only as the repository Actions secret and is supplied only to a promotion step
after prepared escrow has been rechecked.

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
5. `promote_current` receives `NUGET_API_KEY` only after prepared escrow, converges packages/symbols,
   creates the full-commit tag, publishes the same draft, and requires immutable terminal state.

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
| NuGet API key is missing or rejected | verify or rotate the repository secret without printing it; preserve the prepared escrow and do not broaden credential access |
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
- The established API-key process is now wired directly into the current release compiler: only the
  two exact promotion steps receive `secrets.NUGET_API_KEY`, after the prepared-escrow gate. OIDC login,
  `NUGET_USER`, and `id-token:write` are absent. Focused workflow/promotion-target evidence passes 14/14,
  including exact hash guards, retry, symbol replay, and credential redaction.
- R08-04's historical 108-package candidate proved the package-first gate before R11 graduation. The
  current graph is now 93 active packages; the historical count is not current release evidence.
- ARCH-0119 repairs the console lifecycle root behind PMC-029; focused source-equivalent evidence is green.
- The first current rehearsal exposed two fail-fast release-boundary defects. Commit `5aeabb2a6`
  deletes Git file-similarity guessing in favor of the existing evaluated package ID/path continuity
  owner; commit `844449dd8` makes a fresh protected workflow bootstrap from the coherent current source
  rather than reinterpret a legacy noncanonical predecessor. Focused lineage/workflow evidence passes
  43/43, and focused docs lint passes.
- From exact source `844449dd8c4927881853d315cba5a569cdb817c9`, the disposable canonical
  bootstrap compiled version commit `378f43beb54f0e8ee8ca0876013526dc97597b4f`, selected all 93 active
  owners once for `lineage-bootstrap`, and generated 93 owner markers. `pack --clean-room` passed in
  723.2 seconds with 93 nupkgs and the expected 90 snupkgs; the two dependency-only bundles and template
  package intentionally carry no symbols.
- The exact `Sylin.Koan.Templates` identity was `0.17.613`; both public template shapes created, restored,
  built, and reached their business result. Package-only FirstUse passed in 3.546 seconds with SQLite,
  lockfile, REST, facts, MCP, dry-run, mutation, and deletion truth. GoldenJourney passed in 6.863
  seconds with persistence, business rule, reactive work, Jobs, facts, truthful agent schema/boundary,
  configured-adapter rejection/readiness/calm logs, and recovery.
- Local escrow preparation produced
  `release-wave-378f43beb54f0e8ee8ca0876013526dc97597b4f.zip` (6,898,090 bytes), SHA-256
  `79a9305f77d63c520fcf4a41b7daf0a27a98568ca9479fad1a3ba0ea4a2999dc`; lineage SHA-256 is
  `977cc4bc1abda811b19b49372ef25844a334742b002b74c37aef17f5f2079147`, and manifest SHA-256 is
  `3894bcb91f74a62a6fa1f7b2e1a3b107a88033eb79531285e87837aac89b25ae`. These are local
  preflight evidence, not uploaded authority.
- No real NuGet publication or immutable GitHub Release has been observed. Remote prerequisites remain
  deliberately unmodified pending explicit authorization. Refresh the exact-source cell immediately
  before that authorization because this evidence-recording commit follows the proved source.

## References

- [NuGet publishing](../../../../engineering/nuget-publishing.md)
- [Packaging policy](../../../../engineering/packaging.md)
- [ARCH-0110 — dev release compiler](../../../../decisions/ARCH-0110-dev-release-compiler.md)
- [ARCH-0119 — one console host lifecycle](../../../../decisions/ARCH-0119-one-console-host-lifecycle.md)
- [R08-01 — durable release wave](R08-01-durable-release-wave.md)
- [R08-04 — package-first templates](R08-04-package-first-templates.md)
