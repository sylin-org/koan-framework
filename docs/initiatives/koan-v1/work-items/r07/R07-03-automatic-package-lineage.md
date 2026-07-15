---
type: SPEC
domain: framework
title: "R07-03 - Automate Breaking Package Lineage"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: passed
  scope: automatic breaking lineage, full reverse closure, exact artifacts, and clean-room applications
---

# R07-03 — Automate breaking package lineage

- Tranche: `T6 — semantic capability ring`
- Status: `passed`
- Depends on: R07-02, ARCH-0085, and ARCH-0110
- Unlocks: R07-04 release-floor repair and, once it passes, the clean Lifecycle 0.18 break
- Owner: Koan.Packaging and the serialized `dev` release workflow

## Meaningful outcome

A maintainer expresses compatibility intent only by changing source and the owning package's
`version.json`. Advancing `dev` then automatically:

1. finds every package affected by a breaking-tier root;
2. gives unchanged reverse dependents fresh immutable identities;
3. builds, tests, packs, and proves the exact resulting Git tree; and
4. replays the same source/version wave safely when publication stopped partway through.

There is no package checklist, version spreadsheet, patch calculation, or operator-selected release
set. Ordinary leaf changes remain independent.

## Decisions

### DECIDED

- Evaluated MSBuild projects and `ProjectReference`s are the only package graph authority.
- One `PackageGraph` owns dependency resolution, topological order, and transitive reverse closure.
- One `ReleaseLineageCompiler` owns the durable Git projection used to mint otherwise-unchanged
  dependent identities.
- `version.json` remains the human compatibility signal. Before 1.0, a minor advance is breaking;
  from 1.0 onward, a major advance is breaking.
- The protected workflow maintains one serialized `automation/package-lineage-dev` branch. Each
  accepted source advancement becomes one lineage commit containing the exact prior/current source
  tree delta, deterministic markers, and inspectable lineage state. Source merge topology is not
  imported because it advances unrelated NBGV heights.
- A generated package marker is written only when a closure member would otherwise retain its prior
  NBGV identity. The marker names the source commit and breaking roots that required the rebuild.
- The first lineage is a deliberate all-owner bootstrap. Every later lineage state stores the exact
  version identity of every package owner; after bootstrap, historical versions are never reinterpreted
  with today's SDK or NBGV. Bootstrap requires its predecessor's package inventory to remain evaluable
  by the pinned toolchain. A conservative map combines known shared policy with evaluated external
  packed inputs and fans changes out to mapped package owners.
- Manifests distinguish `SourceCommit` from `VersionCommit`; version comparison starts at
  `PreviousVersionCommit`. Package metadata, resumable state, packing, and release evidence bind to
  `VersionCommit`.
- Existing registry reconciliation remains the recovery mechanism for identities that were compiled
  but not published by an earlier run. Same-source partial symbol/state replay is supported; automatic
  cross-event recovery after the lineage tip advances remains the bounded
  [PMC-016](../../POST-CYCLE-TODO.md#current-register) edge.
- Known shared-input paths remain mapped as deletion tombstones. Lineage state does not yet retain an
  arbitrary external pack path found only by a prior MSBuild evaluation; its later deletion or rename
  remains the bounded [PMC-017](../../POST-CYCLE-TODO.md#current-register) edge.
- Package deletion/rename, reserved marker collisions, non-forward source history, graph cycles, and
  incomplete closure fail before packing or publication.
- The initial implementation changes no application/runtime API and does not publish, push, tag, or
  release from a developer checkout.

### DEFAULT

- One serialized workflow job favors correctness and inspectability over maximum release throughput.
- Source-event identity remains visible in summaries alongside the exact version commit.

### OPEN

- Package deletion and rename may gain an explicit migration protocol later. They are unsupported in
  the first automatic-lineage contract rather than inferred from incomplete after-tree evidence.

## Architecture guardrails

```text
dev source advancement
        |
serialized lineage compiler
        |
version.json breaking roots -> PackageGraph reverse closure
        |                         |
source-changed members       deterministic markers for unchanged members
        \_________________________/
                    |
             exact VersionCommit
                    |
        build/test -> plan -> pack/prove -> publish
```

- Git owns change intent and durable identity; the registry is reconciliation evidence, not a version
  allocator.
- The lineage compiler mutates only its dedicated local branch and reserved marker/state files.
- The version branch is a linear source-tree projection. `SourceCommit` is recorded provenance, not a
  merge parent or a substitute for artifact identity.
- No production package graph is parsed from XML, inferred from names, or maintained in a second list.
- No later workflow run may calculate or mint package identities before earlier lineage mutation has
  completed.
- Every pack/metadata check uses the exact version commit, never the source event by implication.

## Red/green plan

1. **Complete.** Add `PackageGraph` tests for dependency order, complete reverse closure,
   independence, unknown roots, and cycle rejection.
2. **Complete.** Extract graph behavior from `ReleasePlanner`; one graph now owns forward order and
   reverse closure.
3. **Complete.** Add compiler-decision tests for breaking tiers, actual-identity marker selection,
   deterministic causes, continuity, and reserved paths.
4. **Complete.** Implement the Git-native linear lineage compiler and schema-versioned state/marker
   artifacts.
5. **Complete.** Split source, previous-version, and version commit semantics across manifest,
   planner, pack metadata, resumable state, console reporting, and release evidence.
6. **Complete.** Serialize the protected workflow before lineage compilation and make its single job
   build/test/pack/publish the exact version commit.
7. **Complete.** Prove two breaking waves, same-source replay, and an unrelated leaf in a disposable
   nested Git repository. Every required dependent gets a distinct identity on both waves while the
   leaf remains independent.
8. **Complete.** Run packaging tests, a real repository offline and registry-reconciled plan, exact
   package/clean-room proof, solution build, workflow structural checks, strict docs, diff, and
   privacy gates before closing the child.
9. **Complete for release machinery.** Harden the foundation with all-owner bootstrap, durable
   per-package identities, mapped shared-input fan-out, exact lineage-tree/parent checks, selected
   dependency-floor proof, pinned release tooling, the complete fail-closed ratchet, and exact
   non-forced release tags. The first whole-solution test execution exposed an existing red baseline;
   R07-04 owns that prerequisite rather than weakening this workflow.

## Verification

- Commits `72db20f3`, `3158ef23`, and their focused hardening retain two production concepts:
  evaluated `PackageGraph` and Git-native `ReleaseLineageCompiler`. Packaging passes 52/52: 49 graph,
  lineage, dependency, schema, workflow, and Git contracts plus all three supported source-application
  proofs. The disposable Git proof includes bootstrap, canonical current version-intent rejection, two
  breaking waves, shared-input fan-out, same-source replay, new-package admission, manual-lineage
  rejection, and unrelated-leaf independence.
- A disposable clone of the complete repository changed only Data.Core's intent from 0.17 to 0.18.
  The compiler derived the exact 81-package breaking closure: one root plus all 80 transitive reverse
  dependents. It generated 78 markers because three members already gained identities from the
  projected source change.
- The offline plan selected and packed all 81 breaking-wave packages. Clean-room restore then failed
  loudly on an unchanged dependency whose current identity was absent from both the offline plan and
  nuget.org. Running the normal registry-reconciled plan selected that identity and 18 other existing
  publication gaps; `--resume` reused the 81 verified artifacts, packed the gaps, and converged.
- The resulting 100-artifact set passed metadata, symbols, compatibility-range, repository-commit,
  and dependency-closure inspection. Package-only FirstUse passed in 4.095s and GoldenJourney in
  10.591s with zero restore/build warnings or errors.
- Importing source merge topology was tested and rejected: the merge parent advanced unrelated NBGV
  heights. Exact source-tree delta projection preserves independent package histories.
- Release solution build, strict docs, skills, structural workflow/YAML, diff, and privacy gates pass.
  No package was published and no remote branch, tag, or release was changed.
- The exact public-release ratchet passes every non-test leg and rejects publication at its
  whole-solution test leg. R07-04 records the isolated failing suites and must turn that gate green
  before Lifecycle; R07-03 does not claim a releasable repository state.

## Acceptance

- A synthetic pre-1.0 minor bump automatically gives the root and every transitive reverse dependent
  a fresh version.
- A source-untouched dependent gets a deterministic Git marker; a source-touched dependent is not
  bumped twice.
- An unrelated package retains its prior identity.
- Omitting a required closure member is impossible through the public planner path and detected by an
  independent closure assertion before publish.
- Re-running the same source event uses the same durable version lineage and registry reconciliation;
  it never reuses a published identity for different bits. Cross-event recovery after partial symbol
  publication is explicitly not claimed until
  [PMC-016](../../POST-CYCLE-TODO.md#current-register) passes its acceptance proof.
- Two successive breaking tiers produce two successive dependent identities; an intervening unrelated
  leaf event does not advance the prior closure.
- The manifest and packed nuspec agree on `VersionCommit`, while `SourceCommit` remains available for
  audit.
- Unsupported history/package-shape changes fail with corrective package/path/source evidence.
- No package is published, no remote ref is changed, and no real release is created by the local proof.
