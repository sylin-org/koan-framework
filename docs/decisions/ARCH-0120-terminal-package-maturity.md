---
id: ARCH-0120
slug: terminal-package-maturity
domain: Architecture
status: Accepted
date: 2026-07-21
title: Terminal package maturity and evidence-driven 0.20 admission
related:
  - ARCH-0091
  - ARCH-0094
  - ARCH-0105
  - ARCH-0109
  - ARCH-0110
  - ARCH-0118
---

# ARCH-0120: Terminal package maturity and evidence-driven 0.20 admission

## Outcome

Koan will bring every package owner that is below 0.20 to a terminal maturity decision. A retained
package reaches 0.20 only when its public guarantee, complete dependency boundary, corrective
behavior, consumer journey, and proportionate tests are accepted together. A package whose accepted
home is elsewhere reaches terminal maturity through proved absorption, migration, or retirement—not
through a false Koan support label.

This record governs R13, the terminal package-maturity epic. The R13 work item is the mutable
execution charter and terminal outcome record; short-lived wave cards may record exact findings,
but they do not redefine this admission method or create another maturity system.
The first dependency-closed R13 wave also supplies R12-07's separate preview-evolution proof.

## Context

At this decision point the generated product surface contains 93 active packable projects:

- 38 owners are supported on 0.20;
- 55 owners remain on earlier lines: 19 belong to demonstrated claims, two to verified claims, two
  to an experimental claim, and 32 are unassessed;
- all 93 satisfy the R11 structural package-quality bar, but structural quality, compilation, pack
  success, and test count are not support evidence by themselves.

The product compiler already enforces a useful bidirectional law: every supported claim owner is on
0.20, its public Koan dependency closure is supported, and every 0.20 package is owned by a supported
claim. A proposed common 0.20 release cohort would have severed that law and made package version
mean availability rather than earned support. We reject that proposal and retain the stronger
signal.

The remaining surface is not one homogeneous backlog. It includes testing infrastructure, inert
contracts, local and remote data providers, storage and media, vector databases, AI runtimes,
external authentication, and a previously accepted cross-repository AI handoff. They need common
admission mechanics but family-specific proof.

## Decision

### 1. Terminal maturity has four valid dispositions

Every one of the 55 baseline owners must finish in exactly one state:

1. **Supported:** Koan owns an accepted public guarantee and proportionate evidence. The claim and
   project-local version move atomically to `supported-foundation` or `supported-extension` and 0.20.
2. **Absorbed:** another supported owner becomes the simpler public boundary, consumers and behavior
   move there, and the redundant package leaves the active packable surface.
3. **Migrated:** an accepted public repository owns the behavior, its replacement evidence is green,
   consumers move, and the Koan package leaves the active packable surface.
4. **Retired:** no product claim justifies the package; its removal and public correction are
   explicit, and it leaves the active packable surface.

“Demonstrated,” “verified,” “experimental,” and “unassessed” remain honest intermediate evidence
states. They are not terminal outcomes for this epic. Publication, repository membership, or a
dependency edge cannot promote a package.

Leaving the active packable surface changes current source and generated product truth; it does not
erase immutable historical NuGet artifacts. A migrated or retired public ID receives a truthful
replacement/correction path where one exists.

### 2. Preserve the 0.20 support invariant and add bounded epic certification

The existing product-surface compiler remains the authority and must continue to reject:

- a supported claim whose owner is not on 0.20;
- a supported owner whose public Koan dependency closure is not supported;
- a 0.20 package not owned by a supported claim.

For final R13 certification, reconcile each of the 55 owners recorded in this decision as either
supported in the compiler-validated active graph or absent through accepted absorption, migration,
or retirement. This bounded epic check does not become a product-compiler input or a permanent
rejection of future unassessed packages: ARCH-0118 continues to surface a newly introduced package
as unassessed until a deliberate product decision is made.

When an owner leaves the graph, its wave record captures the exact absorption/migration/retirement
evidence and adds one entry to the bounded
`docs/initiatives/koan-v1/R13-TERMINAL-OUTCOMES.json` certificate. That artifact
contains only removed baseline owners, their disposition, destination where applicable, exact public
commit, runnable commands, and evidence; it contains no active-package maturity state. Final R13
certification compares the fixed 55-row ADR baseline with compiler-validated active supported owners
plus those entries and rejects a missing or duplicate owner. The certificate is not a
product-compiler source or permanent package registry.

There is no central version override, repository-wide package list, or second maturity ledger.
Versions remain project-local. Generated product truth remains a projection of evaluated package
facts plus `product/claims.json` under ARCH-0118.

### 3. Execute the 55-owner baseline in this dependency order

The sequence is an admission order, not a promise that every entry will survive as its own package.
Within a wave, an owner can advance only after the earlier owners on which its public boundary
depends are terminal.

Before package 1, complete one R13 bootstrap gate without promoting a package:

1. seed project-local `PackageValidationBaselineVersion` for the existing 38 supported
   assembly-bearing owners from their immutable first 0.20 artifacts and prevent another 0.20 patch
   until validation is active;
2. add the real product-surface compile and generated-output drift check to the `main` pull-request
   gate;
3. make the common admission runner result-aware and deadline-bounded across startup, execution,
   readiness, and teardown, with timeout cleanup and failure;
4. let durable claim evidence declare its required admission-cell IDs and lane kind without storing
   run results, then make one always-emitted protected check require exact-merge-candidate results or
   return machine-derived N/A only when no affected claim requires a native lane;
5. create the empty bounded `docs/initiatives/koan-v1/R13-TERMINAL-OUTCOMES.json` certificate and its
   fixed-baseline reconciliation test outside `ProductSurfaceCompiler`.

| # | Wave | Package owner | Intended decision surface |
|---:|---:|---|---|
| 1 | 0 | `Sylin.Koan.Testing.Hosting` | standard real-host test ownership |
| 2 | 0 | `Sylin.Koan.Testing` | public Koan testing facade |
| 3 | 0 | `Sylin.Koan.Testing.Containers` | container-backed provider-test support |
| 4 | 0 | `Sylin.Koan.Cache.Adapter.Sqlite` | durable local cache provider |
| 5 | 0 | `Sylin.Koan.Data.SoftDelete` | Entity soft-delete semantics |
| 6 | 0 | `Sylin.Koan.Web.Admin` | Development-only authenticated read-only diagnostics projection |
| 7 | 0 | `Sylin.Koan.Web.Auth.Connector.Test` | deterministic authentication test provider |
| 8 | 1 | `Sylin.Koan.AI.Contracts` | inert AI provider/module-author request, result, routing, and prompt contracts |
| 9 | 1 | `Sylin.Koan.ZenGarden.Contracts` | inert Zen Garden exchange contracts |
| 10 | 2 | `Sylin.Koan.Redis.Abstractions` | shared Redis contract boundary |
| 11 | 2 | `Sylin.Koan.Redis` | shared Redis connection/configuration owner |
| 12 | 2 | `Sylin.Koan.Data.Connector.Redis` | Redis Entity persistence |
| 13 | 2 | `Sylin.Koan.Cache.Adapter.Redis` | Redis cache behavior |
| 14 | 2 | `Sylin.Koan.Data.Relational.Npgsql` | shared Npgsql relational mechanism |
| 15 | 2 | `Sylin.Koan.Data.Connector.Postgres` | PostgreSQL Entity persistence |
| 16 | 2 | `Sylin.Koan.Data.Connector.Cockroach` | CockroachDB relational delta |
| 17 | 2 | `Sylin.Koan.Data.Connector.SqlServer` | SQL Server Entity persistence |
| 18 | 2 | `Sylin.Koan.Data.Connector.Mongo` | MongoDB Entity persistence |
| 19 | 2 | `Sylin.Koan.Data.Connector.Couchbase` | Couchbase Entity persistence |
| 20 | 3 | `Sylin.Koan.Storage.Abstractions` | storage contracts and capabilities |
| 21 | 3 | `Sylin.Koan.Storage` | storage selection/routing runtime |
| 22 | 3 | `Sylin.Koan.Storage.Connector.Local` | durable local storage floor |
| 23 | 4 | `Sylin.Koan.Storage.Connector.S3` | S3-compatible storage delta |
| 24 | 4 | `Sylin.Koan.Data.Backup` | integrity-first backup and restore |
| 25 | 4 | `Sylin.Koan.Media.Abstractions` | inert media and recipe vocabulary |
| 26 | 4 | `Sylin.Koan.Media.Core` | media transform/lifecycle runtime |
| 27 | 4 | `Sylin.Koan.Media.Web` | media HTTP projection |
| 28 | 4 | `Sylin.Koan.ZenGarden` | public Zen Garden integration |
| 29 | 5 | `Sylin.Koan.Data.Vector.Abstractions` | vector contracts and capability vocabulary |
| 30 | 5 | `Sylin.Koan.Data.Vector` | Entity-first vector persistence, search, election, naming, and participation |
| 31 | 5 | `Sylin.Koan.Data.Vector.Connector.InMemory` | deterministic local vector provider |
| 32 | 5 | `Sylin.Koan.Data.Vector.Connector.SqliteVec` | durable local vector provider |
| 33 | 6 | `Sylin.Koan.Data.Vector.Connector.Qdrant` | Qdrant vector provider delta |
| 34 | 6 | `Sylin.Koan.Data.Vector.Connector.Milvus` | Milvus vector provider delta |
| 35 | 6 | `Sylin.Koan.Data.Vector.Connector.Weaviate` | Weaviate vector provider delta |
| 36 | 6 | `Sylin.Koan.Data.SearchEngine` | shared search-engine mechanism |
| 37 | 6 | `Sylin.Koan.Data.Connector.ElasticSearch` | Elasticsearch Entity/search delta |
| 38 | 6 | `Sylin.Koan.Data.Connector.OpenSearch` | OpenSearch Entity/search delta |
| 39 | 7 | `Sylin.Koan.AI` | mainline AI runtime and selection |
| 40 | 7 | `Sylin.Koan.AI.Prompt` | optional Entity-backed named/versioned prompt catalog resolution |
| 41 | 7 | `Sylin.Koan.AI.Web` | AI HTTP projection |
| 42 | 7 | `Sylin.Koan.AI.Connector.LMStudio` | LM Studio protocol/provider delta |
| 43 | 7 | `Sylin.Koan.AI.Connector.Ollama` | Ollama protocol/provider delta |
| 44 | 7 | `Sylin.Koan.AI.Connector.Onnx` | local ONNX inference delta |
| 45 | 7 | `Sylin.Koan.Data.AI` | Entity AI/vector integration |
| 46 | 8 | `Sylin.Koan.Web.Auth.Connector.Google` | Google authorization-code provider delta |
| 47 | 8 | `Sylin.Koan.Web.Auth.Connector.Microsoft` | Microsoft authorization-code provider delta |
| 48 | 8 | `Sylin.Koan.Web.Auth.Connector.Discord` | Discord authorization-code provider delta |
| 49 | 9 | `Sylin.Koan.AI.Contracts.Shared` | cross-repository lifecycle exchange boundary |
| 50 | 9 | `Sylin.Koan.AI.Orchestration` | migrate orchestration behavior or reverse its prior disposition |
| 51 | 9 | `Sylin.Koan.AI.Agents` | migrate agent behavior or reverse its prior disposition |
| 52 | 9 | `Sylin.Koan.AI.Eval` | migrate evaluation behavior or reverse its prior disposition |
| 53 | 9 | `Sylin.Koan.AI.Review` | migrate review behavior or reverse its prior disposition |
| 54 | 9 | `Sylin.Koan.AI.Models` | migrate model-catalog behavior or reverse its prior disposition |
| 55 | 9 | `Sylin.Koan.AI.Connector.HuggingFace` | migrate catalog/provider behavior or reverse its prior disposition |

### 4. Apply explicit exit gates to each wave

| Wave | Exit gate |
|---:|---|
| 0 | Before trusting the shared substrate, make touched test bases fail closed on setup/teardown, remove direct ambient-host mutation, and prove repeated-host isolation. Consolidate duplicated Web container fixtures and extract reusable Cache conformance rather than starting a repository-wide harness rewrite. Then prove the public testing helpers through downstream meta-tests and package-only consumers; each quick-win runtime still needs its own guarantee, correction, and consumer proof. |
| 1 | Contract packages compile and pack with intentional dependency shape, no runtime module/contribution or functional implementation dependency, inert reference behavior, and an explicit first-0.20 API review. `AI.Contracts` must own a genuine provider/module-author guarantee in this wave because Zen Garden and mainline AI depend on it; inability to state that guarantee stops and revises this decision rather than deferring the owner. Uniform build-transitive semantic-activation metadata may remain. |
| 2 | Reuse shared Data/Cache conformance, including the Wave 0 extracted Cache contract; prove real provider protocol/container behavior, explicit configuration precedence and redaction, pool lifecycle, and PMC-018 shared configuration/provenance. |
| 3 | Establish one provider-neutral storage contract and prove connector-owned Local behavior, including PMC-027 activation, path safety, capabilities, failure, and lifecycle. |
| 4 | Prove S3 against a hermetic S3-compatible service; backup through real restore, corruption, and fail-before-mutation; media through PMC-022 derivative identity/lifecycle/migration; and Zen Garden only through public-repository evidence. |
| 5 | Establish the local vector semantic floor. Replace or tightly bound PMC-008's reflective bridge with typed execute/reject behavior before remote vector providers inherit it. |
| 6 | Run the existing vector surface, semantic, filter, and AODB matrices plus each provider's real protocol/container limits. `Data.SearchEngine` must be terminal before its Elasticsearch and OpenSearch owners. |
| 7 | Close PMC-030 mixed disposal with repeated-host, failed-activation, exactly-once disposal, cancellation, and commit-boundary evidence. Use deterministic wire-contract servers for LM Studio/Ollama, a real local model for ONNX, and admit `Data.AI` only after Vector is terminal. |
| 8 | Use data-driven fake upstream identity services while referencing and activating each real Google, Microsoft, and Discord connector assembly. Prove its concrete authorization-code start/callback/token endpoints, mappings, configuration, state/failure handling, and secret redaction. Live third-party credentials are not required for deterministic support evidence. |
| 9 | Reconcile the accepted cross-repository ownership decision. `AI.Contracts.Shared` may remain as the dependency-free exchange boundary only with a public destination-consumer build and API agreement. Agents/Orchestration retire after equivalent public Agyo.Rag behavior is green; Eval and Review retire after `Sylin.Agyo.Eval` and `Sylin.Agyo.Review` respectively are green; Models/HuggingFace retire after equivalent public Zen Garden behavior is green. Destination proof is pinned to exact public commits and runnable commands. Supporting any of those six in Koan requires an explicit reversal of R11's disposition. |

Before Wave 1, split the current coupled `ai-prompt-and-http-projection` claim into a genuine
provider/module-author contract guarantee for `AI.Contracts` and separate catalog/projection
outcomes. If that cannot be done honestly, stop and revise the sequence; deferral is not a terminal
state. Before Wave 5, separate the current combined AI/vector claim into genuine Vector and AI user
outcomes so Vector can be admitted independently. These are product-claim corrections, not
package-row claims created to satisfy the wave table.

### 5. Use one maturity-admission method

Every owner or tightly coupled family follows the same checkpoint before production changes:

1. State the capability in one business sentence.
2. Record the smallest public expression and its full reference, configuration, runtime, and
   infrastructure prerequisites.
3. Freeze the guarantee, explicit non-claims, and corrective failures.
4. Inventory current public consumers and executable evidence; private dogfood is not evidence.
5. Identify the nearest accepted pattern and the real mechanism owner; prefer coalescence over a new
   abstraction.
6. Choose `keep`, `absorb`, `migrate`, or `retire` before strengthening tests around an accidental
   boundary.
7. Add the smallest missing red proof, repair the owner, and run its focused owner/consumer proof.
8. Prove package shape and an isolated package consumer, review the candidate public API, and prepare
   the standard compatibility check that will bind to the first immutable 0.20 artifact.
9. Admit or remove the owner atomically, compile generated truth, and update current public guidance.
10. Observe the dependency-closed wave before using it as the floor for the next wave.

Shared mechanisms do not need invented package-row promises. They are admitted as technical owners
inside a real user-capability claim, with their dependency and consumer evidence visible from that
claim.

### 6. Make evidence proportional but non-negotiable

| Owner shape | Minimum evidence |
|---|---|
| Inert contracts | clean compile/pack; public dependency and artifact shape; reference-without-activation proof; API review/baseline; at least one real consumer compile |
| Host/runtime module | real `AddKoan()` host; selected activation and unselected inertness; explicit configuration precedence; corrective failure; facts/health/redaction; repeated-host lifecycle and cancellation where state exists |
| Provider/connector | the shared family semantic suite; provider-specific delta; real container, native runtime, or protocol proof; capability/limit narration; unavailable-infrastructure correction |
| Projection/tooling | real host plus HTTP or the actual user action; authorization and serialization boundaries; corrective responses; one package-only consumer journey |
| Migration/retirement | destination behavior and destination tests green; public ownership and consumer migration explicit; old package has no active claim or graph owner; removal and correction compile cleanly |

The existing family kits are extended instead of creating 55 bespoke suites or a universal new test
framework. For Data/Vector AODB cells, `scripts/forge-verify.ps1` supplies the runner and result
semantics. Storage, AI, Auth, and other native families use their own exact runners while preserving
the same rule: a required missing cell, missing result, or skip is inconclusive and cannot count as
green.

Admission runners parse TRX/xUnit results rather than trusting only a process exit code or nonzero
test total. Every named admission cell must report `Passed`; required skips, unknown outcomes,
missing results, and any nonzero test-process exit fail certification. Before Data/Vector admission,
`forge-verify.ps1` must itself fail closed on process exit and unknown outcomes and include the current
provider-bounded-streaming record cell. A certification fixture records infrastructure provenance:
it owns the container/native service or verifies the explicitly supplied endpoint identity. An
unverified environment override remains a developer convenience, not maturity evidence.

Every suite cited for admission—not only the Wave 0 shared bases—must fail closed on setup,
readiness, teardown, ambient-host restoration, and bounded deadlines before its result qualifies.
The bootstrap gate establishes the common enforcement; each later family repairs its cited base
before relying on it.

Every claim added or materially changed by R13 names exact runnable test-project paths and at least
one consumer journey in `product/claims.json`; broad `src` paths, prose, aggregate test counts, and
unrelated suites do not qualify for a new admission. The product compiler validates evidence
identity and existence; CI and the native lane prove execution green. Existing supported claims are
not reopened solely to normalize their older evidence paths, but adopt this form when affected.

### 7. Establish API compatibility at admission

Before an owner's first 0.20 publication, its candidate public API receives an explicit review. The
resulting immutable first 0.20 artifact is then recorded through project-local
`PackageValidationBaselineVersion` for the next change. Every later 0.20 patch must pass package/API
compatibility validation against that artifact. A narrow documented suppression may resolve a
tooling false positive; a genuine public break advances the compatibility tier and cannot be waived
into a 0.20 patch. Koan will use the platform tooling rather than invent an API manifest or
compatibility subsystem.

This baseline protects a proved contract; it must not be enabled early to freeze an accidental API.
The existing 38 supported owners seed their baseline from their already published first 0.20
artifact without reopening their maturity evidence. A package with no public assembly does not use
API compatibility tooling; its existing artifact/dependency-shape and isolated-consumer checks
remain the relevant protection.

After an owner's first 0.20 publication, record the exact immutable version in its project-local
package-validation configuration. The release gate prevents a second 0.20 patch until validation
against that artifact is active. Existing supported owners receive the same lightweight baseline
seeding, but their capability evidence is not re-certified unless affected.

### 8. Admit support and version atomically

Only after all focused evidence is green does one change:

- set the claim's accepted `supported-foundation` or `supported-extension` maturity and exact
  owner/test evidence in `product/claims.json`;
- set each admitted owner's project-local `version.json` to 0.20;
- run the product compiler's claim/version/dependency checks and regenerate its projections;
- update the package-owned public contract, non-claims, and corrective guidance;
- pack the affected dependency-closed set and prove an isolated consumer.

No owner moves to 0.20 in an earlier preparatory change. Absorbed, migrated, and retired owners leave
the active package graph instead of receiving a terminal version bump.

### 9. Exploit the current strategic leverage without adding a subsystem

- **Testing packages first:** Wave 0 turns the existing Koan testing libraries into a supported
  substrate used by later family proofs.
- **Family matrices:** one Data, Storage, Vector, AI-wire, and OAuth provider contract separates
  shared semantics from provider deltas and makes future connectors cheaper to admit.
- **Exact evidence as product truth:** new or affected claims point to runnable suites and consumers, letting the
  existing compiler detect support drift rather than creating another dashboard.
- **Compatibility floor:** each newly admitted 0.20 API acquires a stable patch-line baseline while
  the surface is already under deliberate review.
- **Validation-only native lane:** repurpose the disabled canary workflow for long-running container,
  native-model, and provider certification. One always-emitted branch-protected admission check
  derives applicability from affected claims' declared cell IDs, accepts only results bound to the
  exact PR merge candidate, and reports N/A only when no affected claim requires the lane. A skipped
  conditional job cannot produce green admission. The lane carries no NuGet credential and never
  publishes; deterministic tests remain on the ordinary `main` pull-request gate.
- **Compile truth before merge:** before the first admission, add the real product-surface compiler
  and generated-output drift check to the `main` pull-request gate; compiler unit tests alone are
  insufficient.
- **Ownership cleanup:** the final AI wave completes the already accepted public cross-repository
  handoff instead of preserving duplicate packages merely to make the inventory uniformly versioned.
- **Preview evolution proof:** the first new dependency-closed R13 publication wave must also prove
  R12-07's later-wave upgrade and interrupted-publication recovery contract, allowing R12 to close
  independently of the longer cross-repository handoff.

### 10. Keep publication at the existing boundary

ARCH-0110 remains unchanged:

- work on `dev` triggers no validation or publication workflow;
- pull requests targeting `main` run the existing validation gate and cannot publish;
- the resulting `main` push publishes the supported 0.20 set through the single existing job;
- no workstation publication, tag, GitHub Release, lineage branch, escrow, or second release
  coordinator is introduced.

Focused owner and consumer tests run during a slice. The existing `main` pull-request ratchet absorbs
new deterministic suites. The validation-only native lane certifies required long provider cells.
Run the full local release ratchet once at explicit final epic certification, not after every owner.
Each dependency-closed wave may be published and observed independently.

## Acceptance

R13 satisfies this decision only when:

1. all 55 baseline owners are accounted for exactly once as supported on 0.20, absorbed, migrated,
   or retired;
2. every retained active package is owned by at least one supported claim, with no accidental
   unassessed surface;
3. every supported owner's complete public Koan dependency closure is supported;
4. every claim added or materially changed by R13 points to exact runnable owner tests and at least
   one consumer journey;
5. every native/provider cell required by an R13 admission is green rather than skipped or
   unavailable;
6. every retained assembly-bearing owner has an accepted first-0.20 API baseline and subsequent
   patches enforce it; content-only owners have the equivalent artifact/dependency-shape check;
7. generated product truth, package-owned guidance, and current public docs agree;
8. the first later wave proves public upgrade and interrupted-publication recovery;
9. one explicit final certification passes the complete release ratchet after the terminal graph
   converges.

The already supported 38-owner set is preserved. Completed Tenancy and Classification suites are not
re-certified unless an affected dependency or contract changes.

## Stop conditions

- Stop a slice if it proposes a mechanical version change or treats build/package quality as support.
- Stop if a required provider cell is skipped, silently downgraded, or replaced by an in-memory fake.
- Stop if tests invent cross-provider parity beyond the accepted common contract.
- Stop if a migration-target AI package is supported without explicitly reversing R11's public
  ownership decision.
- Stop if private applications or private artifacts are required to substantiate a public claim.
- Stop if implementation creates another maturity ledger, release coordinator, bespoke compatibility
  format, or universal testing framework.
- Stop if a slice repeats R11 topology/polish work without evidence that the accepted boundary is
  wrong.

## Consequences

### Positive

- 0.20 remains a meaningful, compiler-enforced support signal.
- The active package surface becomes terminally owned instead of indefinitely unassessed.
- Shared provider matrices and supported test helpers lower the cost of later capability growth.
- New 0.20 APIs gain an explicit patch-compatibility floor at the moment their contract is deliberate.
- Cross-repository ownership is completed without relying on private dogfood.

### Costs and risks

- This is slower than a version sweep and requires real provider/native infrastructure.
- Provider services and external protocols can be flaky; required absence therefore remains
  inconclusive rather than producing a false pass.
- Enabling compatibility too early could freeze a poor API; the guarantee review precedes the
  baseline.
- Family work can expand into feature development; explicit guarantees and non-claims bound each
  slice.
- Cross-repository migration can delay the final wave; the Koan package remains honestly nonterminal
  until its public destination is proved.

## References

- [ARCH-0091 — integration-test harness redesign](ARCH-0091-integration-test-harness-redesign.md)
- [ARCH-0094 — Adapter Forge](ARCH-0094-adapter-forge.md)
- [ARCH-0105 — Koan product constitution](ARCH-0105-product-constitution.md)
- [ARCH-0109 — bounded bootstrap test lanes](ARCH-0109-bounded-bootstrap-test-lanes.md)
- [ARCH-0110 — main-boundary independently versioned package releases](ARCH-0110-main-release-boundary.md)
- [ARCH-0118 — evidence-derived product surface](ARCH-0118-evidence-derived-product-surface.md)
- [R11 — Graduate the NuGet product surface](../initiatives/koan-v1/work-items/R11-package-product-quality.md)
- [R12 — Road to the 0.20 preview](../initiatives/koan-v1/work-items/R12-road-to-020-preview.md)
- [R13 — Terminal package maturity](../initiatives/koan-v1/work-items/R13-terminal-package-maturity.md)
