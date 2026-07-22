---
type: SPEC
domain: framework
title: "R13 - Terminal Package Maturity"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-21
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-21
  status: in-progress
  scope: all five pre-Wave-0 bootstrap protections passed; package maturity waves not started
---

# R13 — Terminal package maturity

- Tranche: `T8 — terminal package maturity`
- Status: `in-progress`
- Depends on: passed R11, completed R12-06, and accepted
  [ARCH-0120](../../../decisions/ARCH-0120-terminal-package-maturity.md)
- Coordinates with: [R12-07](r12/R12-07-preview-evolution.md); the first dependency-closed R13
  publication wave supplies its upgrade and interrupted-publication recovery proof
- Unlocks: an active Koan package graph in which every retained owner has an accepted support
  contract and every non-Koan behavior has completed its public migration or retirement
- Owner: the 55-package baseline below 0.20 at ARCH-0120's acceptance point

## Meaningful outcome

Every owner in the fixed 55-package baseline reaches a terminal product decision. A retained Koan
package earns supported 0.20 admission through its guarantee, corrective behavior, dependency
closure, focused tests, consumer proof, package/API compatibility boundary, and generated truth.
An owner whose accepted home is elsewhere completes absorption, public migration, or retirement and
leaves the active package graph without receiving a cosmetic support version.

The result is not “93 packages at the same number.” It is one package surface whose version signal,
support claim, dependency graph, tests, public narrative, and ownership agree.

## Authority and tracking

- ARCH-0120 owns the immutable baseline, exact 1–55 sequence, maturity method, evidence rules,
  admission transaction, strategic leverage, and stop conditions.
- This work item owns the epic's execution boundary and acceptance. It does not repeat the package
  table or redefine the decision.
- [`PROGRESS.md`](../PROGRESS.md) remains the initiative's only live status ledger.
- A wave card opens only when the preceding wave's evidence sizes its real work. It records exact
  claims, test commands, defects, dispositions, and outcomes for that wave.
- Removed-owner outcomes also enter the bounded
  `docs/initiatives/koan-v1/R13-TERMINAL-OUTCOMES.json` final certificate; it is evidence for this
  fixed epic, not a live maturity ledger or product-compiler input.
- R11's `keep`, `absorb`, `migrate`, and `retire` topology decisions remain settled unless current
  support evidence directly disproves one.

## Dependency-ordered execution

| Wave | ARCH-0120 positions | Meaningful outcome |
|---:|---:|---|
| Bootstrap | — | seed existing 0.20 API baselines; add real product-compile drift checking, result/deadline-aware admission, exact-SHA native applicability, and bounded terminal-outcome reconciliation |
| 0 | 1–7 | supported testing substrate plus four evidence-rich quick wins |
| 1 | 8–9 | genuine inert AI provider-author and Zen Garden exchange-contract guarantees |
| 2 | 10–19 | shared Redis/Npgsql roots and remote Entity/cache providers |
| 3 | 20–22 | provider-neutral Storage contract, runtime, and durable Local floor |
| 4 | 23–28 | S3, Backup, Media, and public Zen Garden integration |
| 5 | 29–32 | provider-neutral and local Vector semantic floor |
| 6 | 33–38 | remote Vector and Search provider families |
| 7 | 39–45 | retained mainline AI runtime, prompt catalog, projections, local providers, and Data.AI |
| 8 | 46–48 | deterministic external authentication provider contracts |
| 9 | 49–55 | cross-repository AI contract decision and completed public handoffs/retirements |

The pre-Wave-0 bootstrap and Wave 0 implementation pass. Seven owners are locally admitted as
supported 0.20 and terminal reconciliation reports `7/55` resolved. Publication and public consumer
observation remain the boundary before Wave 1; no later owner advances around that unresolved public
dependency.

## Pre-Wave-0 bootstrap children

| Child | Protection | Status | Depends on |
|---|---|---|---|
| [R13-01](r13/R13-01-supported-api-baselines.md) | seed and enforce first-0.20 package/API baselines | passed | accepted R13 plan |
| [R13-02](r13/R13-02-pr-product-surface-drift.md) | compile product truth and reject generated drift at the `main` PR boundary | passed | R13-01 |
| [R13-03](r13/R13-03-result-aware-admission.md) | result-aware, deadline-bounded admission execution | passed | R13-02 |
| [R13-04](r13/R13-04-native-admission-applicability.md) | always-emitted exact-candidate native applicability/result | passed | R13-03 |
| [R13-05](r13/R13-05-terminal-outcome-reconciliation.md) | bounded removed-owner certificate and fixed-baseline reconciliation | passed | R13-04 |

All five bootstrap children pass, so Wave 0 may now open. The sequence kept each protection
reviewable and lets the package waves consume the exact contracts established here without
introducing a second product, release, or test system.

## Package-wave children

| Child | Owners | Status | Public boundary |
|---|---:|---|---|
| [R13-06](r13/R13-06-wave-0-testing-and-quick-wins.md) | 1–7 | passed | publication and R12-07 observation pending |

Wave 0 passes eleven exact deterministic/native cells with 53/53 named results, a seven-package
external consumer, generated product truth at 33 claims / 93 packages, API posture at 35/42
configured plus seven first-publication pending, and partial terminal reconciliation at 7/55.

## Bootstrap exploration checkpoint

**Task:** Establish the five compiler/CI/evidence protections that must exist before any R13 package
owner can be promoted or removed.

**Application intent:** A maintainer reviewing a pull request can trust that a supported 0.20 package
keeps its admitted API, generated product truth matches source, every required admission result is
present and passed within a bounded lifecycle, native evidence belongs to the exact merge candidate,
and every removed R13 owner remains reconcilable to ARCH-0120.

**Public expression:** No application C# changes. Maintainers keep using standard project-local
MSBuild properties, `product/claims.json`, ordinary `dotnet test` projects, GitHub's `main` pull-request
boundary, and the existing `Koan.Packaging` command. The complete action surface is: record a package
baseline in its owning project; declare exact admission cells only when a claim is added or materially
changed; run the focused local command; open a pull request to `main`; read deterministic and native
admission checks.

**Guarantee/correction:** A later 0.20 package cannot pack past a genuine API break; checked-in
product projections cannot drift; process success without every required Passed result is rejected;
timeouts terminate the owned process tree and fail; native N/A is derived only when no affected claim
requires a native cell; and a removed baseline owner cannot disappear from final R13 accounting.
Every rejection names the owner/cell/candidate and the safe command or source to correct.

**Complete intent surface:** The 93 evaluated packable projects; the 38 currently supported owners;
their project-local versions and package shapes; standard SDK package validation; the product compiler
and generated JSON/Markdown; the `main` PR gate; bootstrap and family admission runners; claim evidence
declarations; the disabled canary workflow; ARCH-0120's fixed 55-owner table; and the bounded terminal
outcomes certificate. No application reference, decoration, configuration, context, or runtime
prerequisite changes.

**Public concepts:** Standard `PackageValidationBaselineVersion`, existing claim evidence, logical
admission-cell IDs with deterministic/native lane kind, ordinary Git commit identity, and the bounded
R13 certificate. Each maps directly to compatibility, executable proof, candidate binding, or removed-
owner reconciliation; no release manifest, run-result ledger, compatibility format, or universal test
framework is added.

**Docs read:** `CLAUDE.md` and the initiative charter establish support/ownership discipline;
ARCH-0110 keeps `main` PR validation separate from `main` publication; ARCH-0118 keeps active product
truth in evaluated projects plus claims; ARCH-0109 establishes bounded process ownership; the packaging
and test-authoring guides require project-local version ownership, real hosts, explicit infrastructure,
and focused validation; ARCH-0120 and this card own the bootstrap outcome.

**Code read:** `Directory.Build.props`/targets and representative package projects own standard
MSBuild metadata; `RepositoryInspector`, `PackageProject`, `PackagingConstants`,
`ProductSurfaceCompiler`, and their focused tests own evaluated graph/claim truth; `pr-gate.yml` owns
the integration boundary; `test-bootstrap.ps1`, `forge-verify.ps1`, `KoanIntegrationHost`,
`KoanDataSpec`, and AODB bases expose the current deadline/result/ambient gaps; the disabled canary is
an empty credential-free workflow shell.

**Reusing:** Standard SDK package validation; project-local csproj/version ownership; the existing
packaging compiler and process helper; deterministic generated product projections; GitHub's merge
candidate SHA; xUnit/TRX outputs; family-owned suites and runners; and ARCH-0120's immutable baseline.

**Creating new:** Each child card lists its exact files. Stable tooling identifiers remain in
`PackagingConstants`; claim/result DTOs remain in the packaging models; scripts own only process
orchestration; the R13 reconciler remains separate from `ProductSurfaceCompiler`.

**Coalescence:** Keep active package maturity and graph law in `ProductSurfaceCompiler`; keep API
compatibility in standard MSBuild and the missing-baseline publication guard in the existing packaging
tool/main publisher; rebuild process-result admission around explicit named results;
repurpose the existing disabled canary for native validation; add one bounded R13 reconciler rather
than teaching the active product compiler about removed owners. Delete the canary noop and any
exit-code-only admission path each child supersedes.

**Ergonomics:** Application authors see no new concepts. Package owners add one standard baseline
property and, only when their claim changes, exact executable evidence. Reviewers receive one
deterministic gate plus one always-visible native result whose N/A/pass/failure is self-explaining.
Coding models can map each failure to a project, claim, cell, phase, and commit without reconstructing
workflow state.

**Constraints satisfied:** no inline HTTP or data-access changes; no runtime hot path; stable IDs are
centralized; standard .NET/GitHub mechanisms remain primary; generated docs change only through their
compiler; family test semantics remain family-owned; no publication, credential, private evidence,
full ratchet, or package promotion occurs in the bootstrap.

**Risks:** Repository evaluation proves 38 supported owners but only 35 are assembly-bearing;
`Sylin.Koan`, `Sylin.Koan.App`, and `Sylin.Koan.Templates` are content-only and therefore retain
artifact/dependency-shape checks rather than SDK API compatibility. R13-03 repaired Forge's result
and streaming gaps, but existing native AODB bases still contain direct ambient mutation that Wave 0
must remove before later provider admissions rely on them. R13-04 deliberately treats shared build,
tooling, workflow, and family-test-kit changes as conservative all-claim applicability boundaries.

## Per-wave execution contract

Each wave:

1. confirms the existing R11 disposition and freezes the user-capability claim, non-claims, and
   corrective failures before production repair;
2. separates a shared family guarantee from provider-specific deltas without inventing universal
   parity;
3. adds the smallest missing red evidence and uses the existing family test kits wherever they own
   the right semantics;
4. parses named TRX/xUnit admission cells and treats every required skip, unknown outcome, missing
   result, nonzero test-process exit, or bounded startup/execution/readiness/teardown timeout as
   failed certification with cleanup;
5. proves exact owner tests, at least one package consumer, artifact/dependency shape, and the
   candidate public API before admission;
6. changes the supported claim and project-local 0.20 intent atomically, or removes the owner after
   completed absorption/migration/retirement;
7. compiles the product surface and focused package set, then observes the dependency-closed wave
   before the next wave uses it as a floor.

Before the first admission, the bootstrap gate makes the pull-request gate run the real product
compiler, validate declared evidence identity and graph/version/claim agreement, and reject
generated-output drift. It also adds the always-emitted, branch-protected, credential-free native
admission check whose applicability and exact cell IDs derive from affected claims and whose results
bind to the exact merge candidate. ARCH-0110's existing `main`-boundary job remains the only
publisher.

## R12 coordination

R12-07 remains a bounded preview-evolution outcome, not the owner of this epic. The first R13 wave
that publishes a new supported dependency-closed set must:

- upgrade an application created from the already public 0.20 template/package line;
- inject or reproduce an interrupted partial publication and prove ordinary rerun recovery through
  immutable package identities and `--skip-duplicate`;
- record the exact commit, packages, public versions, consumer result, and recovery observation.

R12-07 also owns the resulting feedback triage and final maintainer go/no-go record. That evidence
closes R12 independently. R13 continues through Wave 9 without making preview completion wait on
public Agyo or Zen Garden handoffs.

## Evidence economy

- Run focused owner, family-conformance, provider-delta, and consumer tests during a wave.
- Add deterministic suites to the existing `main` pull-request gate.
- Use the native validation lane only for required container, protocol, or local-model cells, bound
  to the exact candidate commit and carrying no publication credential.
- Do not rerun completed Tenancy or Classification evidence unless a dependency or public contract
  changes.
- Do not repeat R11 package polish or topology review unless current support evidence disproves its
  accepted result.
- Run the complete release ratchet once after the terminal graph converges.

## Acceptance

R13 passes only when:

1. all 55 ARCH-0120 baseline owners appear exactly once as supported on 0.20, absorbed, publicly
   migrated, or retired;
2. the existing product compiler passes its bidirectional support/version/dependency law for the
   active graph, and bounded final R13 certification reconciles that graph plus accepted wave
   outcomes against the fixed 55-owner baseline;
3. every R13-added or materially changed claim names exact runnable owner evidence and a consumer
   journey, with every required native cell green;
4. every retained assembly-bearing owner has a first-0.20 package/API baseline and later 0.20 patches
   cannot waive a genuine public break; content-only owners retain artifact/dependency-shape and
   isolated-consumer checks;
5. generated truth, package-owned contracts/non-claims, and current public guidance agree;
6. R12-07 passes with its upgrade/recovery proof, feedback triage, and maintainer go/no-go record;
7. the final full release ratchet passes after all source, claims, versions, migrations, and docs
   converge;
8. the maintainer receives the final 55-owner disposition and a concise go/no-go record for the next
   compatibility tier.

## Stop conditions

- Stop on a mechanical version sweep, package-row-only claim, or support inference from test count.
- Stop when a required provider/native cell is absent, skipped, or replaced by a weaker fake than the
  accepted guarantee permits.
- Stop before changing an R11 topology disposition without direct contrary evidence and an explicit
  recorded decision.
- Stop before supporting a migration-target AI owner without reversing its accepted public home.
- Stop if public support would depend on private dogfood, private artifacts, or unverifiable private
  consumers.
- Stop if the work creates another maturity ledger, release coordinator, universal test framework,
  or compatibility format.
