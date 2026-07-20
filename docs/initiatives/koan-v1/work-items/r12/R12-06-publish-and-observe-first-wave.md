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

### 2026-07-20 first-run portability recovery checkpoint

**Task:** Correct the two Linux-only proof failures from release run `29755662142` without weakening the public
ratchet or changing the release architecture.

**Application intent:** A maintainer advances `dev` and receives the same exact release proof on the Linux workflow
runner that passed on the Windows workstation.

**Public expression:** None. Application code, package references, configuration, runtime prerequisites, package
identities, and the one-push release instruction remain unchanged.

**Guarantee/correction:** The Communication conformance probe must interpret ordinary MSBuild project-reference
paths on both Windows and Linux, and the docs template must not advertise a placeholder as a real link. A real
missing Communication dependency or documentation target still fails. Docs-lint failures must print their full
path, severity, check, and correction in Actions rather than a width-truncated path-only table.

**Complete intent surface:** No user action exists beyond the existing normal `dev` advancement. The first run
failed before staging; `stage_current` and `promote_current` were skipped, no durable lineage branch, tag, Release,
or public package exists, and the next ordinary event remains the all-owner bootstrap.

**Public concepts:** None. Standard MSBuild path syntax, `System.IO.Path`, Markdown link syntax, and PowerShell
diagnostic formatting are sufficient.

**Docs read:** `docs/engineering/index.md` keeps the protected workflow and focused evidence authoritative;
`docs/architecture/principles.md` requires standard .NET concepts and one decision owner; `docs/toc.yml` confirms no
navigation change; the root `README.md` still honestly reports pre-publication status; `samples/CATALOG.md` is an
unrelated retired boundary; `nuget-publishing.md` and this card require an ordinary source correction after a
pre-staging proof failure.

**Code read:** `SemanticActivationManifestBuildTests.cs` owns the conformance table but passes raw backslash XML
includes to platform path APIs; `docs-lint.ps1` correctly identifies the placeholder target on Linux;
`green-ratchet.ps1` requests width-sensitive table output; `docs/engineering/_template.md` owns the fake link; the
six affected package projects use valid ordinary MSBuild backslash references.

**Reusing:** The existing conformance graph, `Path.DirectorySeparatorChar`, `Path.AltDirectorySeparatorChar`, the
existing docs link validator, the existing list output mode, and the unchanged release failure/retry path.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| project-reference include normalization helper | `tests/Koan.Packaging.Tests/SemanticActivationManifestBuildTests.cs` | one test-owned boundary converts cross-platform MSBuild path text before `System.IO` evaluates it |

**Coalescence:** The closest pattern is the existing `ProjectReferences` test helper. Keep that test-local graph
owner and absorb separator normalization there; do not introduce a production path service or evaluate a second
package graph. Keep docs validation in `docs-lint.ps1`; change only its existing caller's rendering mode and make the
template placeholder non-link text. No superseded runtime or release path is created.

**Ergonomics:** Maintainers retain one release action and receive an actionable CI error. Framework users see no
new API or concept, and ordinary MSBuild paths remain ordinary MSBuild paths.

**Constraints satisfied:** No HTTP, data-access, streaming, options, constants, module, public type, or runtime
behavior is involved; no inline endpoints or placeholders are added; current docs/ADR/TOC policy is unchanged; the
focused Packaging test, Windows docs lint, and Linux container reproduction are the bounded proof.

**Risks:** A second platform-specific failure may appear after these fail-fast cells clear. Preserve the same
pre-staging stop boundary and fix only observed evidence; do not bypass either gate or manually stage/promote.

### 2026-07-20 rooted-auth-URI recovery checkpoint

**Task:** Correct the later Linux-only Web Auth failure from release run `29757857299` while preserving the
existing self-hosted test-provider contract.

**Application intent:** A relative self-hosted provider authority such as `/.testoauth` resolves against the
application's Kestrel address in containers exactly as it does on Windows.

**Public expression:** None beyond the existing relative test-provider authority and ordinary
`ASPNETCORE_URLS`/`ASPNETCORE_HTTP_PORTS`/`ASPNETCORE_HTTPS_PORTS` host configuration. Real deployment providers
continue to supply absolute HTTP(S) endpoints.

**Guarantee/correction:** Only absolute HTTP or HTTPS endpoints bypass server-address resolution. A rooted Unix
path must not be mistaken for an absolute `file:` URI; it resolves through the existing wildcard/port-aware owner or
remains relative when no server address exists. Unsupported/unresolvable input retains the current safe fallback.

**Complete intent surface:** No additional user action exists. The second run also failed in the read-only ratchet;
staging and promotion were skipped and no lineage, tag, Release, escrow, or public package was created.

**Public concepts:** None. Standard `System.Uri` HTTP/HTTPS scheme identity expresses the existing network-URL
guarantee.

**Docs read:** `Koan.Web.Auth/README.md` defines maintained external sign-in; `Koan.Web.Auth/TECHNICAL.md` reserves
relative endpoints for the self-hosted local provider and requires absolute HTTP(S) deployment endpoints; the public
Auth card keeps provider configuration as the only application surface; architecture principles keep this decision
inside the Web Auth owner.

**Code read:** `ServerAddressResolver.cs` owns the sole relative-to-server-address conversion and currently accepts
any platform-defined absolute URI; `ServerAddressResolverTests.cs` already covers wildcard, concrete, IPv4, IPv6,
port-fallback, absolute HTTPS, and unresolvable cases; `AuthSchemeSeeder` is only the consumer and needs no change.

**Reusing:** The existing resolver, `Uri.TryCreate`, `Uri.UriSchemeHttp`, `Uri.UriSchemeHttps`, and all 39 focused
Web Auth tests.

**Creating new:** None. Tighten the existing absolute-endpoint predicate in place; no type, method, constant,
option, DTO, service, or configuration key is added.

**Coalescence:** Keep `ServerAddressResolver` as the single Web Auth decision owner. Absorb network-scheme validation
into its existing early-return predicate; do not add a Unix branch, filesystem heuristic, alternate URI parser, or
consumer-side workaround.

**Ergonomics:** Windows and container users keep the same configuration and receive the same URL. The coding model,
IntelliSense surface, and number of concepts remain unchanged.

**Constraints satisfied:** No controller, route, data access, streaming, decoration, option, constant, module, or
public API is added; standard .NET URI concepts are used; current documentation and ADR policy do not change; the
focused Web Auth suite on Windows and Linux is the bounded proof.

**Risks:** Restricting pass-through to HTTP(S) deliberately rejects treating non-network schemes as provider
back-channel URLs; that matches the documented OAuth/OIDC HTTP contract. Continue fail-fast recovery if a later
independent Linux cell appears.

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
