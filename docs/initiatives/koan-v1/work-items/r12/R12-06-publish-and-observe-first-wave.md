---
type: SPEC
domain: framework
title: "R12-06 - Publish and Observe the First 0.20 Wave"
audience: [architects, maintainers, release-engineers, developers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-20
  status: in-progress
  scope: maintainer-authorized first-wave execution; pre-staging recovery and bounded proof redesign
---

# R12-06 — Publish and observe the first 0.20 wave

- Tranche: `T7B — public product maturity`
- Status: `in-progress — maintainer-authorized first-wave execution; no package staged or published yet`
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

### 2026-07-20 async-embedding fixture recovery checkpoint

**Task:** Correct the Data AI timing failure from release run `29759034113` without changing deferred-embedding
production semantics or weakening the vector-only regression proof.

**Application intent:** An application saving an `[Embedding(Async = true)]` Entity receives a durable job whose
`Completed` state means the vector write and model-state confirmation have both completed, without a second save of
the domain Entity.

**Public expression:** None. Applications retain the existing `[Embedding(Async = true)]` declaration, normal
`Save()`, and host-owned `Koan:Data:AI:EmbeddingWorker` policy. Production worker defaults remain unchanged.

**Guarantee/correction:** The integration fixture must not use the production five-second idle cadence as its own
five-second assertion deadline. It supplies a short test-only poll cadence and disables the unrelated global rate
limit, then continues to require the persisted `Completed` state, exactly one domain upsert, and a searchable vector.

**Complete intent surface:** No user action is added. The third run failed in the read-only ratchet after Packaging
and Web Auth passed; staging and promotion were skipped and no lineage, tag, Release, escrow, or public package was
created.

**Public concepts:** None. Standard .NET options configuration on the integration host is sufficient.

**Docs read:** `Koan.Data.AI/README.md` defines queue states and makes polling/rate limits host policy;
`Koan.Data.AI/TECHNICAL.md` requires deferred work to converge on the vector-only writer and records the non-atomic
write/state boundary; this card preserves fail-fast coordinator recovery.

**Code read:** `EmbeddingWorker` persists `Processing`, performs the vector write and durable confirmation, then
persists `Completed`; `EmbeddingWorkerOptions` defaults idle polling to five seconds; the failing fixture starts the
host before enqueue and also polls for only 100 × 50 ms. The isolated test completes at that boundary, confirming a
fixture deadline race rather than a stuck worker or incorrect terminal-state order.

**Reusing:** The existing `EmbeddingWorkerOptions`, normal options configuration, existing integration host, and the
same three behavioral assertions.

**Creating new:** None. Configure the existing worker options inside the existing fixture; add no helper, signal,
type, API, option, or production branch.

**Coalescence:** Keep production ordering in `EmbeddingWorker` and test policy in the fixture. Do not add a second
completion signal, expose worker internals, lengthen a blind wait, or move terminal state ahead of the durable write.

**Ergonomics:** Framework users see no change. Maintainers get a fast deterministic proof that still observes the
public durable state rather than a test-only side channel.

**Constraints satisfied:** No controller, data-access abstraction, streaming path, public API, constant, module, or
production behavior changes; standard .NET options are reused; the focused Data AI test on Windows and Linux is the
bounded proof.

**Risks:** A short fixture cadence increases background polling only inside this one host. If the focused test still
sticks in `Processing`, treat that as a production defect and inspect the write/state boundary rather than relaxing
the assertion.

### 2026-07-20 portable-local-storage-key recovery checkpoint

**Task:** Correct the Local storage contract failure from release run `29760623578` without moving provider-specific
filesystem policy into the Storage abstraction or weakening traversal protection.

**Application intent:** An application using Local storage should get the same accepted key language on Windows,
Linux, and macOS, so a key does not become invalid merely because the application or persisted files move hosts.

**Public expression:** Applications keep ordinary slash-delimited object keys and the existing Local provider. No
registration, option, attribute, package, or API is added.

**Guarantee/correction:** Every key segment rejects control characters and the portable Windows-reserved punctuation
`< > : " | ? *` on every operating system, in addition to characters the current filesystem reports invalid.
Slash remains the logical segment separator and backslash retains its existing normalization to slash. Traversal,
empty-key, containment, sharding, and listing behavior remain unchanged.

**Complete intent surface:** The fourth run passed the repaired Data AI suite and continued through the later test
inventory before Local storage failed `SECURITY-003`; all docs/skills/blueprint legs passed. Current staging,
promotion, escrow, lineage, tag, Release, and public package creation were skipped again.

**Public concepts:** None beyond standard filesystem segments, `Path.GetInvalidFileNameChars`, and `char.IsControl`.

**Docs read:** the Local connector README promises keys normalized below the configured base and rejection of invalid
filename characters; its technical contract locates physical layout and path safety in the connector and promises a
corrective `InvalidOperationException`; the Storage abstractions leave key interpretation to providers.

**Code read:** `LocalStorageProvider.GetPath` is the single path owner; `SanitizeKey` normalizes separators, rejects
dot segments, and currently relies only on the platform-varying `Path.GetInvalidFileNameChars`; all CRUD, range,
stat, and copy paths converge there. `LocalStorageProviderTests.SECURITY-003` explicitly supplies Windows-invalid
punctuation, which Windows rejects and Linux accepts under the current implementation.

**Reusing:** The existing private sanitizer, standard `Path` validation, the existing corrective exception, and the
focused Local connector suite.

**Creating new:** One private static character set inside `LocalStorageProvider` records the stable portable
punctuation floor. It is connector implementation policy, not a public constant or cross-module contract.

**Coalescence:** Keep validation at the existing Local-provider chokepoint. Do not add a Storage-wide key validator,
decorate models, duplicate checks across operations, sanitize by silently renaming keys, or weaken the test to a
Linux-only invalid character. Expand the existing security case so each promised character is independently pinned.

**Ergonomics:** Developers receive one predictable key language and the same corrective error on every supported
host. Existing ordinary names and nested paths remain valid; no ceremony or provider-specific application code is
introduced.

**Constraints satisfied:** The connector remains isolated behind `IStorageProvider`; no controller, data model,
streaming path, option, module, public API, or Koan-specific abstraction is added; standard .NET filesystem concepts
remain the basis; the focused Local suite on Windows and Linux is the bounded proof.

**Risks:** Linux applications that intentionally used the newly reserved punctuation in Local keys will now fail
fast. That is the deliberate cost of a portable Local-provider promise and avoids silent incompatibility when moving
the same application or storage tree to Windows.

### 2026-07-20 SQLite claim-scale sentinel recovery checkpoint

**Task:** Correct the wall-clock-only Jobs SQLite failure from release run `29763701310` without weakening FIFO,
conditional-claim, or indexed-window behavior and without changing production code.

**Application intent:** A durable Jobs host should claim the oldest visible job from a 100,000-row SQLite backlog
through the bounded, pushed-down claim window rather than materializing the ledger in application memory.

**Public expression:** None. Jobs declarations, options, persistence selection, dispatch semantics, and public APIs
remain unchanged.

**Guarantee/correction:** The scale spec continues to seed 100,000 rows, require the exact FIFO head, require its
atomic transition to `Running`, and retain a coarse three-second regression ceiling. The ceiling is test-runner
headroom, not a production latency SLA; a result 8 ms beyond the former 1.5-second cutoff cannot by itself establish
a query-shape regression.

**Complete intent surface:** The fifth run again passed the repaired Data AI boundary but stopped in the Jobs SQLite
suite before reaching Local storage. The same Jobs suite passed the previous run, no Jobs production source changed
between runs, and pack, staging, promotion, escrow, lineage, tag, Release, and public package creation were skipped.

**Public concepts:** None. Standard `Stopwatch` remains only a coarse scale-regression sentinel.

**Docs read:** the Jobs code contract describes the durable ledger as provider-backed and the claim as a bounded
indexed lane seek; this R12 card keeps the workflow as the broad certification owner. No public documentation claims
an absolute 1.5-second SQLite dispatch SLA.

**Code read:** `DataJobLedger.ClaimNext` queries a paginated window ordered by `VisibleAt` and `FirstSubmittedAt`, then
uses provider conditional replacement; `JobRecord` declares the leading `Status, VisibleAt, FirstSubmittedAt` claim
index and the lane claim index. `HighVolumeScanShapeSpec` seeds 100,000 queued rows and correctly pins FIFO/status,
but its class comment claimed a sublinearity ratio that it never measured and its 1.5-second cutoff had no runner
margin. Run `29763701310` measured 1,508 ms; the immediately prior run passed the unchanged suite.

**Reusing:** The existing real SQLite harness, 100,000-row seed, production claim path, FIFO/status assertions, and
single coarse elapsed-time guard.

**Creating new:** None. Change one test threshold and make the existing comment honest; add no helper, benchmark
framework, option, API, or production branch.

**Coalescence:** Keep performance intent in the one existing scale spec. Do not add timing policy to production,
duplicate the claim algorithm in a test, expose SQL internals solely for this recovery, or remove the gross-regression
sentinel. Query-plan introspection can replace elapsed time only as a separately designed adapter testing capability.

**Ergonomics:** Framework users see no change. Maintainers retain a meaningful guard against multi-second/full-ledger
fallbacks without making shared-runner scheduler noise an eight-millisecond release veto.

**Constraints satisfied:** No production, public API, controller, data model, option, constant, module, or docs promise
changes; the focused scale test and full SQLite Jobs suite on Windows/Linux are the bounded proof.

**Risks:** A three-second ceiling remains environment-sensitive and intentionally coarse. It catches gross fallback,
not fine performance drift; a future deterministic query-plan seam would be stronger but is beyond this pre-staging
recovery and is not justified by one 8 ms miss.

### 2026-07-20 bounded certification-wave checkpoint

**Task:** Remove the accidental serial topology from the complete release proof without splitting exact-version
authority across workflow jobs or weakening project-host isolation.

**Application intent:** A maintainer advancing `dev` should receive the complete exact-version certification in
bounded minutes, with independent suites using independent processes and all failures reported before package work
can begin.

**Public expression:** None. The release action remains one ordinary `dev` advancement and the local certification
command remains `pwsh scripts/green-ratchet.ps1 -Configuration Release -PublicRelease`. Maintainers may use the
existing script with an explicit bounded test-project concurrency when diagnosing a constrained workstation.

**Guarantee/correction:** The ratchet continues to discover every runnable `Microsoft.NET.Test.Sdk` project, launch
each project in its own `dotnet test` process with five-minute host-hang detection, and require every project to pass.
Independent project processes now execute in one CPU-bounded worker wave instead of one alphabetical queue. Results
join once inside the same read-only exact-version job; any failed worker makes the ratchet red before pack, escrow,
lineage persistence, staging, or promotion.

**Complete intent surface:** Discover the same project set deterministically; reject an empty set; choose an explicit
positive concurrency or a processor-count-derived default capped at four; start at most that many project processes;
retain each project's complete output and elapsed time; report results in stable project order; report every failure
from the wave rather than hiding later independent defects; leave build, lockfile, docs, public docs, instructional
code, skills, blueprints, pack, clean-room, escrow, and mutation ordering unchanged; use the same path for current
proof and prior-wave recovery.

**Public concepts:** Standard PowerShell parameters and parallel pipelines, `Environment.ProcessorCount`, and one
ordinary child `dotnet test` process per project. No Koan-specific scheduler, test DSL, release identity, or workflow
artifact is introduced.

**Docs read:** `CLAUDE.md`; architecture principles; engineering and test-authoring front doors; `tests/README.md`;
R11-07 certification boundary; R12-06; NuGet publishing guidance; ARCH-0110.

**Code read:** `scripts/green-ratchet.ps1`; its bounded-solution and per-project-isolation history; the complete
`release-on-dev.yml` job/output/permission graph; `ReleaseWorkflowContractTests.cs`; all runnable-project discovery,
parallel-job, and test-host timeout references under `scripts/`, `tests/`, and `.github/`.

**Reusing:** The existing ratchet, runnable-project marker, one-process-per-project contract, full solution build,
five-minute VSTest hang detector, six release authority boundaries, and Packaging workflow-contract suite.

**Creating new:**

| New code | Location | Justification |
| --- | --- | --- |
| None | — | Bounded scheduling belongs inside the existing ratchet owner; another script, workflow job, manifest, or artifact would only duplicate authority. |

**Coalescence:** Restore the bounded concurrency promised by R11-07 while retaining the stronger per-project lifecycle
introduced later. Do not return to one solution-wide VSTest invocation, create 106 GitHub jobs, or split release
lineage compilation and proof across artifact handoffs. One ratchet remains the chokepoint for both current and prior
exact-version certification.

**Ergonomics:** The default uses available CPU but never exceeds four concurrent projects; `-TestProjectConcurrency`
provides a standard explicit override. Logs remain grouped per project and the final summary names every failed
project, so faster feedback is also more actionable than serial fail-fast/restart cycles.

**Constraints satisfied:** Build/test/pack remains read-only and API-key-free; staging and promotion remain unreachable
until the joined ratchet succeeds; every runnable suite remains included and process-isolated; no package identity,
public API, HTTP/data path, module, option, constant, model, documentation curriculum, or remote configuration changes;
`tmp/` remains excluded.

**Risks:** Concurrent integration projects consume more peak CPU, memory, and Docker capacity and may expose a suite
that violates the existing isolated-partition/database/port rule. The cap is deliberately four and overrideable;
such a collision is a test-ownership defect to correct, not a reason to restore a 106-project serial queue. Buffered
per-project output increases transient memory modestly but prevents unreadable interleaving and preserves complete
failure diagnostics.

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

- The maintainer explicitly authorized the normal `dev` release path. This does not authorize repository-secret or
  repository-setting changes, manual tag/Release/package mutation, or publication outside the coordinator.
- Stop if local HEAD differs from the R12-05 freeze or remote state contradicts its preflight assumptions.
- Stop before advancing `dev` unless required trust prerequisites are positively verified and the exact operation is
  explicitly authorized.
- Stop on public package identity without exact prepared escrow, mutable terminal Release, tag mismatch, or unknown
  partial publication. Preserve evidence and reconcile through the existing coordinator.
- Stop if local-feed or automation-only evidence is substituted for independent public comprehension.
