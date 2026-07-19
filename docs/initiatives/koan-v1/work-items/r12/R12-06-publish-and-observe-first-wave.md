---
type: SPEC
domain: framework
title: "R12-06 - Publish and Observe the First 0.20 Wave"
audience: [architects, maintainers, release-engineers, developers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-19
  status: in-progress
  scope: publication and public-consumer architecture checkpoint; no remote action authorized
---

# R12-06 — Publish and observe the first 0.20 wave

- Tranche: `T7B — public product maturity`
- Status: `pending — execution requires passed R12-05 and separate remote authorization`
- Depends on: passed R12-05 exact frozen candidate
- Unlocks: R12-07 public-to-later-wave upgrade and recovery proof
- Owner: `release-on-dev.yml` owns publication; NuGet and immutable GitHub Release own public evidence;
  independent consumers own comprehension evidence

## Meaningful outcome

The exact R12-05 candidate becomes one recoverable public 0.20 wave. A clean maintainer environment can install Koan
from public NuGet, reach a useful Entity result, replace local persistence
without changing business code, diagnose rejected intent, and explain composition using public guidance only.

## Architecture checkpoint — publish once, then test the public product

**Task:** Authorize one exact `dev` advancement, observe the existing API-key release state machine to terminal
success, then test only the publicly visible packages and guidance.

**Application intent:** “Install Koan normally from NuGet and trust that what I run is the exact candidate Koan
proved, published, and can recover.”

**Public expression:** `dotnet new install Sylin.Koan.Templates`, `dotnet new koan-web`, ordinary
`PackageReference`, configuration, HTTP, health, runtime facts, and `koan.lock.json`. Exact-version commands are
used only to bind validation to the manifest; the unqualified install must resolve to the same current template.

**Guarantee/correction:** The pushed source must equal the frozen R12-05 source. The workflow must re-prove it,
persist exact lineage, upload immutable escrow before promotion, use `NUGET_API_KEY` only in the prepared promotion
step, publish in dependency order, wait for registry visibility, bind one completion receipt and exact tag, and
finish with an immutable Release. Failure preserves the existing prepared escrow and follows coordinator recovery;
it never hand-rebuilds, replaces evidence, moves a tag, or chooses packages manually.

**Complete intent surface:** Revalidate the exact source and remote target; verify the existing publish-scoped
secret and immutable/protected trust prerequisites without exposing the key; obtain explicit authorization;
advance `dev` once; observe prior reconciliation, proof, staging, promotion, visibility, completion, tag, and
immutability; wait for the complete manifest on public NuGet; use isolated CLI/global-package homes and public
sources only; install both templates; run first results; replace SQLite with the supported JSON provider and back
without changing Entity/controller/business-rule code; inspect startup, health, facts, and lock truth; provoke and
recover one unavailable-adapter intent; run public package-only FirstUse and GoldenJourney; conduct the maintainer
journey; record immutable identities, links, findings, and go/no-go for R12-07. Coding-agent evidence may supplement
that journey but is not a gate; the maintainer remains the sole validation authority.

**Public concepts:** Standard NuGet package visibility, SemVer `0.20.x`, `dotnet new`, PackageReference,
configuration, GitHub Release, and Git commit/tag identity; existing Koan Entity, Reference = Intent, runtime facts,
health, and lockfile concepts. “RC” is a process state, not a `-rc` package suffix.

**Docs read:** R12 charter/R12-05; R08-05 retained publication contract; `nuget-publishing.md`; `packaging.md`;
ARCH-0110; public README, quickstart, templates, provider guidance, FirstUse, GoldenJourney, product surface, and
feedback templates.

**Code read:** `release-on-dev.yml`; `ReleaseWaveCoordinator`; `GitHubReleaseWaveEscrow`;
`NuGetPackagePromotionTarget`; release-wave marker/completion models and constants; template/FirstUse/
GoldenJourney probes; workflow, promotion, escrow, visibility, recovery, and credential-redaction tests.

**Reusing:** One push-triggered workflow, six existing permission boundaries, API-key promotion, deterministic
lineage/manifest/escrow/completion, and existing application probes. Public readers use the same docs graph R12-04
already protects. Findings become ordinary repository issues/corrections, not a new maturity database.

**Creating new:** No publication mechanism, credential path, package list, recovery script, CLI, or consumer DSL.
Add only bounded public-feed assertions to existing probes if observation exposes a missing executable promise.

**Coalescence:** Absorb the former R12-05 independent consumer journey into this post-publication observation.
Keep R12-05 as local freeze/certification and R12-07 as later-wave evolution. Preserve R08-05 as historical local
evidence; do not execute or maintain a second release path from it.

**Ergonomics:** The maintainer authorizes one exact source advancement. Automation owns versions, order, escrow,
retry, and recovery. The developer installs one template and changes infrastructure through ordinary package/
configuration intent; the business model remains untouched and runtime explanation is discoverable.

**Constraints satisfied:** standard GitHub/NuGet/.NET concepts first; the existing API key remains; no OIDC or
Koan-specific release ceremony is added; only guaranteed owners carry 0.20; public proof occurs only after public
visibility; no private dogfood or `tmp/` enters evidence.

**Risks:** `dev` advancement is immediately a release event and cannot be used as a harmless staging push. The
first durable lineage may select every active owner once even though only 38 carry the 0.20 guarantee. NuGet has no
multi-package transaction, so exact escrow, dependency order, visibility waits, and recovery are load-bearing.
Remote trust settings are unstable facts and must be re-read immediately before authorization.

## Work

1. Revalidate that local HEAD exactly equals the passed R12-05 source and that no later tracked change exists.
2. Read-only verify remote `dev`, release queue, durable lineage, workflow identity, immutable Release setting where
   observable, branch/workflow protection, and presence/scope posture of `NUGET_API_KEY` without reading its value.
3. Present the exact target, source, likely bootstrap posture, authority boundaries, stop map, and irreversible
   effects; obtain separate explicit authorization for any required setup and the single `dev` advancement.
4. Advance `dev` exactly once through the normal reviewed path. Do not invoke stage/promote manually.
5. Observe all six workflow jobs. On failure, follow the recorded state-specific recovery and preserve escrow.
6. Require every manifest nupkg visible, exact tag/lineage agreement, one completion receipt, and immutable Release.
7. From clean isolated environments using public NuGet only, execute exact and unqualified template installs,
   both first results, JSON provider replacement/removal, rejection/recovery, facts/health/lock inspection, and
   public package-only FirstUse/GoldenJourney.
8. Have the maintainer follow public guidance only. Record confusion, elapsed time, corrections, and the resulting
   explanation of reference/application responsibility. Optional coding-agent evidence may supplement this record
   but is not required.
9. Record immutable evidence and bounded follow-ups. Do not call the wave successful while any public contradiction,
   unavailable manifest package, mutable Release, or unexplained consumer failure remains.

## Acceptance

1. The pushed source is exactly the passed R12-05 freeze and one normal `dev` event owns the wave.
2. Proof/staging/promotion retain their least-privilege boundaries; only prepared promotion receives
   `NUGET_API_KEY`.
3. Durable lineage, source/version commits, manifest, package/symbol hashes, marker, completion receipt, exact tag,
   NuGet visibility, and immutable Release all agree.
4. Every supported owner carries its exact 0.20 identity; additional bootstrap/changed packages retain lower-line
   identity and never acquire an implied support claim.
5. Clean public-feed template, FirstUse, and GoldenJourney execution succeeds without repository/local-feed/cache
   assistance.
6. SQLite → JSON → SQLite changes only package/configuration intent; business code remains unchanged and facts/lock
   explain each result. Invalid adapter intent rejects correctively and recovers by correcting/removing that intent.
7. The maintainer completes the journey and leaves no unresolved contradiction or hidden prerequisite; no second
   agent or human acceptance authority is required.
8. Failures use exact coordinator recovery; no artifact rebuild, manual package choice, tag movement, evidence
   replacement, unlisting, or parallel publication path occurs.
9. Immutable links/identities and bounded feedback are recorded without copying package state into a new ledger.

## Authorization and stop conditions

- This card records architecture only. It does not authorize remote inspection requiring new access, secret or
  repository setting changes, push, tag, Release, or NuGet publication.
- Stop if local HEAD differs from the R12-05 freeze or remote state contradicts its preflight assumptions.
- Stop before advancing `dev` unless required trust prerequisites are positively verified and the exact operation is
  explicitly authorized.
- Stop on public package identity without exact prepared escrow, mutable terminal Release, tag mismatch, or unknown
  partial publication. Preserve evidence and reconcile through the existing coordinator.
- Stop if local-feed or automation-only evidence is substituted for independent public comprehension.
