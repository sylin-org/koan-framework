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

## Questions this card must settle

1. Does the NuGet identity use a true SemVer prerelease suffix such as `-preview.N`, or does “preview”
   describe the pre-1.0 `0.20.*` quality band while NuGet receives ordinary `0.20.x` versions?
2. Which current capability contracts are strong enough to become `supported-foundation` or
   `supported-extension`, and which exact package owners carry each guarantee?
3. Does each guaranteed package's public dependency closure also carry supportable contracts? If not,
   the parent is not promoted until the boundary is corrected; transitive membership alone never grants 0.20.
4. How does NBGV derive exact output from `0.20.0` owner intent without erasing independent per-project
   patch lineage afterward?
5. Which dependency bands allow promoted and non-promoted owners to coexist without implying that a
   lower-maturity dependency is guaranteed?
6. What source/binary/configuration compatibility should testers expect within the preview line?
7. Which platforms, SDK, package sources, security posture, and operational prerequisites are supported?
8. What is the recommended capability spine, and how are demonstrated, experimental, and unassessed
   extensions presented without implying equal support?
9. What feedback belongs in a defect, a documentation correction, a provider limitation, or a future
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
