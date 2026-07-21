---
type: SPEC
domain: framework
title: "R12-05 - Freeze and Certify the First 0.20 Candidate"
audience: [architects, maintainers, release-engineers, ai-agents]
status: current
last_updated: 2026-07-20
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-20
  status: in-progress
  scope: public-feed template journey works; clean-restore correction pending
---

# R12-05 — Freeze and certify the first 0.20 candidate

- Tranche: `T7B — public product maturity`
- Status: `in-progress — public-feed journey works; clean-restore correction pending`
- Depends on: passed R12-01 through R12-04
- Unlocks: R12-06 publication and genuine public-feed consumer observation
- Owner: the shipped template source owns the package-first consumer expression; standard NuGet owns resolution

## Meaningful outcome

Koan has one frozen source commit that is ready to become the first public 0.20 preview wave. Its exact
selective guarantee boundary, independently versioned package graph, release-ready public narrative,
templates, FirstUse, GoldenJourney, and recovery evidence pass locally before any remote mutation.

This is release-candidate engineering evidence, not public-consumer evidence. An isolated local feed can
prove exact package behavior; only packages visible on NuGet can prove the public experience.

## Public-feed observation checkpoint — 2026-07-21

**Task:** Remove the warning from the live template journey and replace pre-publication copy with the
current NuGet path.

**Application intent:** A new developer installs Koan, generates an application, and receives the
latest compatible 0.20 fixes with a clean restore.

**Public expression:** `dotnet new install Sylin.Koan.Templates`, `dotnet new koan-web -o TodoApi`,
and `dotnet run`. Generated Koan references use standard NuGet `0.20.*` patch-floating versions.

**Guarantee/correction:** NuGet selects the latest available 0.20 patch and cannot cross into 0.21.
If no 0.20 package exists, restore fails honestly; it does not approximate a nonexistent `0.20.0`
lower bound with NU1603 warnings.

**Complete intent surface:** No additional command, Koan API, decoration, configuration, context, or
runtime prerequisite exists beyond the public expression.

**Public concepts:** Only NuGet's standard floating patch version, which expresses “latest compatible
fix within the supported 0.20 line.”

**Docs read:** `docs/engineering/index.md` requires ordinary packaging and focused validation;
`docs/architecture/principles.md` requires standard .NET and one current path; `README.md`,
`docs/index.md`, `docs/getting-started/quickstart.md`, `docs/getting-started/overview.md`, and the
template companions own the public entry but still describe publication as pending; ARCH-0110 keeps
publication exclusively on the resulting `main` push.

**Code read:** `templates/Sylin.Koan.Templates.csproj` is the content-only package owner;
`templates/koan-web/KoanWebApp.csproj` and `templates/koan-console/KoanConsoleApp.csproj` contain the
four stale ranges; `templates/Directory.Build.props` and `.targets` deliberately prevent hidden
rewrite machinery; the template programs/models/controllers already express the intended four-line
host and Entity-first application.

**Reusing:** The existing content-only template pack, public 0.20 packages, standard NuGet resolution,
the four-line `AddKoan()` host, `Entity<T>`, `EntityController<T>`, and SQLite's autonomous local
default.

**Creating new:** None. The shipped template source and its current public documentation are the
existing owners.

**Coalescence:** The closest pattern is the four direct template `PackageReference` entries. Keep the
content-only package architecture; replace their stale bounded literals at the template source and
delete pending-publication language. A release tool, token replacement phase, central version
constant, Koan abstraction, or generated-app correction would add a second owner.

**Ergonomics:** The developer still learns three ordinary .NET commands and no version choice. The
generated project remains immediately readable in an IDE; `0.20.*` visibly communicates the patch
policy without a Koan-specific concept or restore warning.

**Constraints satisfied:** Business intent leads; standard .NET/NuGet owns the behavior; no runtime,
data, controller, Entity, provider, options, constants, DTO, or shared-contract change exists; the
controller-only and Entity-first guardrails remain intact; documentation becomes instruction-first
and current; validation is limited to isolated public-feed install/generate/restore/build/runtime
proofs.

**Risks:** Floating restore is intentionally variable within the compatible 0.20 line. An application
that requires exact reproducibility can use ordinary NuGet locking; the beginner template optimizes
for receiving current fixes. The public feed may be eventually consistent immediately after a future
publication.

**Observed before correction:** `Sylin.Koan.Templates 0.20.5` installed and generated successfully.
The web template restored and built against `Sylin.Koan.App 0.20.4` and
`Sylin.Koan.Data.Connector.Sqlite 0.20.4`; REST create/read, SQLite persistence, and Koan facts passed.
Both direct references emitted NU1603 because `[0.20.0,0.21.0)` names an unpublished minimum. A
temporary `0.20.*` substitution restored cleanly and resolved both packages to `0.20.4`.

**Focused correction evidence:** The corrected content-only template packed successfully. An isolated
custom hive installed that exact nupkg and generated both public short names. Both generated projects
contained only `0.20.*` Koan references and restored from NuGet.org with no warning. The web project
built with zero warnings/errors. The console project selected local SQLite and passed Entity
save/load/query. The earlier live web host passed SQLite-backed REST create/read and
`/.well-known/Koan/facts`. No full release ratchet was run.

**Separate observed rough edge:** SQLite's zero-configuration local fallback works, but startup first
reports failed service discovery and a corrective endpoint diagnostic before selecting
`.koan/data/Koan.sqlite`. This is not a template or persistence failure; it is a distinct startup
explanation/delight issue for the discovery owner and is not folded into this package-range correction.

This checkpoint supersedes the pre-publication release-compiler mechanics below. Those mechanisms
were subsequently removed; R12-06 records the minimal successful main-boundary publication.

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

## Pre-freeze convergence checkpoint — 2026-07-20

The workstation now has the repository-pinned .NET SDK 10.0.302 installed, and `global.json` resolves it exactly with
roll-forward disabled. Docker Desktop is healthy for container-backed suites. Read-only remote inspection finds
`origin/dev` at `7249ce72ad836324fc02b289e21f39ddcd3b6290`, no
`automation/package-lineage-dev` branch, and the existing Actions secret name `NUGET_API_KEY`; its value was not read
or exposed. The repository API does not affirm immutable Releases, so that setting remains an explicit R12-06
prerequisite rather than being guessed or changed here.

The absent durable lineage makes this candidate an all-current-owner bootstrap through the existing compiler. No
operator package list or predecessor reconstruction is permitted. The dirty source scope consists of the accepted
dependency modernization and compatibility adaptations, the central `Directory.Packages.props` constitution and its
governance test, AnimeRecommendations/Usagi Picks extraction, coherent public narrative, and the final NativeAOT
truth correction. `tmp/` remains scratch-only and is excluded from staging.

GardenCoop's prior win-x64 NativeAOT success is no longer presented as current evidence. The pinned SDK/runtime-pack
combination stops inside the ILC analyzer with an `IndexOutOfRangeException` before producing an executable. Current
public guidance now calls the path experimental, preserves the minimal configuration as a reproducible diagnostic,
and directs candidate users to self-contained or single-file JIT publication. The ordinary GardenCoop journey
remains verified. This narrows an unproved deployment claim; it does not change runtime code or add an exception
mechanism. The exact pinned SDK passes the focused dependency-governance and release-workflow pre-freeze cells 12/12,
and the complete tracked diff passes whitespace validation.

## Work

1. Close R12-04 with maintainer disposition of the completed cold-read evidence and all resulting tracked
   corrections; no additional agent or human validator is required.
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

- Stop until R12-04's maintainer disposition leaves no unresolved contradiction.
- Stop on tracked drift after freeze or any source/version/manifest/artifact mismatch.
- Stop if release selection requires an operator package list or if a lower-maturity package is relabeled 0.20.
- Stop if local-feed success is presented as public availability or independent consumer evidence.
- Stop before any remote mutation; R12-06 owns authorization, publication, and public observation.
