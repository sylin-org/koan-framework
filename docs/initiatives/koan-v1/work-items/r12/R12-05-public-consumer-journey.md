---
type: SPEC
domain: framework
title: "R12-05 - Freeze and Certify the First 0.20 Candidate"
audience: [architects, maintainers, release-engineers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-19
  status: in-progress
  scope: corrected local-candidate architecture checkpoint; execution waits on R12-04
---

# R12-05 — Freeze and certify the first 0.20 candidate

- Tranche: `T7B — public product maturity`
- Status: `pending — checkpoint accepted; execution waits on R12-04`
- Depends on: passed R12-01 through R12-04
- Unlocks: R12-06 publication and genuine public-feed consumer observation
- Owner: the existing release compiler owns exact selection, versions, proof, artifacts, and local escrow

## Meaningful outcome

Koan has one frozen source commit that is ready to become the first public 0.20 preview wave. Its exact
selective guarantee boundary, independently versioned package graph, release-ready public narrative,
templates, FirstUse, GoldenJourney, and recovery evidence pass locally before any remote mutation.

This is release-candidate engineering evidence, not public-consumer evidence. An isolated local feed can
prove exact package behavior; only packages visible on NuGet can prove the public experience.

## Architecture checkpoint — one frozen local candidate

**Task:** Converge all accepted 0.20 source, package, and narrative changes, freeze one source commit, and
run the repository's existing complete local candidate boundary without publishing it.

**Application intent:** “Before Koan asks anyone else to try 0.20, prove that the exact package family can
create and run the documented applications without repository help.”

**Public expression:** No new application API exists. The candidate must preserve ordinary
`dotnet new install Sylin.Koan.Templates`, the four-line `AddKoan()` host, `Entity<T>`, and
`EntityController<T>` path. During local proof the exact nupkg replaces NuGet as the package source;
that substitution is maintainer evidence and is never taught as the public install path.

**Guarantee/correction:** The frozen commit deterministically compiles a coherent manifest, exact package
identities, bounded internal dependencies, clean artifacts, both templates, FirstUse, GoldenJourney, and
one escrow marker/bundle. Any drift, audit failure, hidden source edge, package mismatch, stale template
range, runtime contradiction, or false success stops before staging or publication and names the existing owner.

**Complete intent surface:** Finish R12-04 reader corrections; make public copy release-ready; commit every
tracked change; freeze one source SHA; perform read-only remote-lineage/prerequisite discovery; compile lineage
and manifest in a disposable checkout; run the one complete local public-release ratchet; pack and inspect the
exact selected set with clean-room templates, FirstUse, and GoldenJourney; create deterministic local escrow;
record a go/no-go without making another tracked change. Any correction invalidates the freeze and starts a
new exact candidate.

**Public concepts:** None. Standard Git commit identity, MSBuild/NBGV package identity, NuGet ranges,
`dotnet new`, and the already documented Koan application grammar remain the only concepts.

**Docs read:** the R12 charter and R12-01 through R12-04; R08-04 package-first templates; R08-05 retained
candidate/API-key boundary; `nuget-publishing.md`; `packaging.md`; ARCH-0110; root/template/FirstUse/
GoldenJourney public entry points.

**Code read:** `release-on-dev.yml`; `PackagingProgram`; `ReleaseLineageCompiler`; `ReleasePlanner`;
`PackagePipeline`; `TemplatePackageProbe`; FirstUse/GoldenJourney probes; `ReleaseWaveBundle` and its
coordinator/model/constants; workflow, lineage, planner, package, template, escrow, and recovery tests.

**Reusing:** The release compiler remains the sole selection/version owner. The canonical green ratchet remains
the aggregate source proof. `PackagePipeline --clean-room` remains the package-only execution chokepoint.
`ReleaseWaveBundle` remains the only local escrow compiler. Existing facts, health, and lockfile assertions remain
composition truth.

**Creating new:** No production type, release command, manifest, runner, CLI, sample, or evidence ledger is
planned. Add assertions only inside the existing probe or compiler owner if the exact candidate exposes an
unproved accepted promise.

**Coalescence:** Rebuild the former pre-public “consumer journey” card into this local certification boundary.
Keep R12-04's narrative comprehension review. Transfer all genuine independent installation and provider-swap
evidence to R12-06 after NuGet visibility. Keep the release compiler, API-key workflow, and immutable escrow as
the single release path; delete no recovery guard and add no manual publication lane.

**Ergonomics:** The maintainer makes one decision—this source commit is ready—and supplies no package list,
version, dependency order, or recovery choice. A developer still sees only the normal template/Entity path.
Local-feed mechanics remain invisible outside release evidence.

**Constraints satisfied:** business intent leads; fewer meaningful moving parts win; standard Git/.NET/MSBuild/
NuGet concepts come first; only supported claims carry 0.20; the one complete ratchet runs only at the frozen
candidate boundary; private dogfood, `tmp/`, staging, publication, tags, Releases, secrets, and remote mutation
remain outside scope.

**Risks:** The local branch currently contains many unpublished events and the authoritative remote lineage may
still require an all-owner bootstrap. Therefore neither package count nor exact version commit is assumed before
read-only preflight. A local proof followed by a tracked commit is no longer exact. Remote settings and secret
existence cannot be inferred from local tests.

## Work

1. Close R12-04 with the pre-correction and post-correction comprehension-only coding-agent reads,
   maintainer disposition, and all resulting tracked corrections; no additional human validator is required.
2. Replace temporary publication-pending language with the final public install posture and rerun affected docs,
   examples, skills, generated-truth, sample-lock, and focused package checks.
3. Commit all intended tracked changes, confirm only `tmp/` remains untracked, and record the frozen source SHA.
4. Read-only inspect current remote `dev`, durable lineage, workflow, immutable-Release posture where observable,
   and the named API-key prerequisite without exposing or changing credentials.
5. In a disposable checkout, compile exact lineage and manifest from the authoritative predecessor and frozen source.
6. Run the one complete local public-release ratchet, then exact clean-room pack and local escrow preparation.
7. Verify generated package identities against the 38-owner 0.20 guarantee boundary while allowing lower-line
   available packages selected by bootstrap or real change.
8. Record source/version commits, derived package count, hashes, durations, application evidence, and a concise
   R12-06 go/no-go. Make no tracked change after the freeze; if evidence must enter Git, refreeze and rerun.

## Acceptance

1. R12-04 passes and final public copy contains no contradictory “published”/“pending” states.
2. One frozen source commit contains every intended tracked release change and no private or scratch material.
3. The exact local lineage/manifest is derived without a hand-maintained package list or version input.
4. Exactly the supported-claim owners carry 0.20; other selected packages retain their truthful version/maturity.
5. The complete local ratchet, advisory enforcement, exact pack, artifact inspection, both templates,
   package-only FirstUse, package-only GoldenJourney, and escrow validation pass from that frozen source.
6. No tracked file changes after the proof. A correction creates and fully reproves a new candidate.
7. The R12-06 checkpoint names the exact source, derived version commit, manifest, remote target, credential name,
   immutable custody requirement, stop map, and authorization still required.
8. No push, tag, publication, GitHub Release, deployment, secret/configuration mutation, private dogfood inspection,
   or `tmp/` staging occurs.

## Stop conditions

- Stop until R12-04's coding-agent evidence and maintainer disposition leave no unresolved contradiction.
- Stop on tracked drift after freeze or any source/version/manifest/artifact mismatch.
- Stop if release selection requires an operator package list or if a lower-maturity package is relabeled 0.20.
- Stop if local-feed success is presented as public availability or independent consumer evidence.
- Stop before any remote mutation; R12-06 owns authorization, publication, and public observation.
