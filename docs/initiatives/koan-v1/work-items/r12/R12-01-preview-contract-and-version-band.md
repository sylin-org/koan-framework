---
type: SPEC
domain: framework
title: "R12-01 - Define the 0.20 Preview Contract and Version Band"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-19
  status: in-progress
  scope: preview intent, version grammar, compatibility boundary, support posture, and exact inventory plan
---

# R12-01 — Define the 0.20 preview contract and version band

- Tranche: `T7B — public product maturity`
- Status: `in-progress`
- Depends on: passed R11 and the exact local R08-05 candidate evidence
- Unlocks: bounded preview repairs, coherent product classification, mass public narrative alignment,
  and the exact selective 0.20 promotion wave
- Owner: the human meaning and deterministic package expression of the 0.20 maturity cycle

## Meaningful outcome

A developer can tell what “0.20 preview” means before installing it: which channel they are joining,
which compatibility promises exist, which capabilities form the recommended spine, how packages
advance independently, how to report a problem, and what Koan deliberately does not promise yet.

Maintainers receive one exact version grammar. They do not hand-edit package outputs, synchronize every
future release, or interpret “pre-release” differently across NuGet, documentation, and support policy.

## User decisions already accepted

- The next epic is the road to pre-release and the pre-release is a maturity cycle.
- Only packages whose contracts Koan guarantees are promoted to the 0.20 quality band from `0.20.0`
  version intent; other packages keep their truthful maturity and version line.
- The current “less but more meaningful moving parts” architecture remains the baseline.
- The cycle includes a mass realignment that creates a coherent narrative across all public-facing
  content.
- The objective is a version worthy of external testing, not another internal architecture campaign.

## 2026-07-19 read-only inventory

No version, claim, package, production source, or remote state changed during this inventory.

### Current facts

- The active graph contains 93 package owners. Their current major/minor intents are: three at `0.1`,
  68 at `0.17`, 16 at `0.18`, four at `0.19`, and two at `0.20`.
- Product truth contains 26 claims: 15 `verified`, nine `demonstrated`, one `experimental`, and one
  `specified`. No claim is yet `supported-foundation` or `supported-extension`, so no existing maturity
  label alone is the accepted 0.20 guarantee set.
- The 15 verified claims name 31 packages directly. Their transitive public package closure adds six
  currently unassessed dependencies: `Sylin.Koan.Core`, `Sylin.Koan.Cache.Abstractions`,
  `Sylin.Koan.Media.Abstractions`, `Sylin.Koan.Storage`, `Sylin.Koan.Storage.Abstractions`, and
  `Sylin.Koan.Web.Auth.Abstractions`.
- Adding the entry bundle, application bundle, and templates creates a maximum 40-package candidate
  boundary. This is a discovery ceiling, not an accepted promotion list.
- Package version intent is exactly `major.minor`; the repository rejects a literal `0.20.0` value in
  `version.json`. NBGV owns the patch. A new owner entering the line normally declares `"version":
  "0.20"`; its lineage commit determines the exact `0.20.x` identity.
- Communication already declares `0.20` intent. At the current source its direct NBGV previews are
  `Sylin.Koan.Communication` `0.20.3` and RabbitMQ `0.20.6`; resetting either to `.0` would fight
  deterministic history and is not proposed.
- Current compatibility bands and release-wave parsing intentionally accept stable three-part package
  versions. GitHub release escrow explicitly rejects a Release marked prerelease. Introducing
  `-preview.N` would therefore require a second compatibility and custody mechanism.

### Provisional guarantee slate

The evidence supports assessing this 35-package slate for 0.20 admission. None is promoted merely by
appearing here; R12-02/R12-03 must either admit its exact guarantee or remove it from the slate.

| Responsibility | Exact package owners | Count |
|---|---|---:|
| entry and foundation | `Sylin.Koan`, `Sylin.Koan.App`, `Sylin.Koan.Templates`, `Sylin.Koan.Core`, `Sylin.Koan.Data.Abstractions`, `Sylin.Koan.Data.Core`, `Sylin.Koan.Data.Connector.Json`, `Sylin.Koan.Communication`, `Sylin.Koan.Web` | 9 |
| local test data | `Sylin.Koan.Data.Connector.InMemory` | 1 |
| cache | `Sylin.Koan.Cache`, `Sylin.Koan.Cache.Abstractions` | 2 |
| Canon | `Sylin.Koan.Canon`, `Sylin.Koan.Canon.Web` | 2 |
| Classification | `Sylin.Koan.Classification`, `Sylin.Koan.Classification.Contracts` | 2 |
| distributed Communication | `Sylin.Koan.Communication.Connector.RabbitMq` | 1 |
| Jobs | `Sylin.Koan.Jobs` | 1 |
| Web projections | `Sylin.Koan.Web.Extensions`, `Sylin.Koan.Web.OpenApi`, `Sylin.Koan.Web.OpenGraph`, `Sylin.Koan.Web.Sse` | 4 |
| authentication, identity, trust, and tenancy | `Sylin.Koan.Web.Auth.Abstractions`, `Sylin.Koan.Web.Auth`, `Sylin.Koan.Web.Auth.Server`, `Sylin.Koan.Identity`, `Sylin.Koan.Identity.Web`, `Sylin.Koan.Identity.Tenancy`, `Sylin.Koan.Security.Trust`, `Sylin.Koan.Tenancy`, `Sylin.Koan.Tenancy.Web` | 9 |
| MCP | `Sylin.Koan.Mcp`, `Sylin.Koan.Mcp.Explorer`, `Sylin.Koan.Mcp.Operations` | 3 |
| observability | `Sylin.Koan.Observability` | 1 |

Media is deliberately conditional rather than silently included. Its verified claim adds
`Sylin.Koan.Media.Core` and `Sylin.Koan.Media.Web`, but their public closure also requires
`Sylin.Koan.Media.Abstractions`, `Sylin.Koan.Storage`, and `Sylin.Koan.Storage.Abstractions`. Storage
has no assessed product claim and retains PMC-033's unused-activation defect; the five-package group
may join 0.20 only if Storage earns its own guarantee and the layered boundary is corrected. Otherwise
Media stays below 0.20 without losing its current verified evidence.

### Concerns attached to the slate

R12-02 must re-evaluate the complete current register, with these direct admission questions:

- foundation/first use: Windows EventLog behavior (PMC-025), exact public installation, and template
  upgrade/recovery;
- Data/Web: provider-correct public filtering (PMC-007) and case-colliding Entity models (PMC-015);
- Jobs: the `JobMetric.Count` Entity-language collision (PMC-001);
- MCP: legacy transport option names and JSON casing contract (PMC-002/004);
- Communication: details-required authoring feedback and explicit single-application/wire-evolution
  limits (PMC-021/023);
- Media/Storage: derivative lifecycle, connector-owned evidence, and unused layered activation
  (PMC-022/027/033);
- Auth/Tenancy: passwords/MFA and distributed-safe invitations remain explicit non-claims unless their
  full ceremonies are separately earned (PMC-034/035);
- release and repository evidence: warning/doc policy, buffered progress, aggregate certification,
  build-fixture isolation, SQLite test drift, and stale connector references
  (PMC-003/006/009/020/024/028/032).

AI/vector, remote Data providers, Backup, and other demonstrated/experimental/unassessed packages stay
outside the initial slate. Their PMCs remain real, but they do not block 0.20 unless a supported package
depends on their guarantee.

## Proposed architecture checkpoint

1. **Channel:** publish ordinary stable-format `0.20.x` NuGet identities. “Preview” describes Koan's
   pre-1.0 maturity cycle, not a NuGet `-preview.N` suffix. Installation therefore needs no
   `--prerelease`, and the existing compatibility/escrow machinery remains single-path.
2. **Admission:** a package reaches 0.20 only when it is named by an accepted
   `supported-foundation` or `supported-extension` claim. `verified` is necessary evidence but is not
   automatic admission.
3. **Dependency closure:** every public Koan dependency of a promoted package must be inside an accepted
   support claim. Add a true dependency contract to the relevant claim, create an earned foundation
   claim, or withhold the dependent package; never promote dependencies mechanically.
4. **Version expression:** admitted owners declare `"version": "0.20"`; NBGV owns exact patches.
   Existing valid 0.20 history is preserved. No stamping script, package list, or exact `.0` override is added.
5. **Compatibility:** within an admitted package's 0.20 patch line, Koan makes no intentional breaking
   public-contract change. The next deliberate pre-1.0 breaking tier is a later minor line and triggers
   the existing reverse-dependent closure and bounded dependency ranges.
6. **Mixed maturity:** the first coherent public bootstrap may publish missing lower-line packages as
   dependencies or experiments. Their lower version and maturity remain visible; only 0.20 carries the
   guarantee signal.
7. **Entry timing:** bundle, App, and Templates are provisional 0.20 candidates. Their version edits
   occur only after their support contract is accepted and the exact package-only candidate is green;
   public observation completes, rather than substitutes for, that evidence.

## Questions this card must settle

1. Which current capability contracts are strong enough to become `supported-foundation` or
   `supported-extension`, and which exact package owners carry each guarantee?
2. Does each guaranteed package's public dependency closure also carry supportable contracts? If not,
   the parent is not promoted until the boundary is corrected; transitive membership alone never grants 0.20.
3. How does NBGV derive exact output from `0.20` owner intent without erasing independent per-project
   patch lineage afterward?
4. Which dependency bands allow promoted and non-promoted owners to coexist without implying that a
   lower-maturity dependency is guaranteed?
5. Which platforms, SDK, package sources, security posture, and operational prerequisites are supported?
6. What is the recommended capability spine, and how are demonstrated, experimental, and unassessed
   extensions presented without implying equal support?
7. What feedback belongs in a defect, a documentation correction, a provider limitation, or a future
   proposal?

## Evidence to read first

- `version.json` files for every active package owner and repository-level NBGV policy;
- `Directory.Build.props`, `Directory.Packages.props`, evaluated project package metadata, and generated
  dependency bands;
- `ReleaseLineageCompiler`, `ReleasePlanner`, package version-policy tests, and exact R08-05 lineage;
- `docs/engineering/versioning.md`, `docs/engineering/packaging.md`, ARCH-0110, and current release docs;
- generated product surface, package quality report, active claims, and public install instructions;
- official NuGet and Semantic Versioning rules if the channel-label decision needs external verification.

## Focused discovery and coalescence assessment

- **User's business sentence:** “Let people test one coherent 0.20 Koan without pretending the
  framework is already 1.0.”
- **Smallest public expression:** one documented install command naming the chosen channel; no package
  matrix or version calculation.
- **Complete user action surface:** install template or package, accept the documented preview policy,
  run the application, inspect facts, and report observed behavior through the recorded feedback path.
- **Guarantee and correction:** a 0.20 identity means Koan accepts the recorded compatibility boundary
  for that package. If a dependency cannot support that guarantee, the dependent package does not
  promote merely by declaration; the boundary is corrected, narrowed, or left below 0.20.
- **Additional public concepts:** only the standard SemVer/NuGet channel, compatibility statement, and
  maturity labels. No Koan-specific release tier is added.
- **Current owners:** project-local `version.json` owns compatibility intent; NBGV owns exact identity;
  evaluated lineage owns selection; NuGet owns package semantics; generated claims own maturity.
- **Coalescence:** extend those owners. Do not add a version service, package catalogue, release CLI, or
  hand-maintained synchronized manifest.
- **Ergonomics:** ordinary users see one install instruction and one support statement. Independent
  package mechanics remain invisible unless diagnosing exact provenance.

## Scope

### In

- complete read-only inventory of active version intent, maturity evidence, claim ownership, public
  dependency closure, and exact generated identity;
- exact 0.20 channel, guarantee-admission, and compatibility decision;
- selective package promotion and dependency-band design;
- preview support/non-support and feedback contract;
- a recorded architecture checkpoint before mass version edits;
- focused version/compiler/workflow tests needed to pin the decision.

### Out

- changing package versions before the checkpoint is accepted;
- rewriting the full public documentation surface;
- runtime feature repairs;
- packing or publishing the selective 0.20 wave;
- any remote repository, NuGet, tag, or Release mutation.

## Verification

- exact inventory of active owner intent and projected package identities;
- focused NBGV/version policy, lineage, planner, and workflow contract tests;
- a synthetic mixed-maturity fixture proving selective promotion and independent owner advancement;
- dependency-band proof for unchanged and independently advanced consumers;
- documentation lint for the preview contract pages;
- `git diff --check` and explicit confirmation that `tmp/` remains unstaged.

## Acceptance additions

1. “0.20 preview” has one exact NuGet/SemVer meaning.
2. Every promoted owner maps to an accepted guarantee; every non-promoted owner remains visibly outside
   the 0.20 signal without being mislabeled as broken or retired.
3. Selective promotion and later independent waves require no operator package/version input beyond the
   accepted guarantee set encoded in product truth.
4. Compatibility, support, security, and feedback expectations are concise and non-conflicting.
5. The proposal is presented as an architecture checkpoint before version files or release behavior change.

## Stop conditions

- Stop if a proposed shortcut synchronizes all package versions or promotes a package without a guarantee.
- Stop if exact package identity requires a maintained list or hand-authored release manifest.
- Stop if NuGet prerelease behavior and the documentation meaning of “preview” disagree.
- Stop before any version edit, full pack, or remote mutation until the checkpoint is accepted.
