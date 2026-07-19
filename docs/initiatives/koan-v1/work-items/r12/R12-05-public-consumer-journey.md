---
type: SPEC
domain: framework
title: "R12-05 - Prove the Public Consumer Journey"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-19
  status: in-progress
  scope: public-consumer architecture checkpoint; execution waits on R12-04 independent-reader acceptance
---

# R12-05 — Prove the public consumer journey

- Tranche: `T7B — public product maturity`
- Status: `pending — checkpoint recorded; execution waits on R12-04`
- Depends on: passed R12-01 through R12-03 and completed R12-04 independent-reader acceptance
- Unlocks: R12-06 exact initial 0.20 publication and observation
- Owner: the existing release compiler owns exact package/template execution; independent readers own
  public comprehension evidence

## Meaningful outcome

A developer or coding agent with no repository context follows only Koan's public entry path, installs the
exact 0.20 candidate, creates an application, reaches a useful Entity result, changes the persistence choice
without changing business code, diagnoses one rejected intent, and correctly explains the resulting composition.

This slice proves the public product before publication. It does not create an alternate quickstart, consumer
harness, package catalogue, application abstraction, or remote release operation.

## Architecture checkpoint — one package-only consumer path

**Task:** Prove that the coherent R12-04 narrative and selective R12-03 package boundary produce the intended
application experience from an isolated package feed and CLI home.

**Application intent:** “Start from public Koan guidance, build something useful, change one infrastructure
decision without redesigning the application, and understand both success and correction.”

**Public expression:** Use standard `dotnet new install`, `dotnet new koan-web`, `dotnet restore/build/run`,
ordinary `PackageReference`, configuration, HTTP, health, and runtime facts. The shortest host and Entity model
remain unchanged while capability references express deliberate growth.

**Guarantee/correction:** The exact locally compiled candidate must restore without repository sources or ambient
Koan packages, reach the documented business result, expose composition facts matching its lock/package graph,
elect the deliberately referenced local provider, and reject a named unavailable provider with the documented
correction. Any hidden source edge, stale package range, undisclosed configuration, silent fallback, or mismatch
between prose and runtime evidence fails before publication and names the owning public surface.

**Complete intent surface:** install the exact template package; create both public template shapes; restore,
build, and run in an isolated CLI/package home; exercise Entity persistence and HTTP; replace SQLite with one
supported zero-administration provider using only its public package/configuration contract; inspect startup,
health, facts, and `koan.lock.json`; request one unavailable adapter and recover; add the documented Jobs/MCP
progression through GoldenJourney; remove the added capability/provider and return to the prior result.

**Public concepts:** standard .NET templates, NuGet packages and ranges, `PackageReference`, configuration,
hosting, HTTP, and health; Koan's existing Entity language, Reference = Intent, startup report, runtime facts,
composition lock, and generated maturity labels. No R12-specific application concept is introduced.

**Docs read:** the R12 charter and R12-01 through R12-04 contracts; root/template/quickstart front doors;
FirstUse and GoldenJourney; generated product-surface truth; package-first template work in R08-04; retained local
candidate/API-key evidence in R08-05; and R11-06 package rendering/consumer proof.

**Code read:** `PackagePipeline.VerifyCleanRoomAsync`, `TemplatePackageProbe`,
`CleanRoomApplicationCompiler`, `FirstUseApplicationProbe`, `GoldenJourneyApplicationProbe`, application probe
constants/hosting, release evidence/bundle validation, and the focused Packaging test owners.

**Reusing:** The release compiler remains the sole exact-candidate and isolated-feed owner.
`TemplatePackageProbe` remains the template consumer chokepoint; FirstUse and GoldenJourney remain the cumulative
application probes; runtime facts and `koan.lock.json` remain composition truth. Independent readers follow the
same public steps and report confusion as anonymous findings in this card rather than through a new ledger.

**Creating new:** Add only missing assertions or scenario steps to the existing template/application probes when
an executable public promise is not already covered. Prefer extending their existing evidence records and bundle
validation. Add no new runner, scenario DSL, package allowlist, CLI, sample, or maintained support matrix.

**Coalescence:** R08-04 already proves exact template materialization; R08-05 retains exact candidate custody;
R11-06 proves package-page signatures; R12-04 owns narrative coherence. R12-05 composes these at their existing
release boundary and deletes or fixes any duplicated proof it exposes. It does not restate package maturity or
release selection.

**Ergonomics:** Measure time and concepts to first result, edits required to change infrastructure, ability to
predict what a reference activates, correction usefulness, and whether a fresh reader can explain application
versus framework responsibility. The ideal provider swap changes package/configuration intent while leaving
Entity/controller business code untouched.

**Constraints satisfied:** business intent and user delight lead; fewer meaningful moving parts win; standard
.NET concepts precede Koan vocabulary; package and context behavior stay at existing compiler/runtime
chokepoints; only guaranteed owners carry 0.20; `tmp/`, private dogfood, remotes, publication, tags, Releases,
and repository configuration remain outside scope.

**Risks:** A local feed can conceal ambient NuGet caches or source references; automated probes can prove behavior
without proving comprehension; infrastructure replacement can accidentally test an unpromoted provider; a broad
ratchet can obscure a consumer-specific failure. Use an isolated CLI/global-package home, exact candidate feed,
supported zero-administration package, public-context-only readers, and focused evidence. Stop if R12-04 readers
still find an unresolved narrative contradiction or if the candidate package boundary changes.

## Work

1. After R12-04 passes, compile one exact selective-0.20 local candidate through the existing release path.
2. Verify that its template package contains the exact compatible package ranges and no unresolved source/token edge.
3. Run both templates in an isolated CLI and NuGet home using only the exact candidate feed.
4. Extend the web-template proof through create/read, facts, health, lock explanation, supported local-provider
   replacement, removal/recovery, and unavailable-provider correction without changing its business model.
5. Run package-only FirstUse and GoldenJourney as the progressive capability path; retain their existing
   application-owned rule, Jobs, MCP, inspectability, and correction evidence.
6. Give the public entry path and exact candidate to independent human and agent consumers without repository or
   initiative context. Record anonymous observations, corrections, elapsed time, and whether each could explain
   composition and responsibility.
7. Re-run only affected packaging, template, application, docs, skills, lock, and artifact-integrity gates.
8. Produce one go/no-go record for R12-06. Do not publish.

## Acceptance

1. Both public templates install, restore, build, and reach their documented business result from the exact
   candidate with no repository source, external Koan feed fallback, or pre-existing Koan package cache.
2. The web path changes from SQLite to one supported zero-administration provider and back without changing Entity,
   controller, or business-rule code; facts and lock truth match each deliberate reference/configuration state.
3. An unavailable adapter intent fails correctively and recovery requires only removing or correcting that intent;
   there is no silent fallback.
4. Package-only FirstUse and GoldenJourney reproduce their cumulative public results from the same exact candidate.
5. At least one independent human and one independent coding agent complete the path from public guidance only,
   explain what reference plus `AddKoan()` selected, and leave no unresolved contradiction or hidden prerequisite.
6. Evidence records exact source/version commits, package identities/hashes/ranges, isolated environment, commands,
   results, corrections, elapsed time, and anonymous reader findings without creating a second release ledger.
7. Focused affected gates and exact artifact/bundle validation pass; no full release ratchet is repeated without an
   affected dependency.
8. No push, tag, publication, GitHub Release, deployment, secret/configuration change, private dogfood inspection,
   or `tmp/` staging occurs.

## Stop conditions

- Stop execution until R12-04's two independent public-context-only reads pass.
- Stop if the proposed provider is outside the supported 0.20 boundary.
- Stop if proof requires a repository ProjectReference, hidden local feed, manual package alignment, or a new CLI.
- Stop if automation is presented as independent comprehension evidence.
- Stop before any remote mutation; R12-06 owns publication and public observation.
